using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Network;
using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Enums;
using OPNX.Lib.Data.ORM.EventHandlers;
using OPNX.Lib.Data.ORM.Interfaces;
using OPNX.Lib.Data.ORM.Mapping;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Services
{
    public abstract class BaseDataBaseService : DisposableObject, IDataBaseService
    {
        #region Fields
        private int _commandTimeout = 10000;
        private string _connectionString = string.Empty;

        private static readonly ConcurrentDictionary<string, MethodInfo> _cachedGenericMethods = new();

        private readonly IEntityStore _entityStore;
        private readonly ILogger _logger;
        private readonly DataRowMapper _dataRowMapper = new();

        private sealed class TxContext
        {
            public DbConnection? Connection { get; init; }
            public DbTransaction? Transaction { get; init; }
            public int Depth { get; set; }

            public ConcurrentQueue<Action> PendingStoreActions { get; } = [];
            public ConcurrentQueue<EntityChangedEventArgs> PendingEntityEvents { get; } = [];
        }

        private readonly AsyncLocal<TxContext?> _tx = new();

        protected DbConnection? CurrentConnection => _tx.Value?.Connection;
        protected DbTransaction? CurrentTransaction => _tx.Value?.Transaction;
        #endregion

        #region Constructors
        public BaseDataBaseService(string connectionString, ILogger? logger = null)
            : this(connectionString, new EntityStore(), logger)
        {
        }

        public BaseDataBaseService(string connectionString, IEntityStore? entityStore, ILogger? logger = null)
            : base()
        {
            _logger = logger ?? NullLogger.Instance;

            if (!string.IsNullOrEmpty(connectionString))
                ConnectionString = connectionString;

            _entityStore = entityStore ?? throw new ArgumentNullException(nameof(entityStore));
            _entityStore.EntityChanged += EntityStore_EntityChanged;
        }
        #endregion

        #region Properties
        public abstract DatabaseType DBType { get; }
        public bool AutoTransactionForEntityOperations { get; set; } = true;

        public IEntityStore EntityStore { get => _entityStore; }

        public int CommandTimeout
        {
            get => _commandTimeout;
            set => _commandTimeout = value;
        }

        public string ConnectionString
        {
            get { return _connectionString; }
            set
            {
                if (_connectionString == value)
                    return;

                string[]? strArray = null;

                if (!string.IsNullOrEmpty(value))
                {
                    strArray = value.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < strArray.Length; i++)
                    {
                        string str = strArray[i];
                        if (string.IsNullOrEmpty(str)) continue;

                        string[] parts = str.Split('=');
                        if (parts.Length != 2) continue;

                        string key = parts[0].Trim().ToUpperInvariant();
                        string address = parts[1].Trim().ToLowerInvariant();

                        if (key == "SERVER" || key == "HOST")
                        {
                            if (address == "localhost" || address == "127.0.0.1")
                            {
                                if (DBType == DatabaseType.PostgreSQL)
                                    continue;

                                address = NetworkingAddress.GetLocalIPAddress();
                                if (!string.IsNullOrEmpty(address))
                                    strArray[i] = $"{key}={address}";
                            }
                        }
                    }
                }

                _connectionString = strArray == null ? string.Empty : string.Join(";", strArray);
            }
        }
        #endregion

        #region Events
        public event EntityChangedEventHandler? EntityChanged;
        #endregion

        #region Public Methods
        public virtual void LoadDataBase() { }
        public virtual void LoadEntity(Type entityType) { }
        public virtual string GetTableIdentifier(Type entityType) => DatabaseNaming.GetTableName(entityType);

        public DbConnection? OpenDataBase() => OpenDataBase(_connectionString);

        public Task<DbConnection?> OpenDataBaseAsync(CancellationToken cancellationToken = default) => OpenDataBaseAsync(_connectionString, cancellationToken);

        public DbConnection? OpenDataBase(string connectionString)
        {
            if (IsDisposed)
                return null;

            if (CurrentConnection != null)
            {
                if (CurrentConnection.State == ConnectionState.Open)
                    return CurrentConnection;

                return null;
            }

            DbConnection? conn = null;

            try
            {
                conn = CreateConnection(connectionString);
                conn.Open();
                return conn;
            }
            catch (Exception ex)
            {
                try
                {
                    conn?.Dispose();
                }
                catch { }

                _logger.LogError(ex, "{Message}", ex.Message);
            }

            return null;
        }

        public async Task<DbConnection?> OpenDataBaseAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
                return null;

            if (CurrentConnection != null)
                return CurrentConnection.State == ConnectionState.Open ? CurrentConnection : null;

            DbConnection? connection = null;
            try
            {
                connection = CreateConnection(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch (OperationCanceledException)
            {
                if (connection != null)
                    await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                if (connection != null)
                    await connection.DisposeAsync().ConfigureAwait(false);
                _logger.LogError(ex, "{Message}", ex.Message);
                return null;
            }
        }

        public void CloseDataBase(DbConnection dbConnection)
        {
            if (dbConnection == null)
                return;

            if (ReferenceEquals(dbConnection, CurrentConnection))
                return;

            try
            {
                dbConnection.Dispose();
            }
            catch
            {
            }
        }

        public async ValueTask CloseDataBaseAsync(DbConnection dbConnection)
        {
            if (dbConnection == null || ReferenceEquals(dbConnection, CurrentConnection))
                return;

            try
            {
                await dbConnection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        public void ExecuteInTransaction(Action work)
        {
            ArgumentNullException.ThrowIfNull(work);
            ExecuteInTransaction(() =>
            {
                work();
                return true;
            });
        }

        public void ExecuteInTransaction(Action<IDataBaseService> work)
        {
            ArgumentNullException.ThrowIfNull(work);
            ExecuteInTransactionCore(() =>
            {
                work(this);
                return true;
            }, true);
        }

        public TResult ExecuteInTransaction<TResult>(Func<IDataBaseService, TResult> work)
        {
            ArgumentNullException.ThrowIfNull(work);
            return ExecuteInTransactionCore(() => work(this), true);
        }

        public TResult ExecuteInTransaction<TResult>(Func<TResult> work) => ExecuteInTransactionCore(work, false);

        private TResult ExecuteInTransactionCore<TResult>(Func<TResult> work, bool throwOnError)
        {
            ArgumentNullException.ThrowIfNull(work);
            if (_tx.Value != null)
            {
                _tx.Value.Depth++;
                try
                {
                    return work();
                }
                finally
                {
                    _tx.Value.Depth--;
                }
            }

            var conn = OpenDataBase();
            if (conn == null)
            {
                if (throwOnError)
                    throw new InvalidOperationException("Failed to open the database connection for the transaction.");
                return default!;
            }

            DbTransaction? tx = null;
            TxContext ctx;

            try
            {
                tx = conn.BeginTransaction();

                ctx = new TxContext
                {
                    Connection = conn,
                    Transaction = tx,
                    Depth = 1
                };

                _tx.Value = ctx;

                var result = work();

                tx.Commit();

                FlushPendingStoreActions(ctx);
                FlushPendingEntityEvents(ctx);

                return result;
            }
            catch (Exception ex)
            {
                try
                {
                    tx?.Rollback();
                }
                catch { }
                _logger.LogError(ex, "{Message}", ex.Message);
                if (throwOnError)
                    throw;
                return default!;
            }
            finally
            {
                _tx.Value = null;

                try
                {
                    tx?.Dispose();
                }
                catch { }

                CloseDataBase(conn);
            }
        }

        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);
            await ExecuteInTransactionAsync(async token =>
            {
                await work(token).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        public Task ExecuteInTransactionAsync(Func<IDataBaseService, CancellationToken, Task> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);
            return ExecuteInTransactionAsyncCore(async token =>
            {
                await work(this, token).ConfigureAwait(false);
                return true;
            }, true, cancellationToken);
        }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<IDataBaseService, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);
            return ExecuteInTransactionAsyncCore(token => work(this, token), true, cancellationToken);
        }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default) => ExecuteInTransactionAsyncCore(work, false, cancellationToken);

        private async Task<TResult> ExecuteInTransactionAsyncCore<TResult>(Func<CancellationToken, Task<TResult>> work, bool throwOnError, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(work);
            if (_tx.Value != null)
            {
                _tx.Value.Depth++;
                try
                {
                    return await work(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _tx.Value.Depth--;
                }
            }

            DbConnection? connection = await OpenDataBaseAsync(cancellationToken).ConfigureAwait(false);
            if (connection == null)
            {
                if (throwOnError)
                    throw new InvalidOperationException("Failed to open the database connection for the transaction.");
                return default!;
            }

            DbTransaction? transaction = null;
            TxContext? context = null;
            try
            {
                transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                context = new TxContext { Connection = connection, Transaction = transaction, Depth = 1 };
                _tx.Value = context;

                TResult result = await work(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                FlushPendingStoreActions(context);
                FlushPendingEntityEvents(context);
                return result;
            }
            catch (OperationCanceledException)
            {
                if (transaction != null)
                {
                    try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                if (transaction != null)
                {
                    try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                }
                _logger.LogError(ex, "{Message}", ex.Message);
                if (throwOnError)
                    throw;
                return default!;
            }
            finally
            {
                _tx.Value = null;
                if (transaction != null)
                    await transaction.DisposeAsync().ConfigureAwait(false);
                await CloseDataBaseAsync(connection).ConfigureAwait(false);
            }
        }

        public virtual int ExecuteNonQuery(string sqlQuery, List<KeyValuePair<string, object>> paramList) => int.MinValue;

        public virtual Task<int> ExecuteNonQueryAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default) => Task.FromResult(int.MinValue);

        public virtual DataTable? ExecuteReader(string sqlQuery, List<KeyValuePair<string, object>> paramList) => null;

        public virtual Task<DataTable?> ExecuteReaderAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default) => Task.FromResult<DataTable?>(null);

        public virtual IReadOnlyList<T> Query<T>(string sqlQuery, List<KeyValuePair<string, object>> paramList) => _dataRowMapper.Map<T>(ExecuteReader(sqlQuery, paramList), typeof(IEntity).IsAssignableFrom(typeof(T)) ? _entityStore : null);

        public virtual async Task<IReadOnlyList<T>> QueryAsync<T>(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default) => _dataRowMapper.Map<T>(await ExecuteReaderAsync(sqlQuery, paramList, cancellationToken).ConfigureAwait(false), typeof(IEntity).IsAssignableFrom(typeof(T)) ? _entityStore : null);

        public virtual object? ExecuteScalar(string sqlQuery, List<KeyValuePair<string, object>> paramList) => null;

        public virtual Task<object?> ExecuteScalarAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);

        public virtual int InsertEntity<T>(T insertEntity) where T : IEntity
        {
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransaction(() => InsertEntityCore(insertEntity));

            return InsertEntityCore(insertEntity);
        }

        public virtual Task<int> InsertEntityAsync<T>(T insertEntity, CancellationToken cancellationToken = default) where T : IEntity
        {
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransactionAsync(token => InsertEntityCoreAsync(insertEntity, token), cancellationToken);

            return InsertEntityCoreAsync(insertEntity, cancellationToken);
        }

        public virtual int BatchInsert<T>(IReadOnlyList<T> insertEntities) where T : IEntity
        {
            ArgumentNullException.ThrowIfNull(insertEntities);
            if (insertEntities.Count == 0)
                return 0;

            return ExecuteInTransaction(() =>
            {
                int insertedCount = 0;
                foreach (T insertEntity in insertEntities)
                {
                    ArgumentNullException.ThrowIfNull(insertEntity);
                    if (InsertEntityCore(insertEntity) <= 0)
                        throw new InvalidOperationException($"Failed to insert {typeof(T).Name} in batch.");

                    insertedCount++;
                }

                return insertedCount;
            });
        }

        public virtual Task<int> BatchInsertAsync<T>(IReadOnlyList<T> insertEntities, CancellationToken cancellationToken = default) where T : IEntity
        {
            ArgumentNullException.ThrowIfNull(insertEntities);
            if (insertEntities.Count == 0)
                return Task.FromResult(0);

            return ExecuteInTransactionAsync(async token =>
            {
                int insertedCount = 0;
                foreach (T insertEntity in insertEntities)
                {
                    ArgumentNullException.ThrowIfNull(insertEntity);
                    if (await InsertEntityCoreAsync(insertEntity, token).ConfigureAwait(false) <= 0)
                        throw new InvalidOperationException($"Failed to insert {typeof(T).Name} in batch.");
                    insertedCount++;
                }
                return insertedCount;
            }, cancellationToken);
        }

        public virtual bool UpdateEntity<T>(T updateEntity) where T : IEntity
        {
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransaction(() => UpdateEntityCore(updateEntity));

            return UpdateEntityCore(updateEntity);
        }

        public virtual Task<bool> UpdateEntityAsync<T>(T updateEntity, CancellationToken cancellationToken = default) where T : IEntity
        {
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransactionAsync(token => UpdateEntityCoreAsync(updateEntity, token), cancellationToken);

            return UpdateEntityCoreAsync(updateEntity, cancellationToken);
        }

        public virtual int BatchUpdate<T>(IReadOnlyList<T> updateEntities) where T : IEntity
        {
            ArgumentNullException.ThrowIfNull(updateEntities);
            if (updateEntities.Count == 0)
                return 0;

            return ExecuteInTransaction(() =>
            {
                int updatedCount = 0;
                foreach (T updateEntity in updateEntities)
                {
                    ArgumentNullException.ThrowIfNull(updateEntity);
                    if (!UpdateEntityCore(updateEntity))
                        throw new InvalidOperationException($"Failed to update {typeof(T).Name} in batch.");

                    updatedCount++;
                }

                return updatedCount;
            });
        }

        public virtual Task<int> BatchUpdateAsync<T>(IReadOnlyList<T> updateEntities, CancellationToken cancellationToken = default) where T : IEntity
        {
            ArgumentNullException.ThrowIfNull(updateEntities);
            if (updateEntities.Count == 0)
                return Task.FromResult(0);

            return ExecuteInTransactionAsync(async token =>
            {
                int updatedCount = 0;
                foreach (T updateEntity in updateEntities)
                {
                    ArgumentNullException.ThrowIfNull(updateEntity);
                    if (!await UpdateEntityCoreAsync(updateEntity, token).ConfigureAwait(false))
                        throw new InvalidOperationException($"Failed to update {typeof(T).Name} in batch.");
                    updatedCount++;
                }
                return updatedCount;
            }, cancellationToken);
        }

        public virtual bool DeleteEntity<T>(T deleteEntity) where T : IEntity
        {
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransaction(() => DeleteEntityCore(deleteEntity));

            return DeleteEntityCore(deleteEntity);
        }

        public virtual Task<bool> DeleteEntityAsync<T>(T deleteEntity, CancellationToken cancellationToken = default) where T : IEntity
        {
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransactionAsync(token => DeleteEntityCoreAsync(deleteEntity, token), cancellationToken);

            return DeleteEntityCoreAsync(deleteEntity, cancellationToken);
        }

        public virtual int BatchDelete<T>(IReadOnlyList<T> deleteEntities) where T : IEntity
        {
            ArgumentNullException.ThrowIfNull(deleteEntities);
            if (deleteEntities.Count == 0)
                return 0;

            return ExecuteInTransaction(() =>
            {
                int deletedCount = 0;
                foreach (T deleteEntity in deleteEntities)
                {
                    ArgumentNullException.ThrowIfNull(deleteEntity);
                    if (!DeleteEntityCore(deleteEntity))
                        throw new InvalidOperationException($"Failed to delete {typeof(T).Name} in batch.");

                    deletedCount++;
                }

                return deletedCount;
            });
        }

        public virtual Task<int> BatchDeleteAsync<T>(IReadOnlyList<T> deleteEntities, CancellationToken cancellationToken = default) where T : IEntity
        {
            ArgumentNullException.ThrowIfNull(deleteEntities);
            if (deleteEntities.Count == 0)
                return Task.FromResult(0);

            return ExecuteInTransactionAsync(async token =>
            {
                int deletedCount = 0;
                foreach (T deleteEntity in deleteEntities)
                {
                    ArgumentNullException.ThrowIfNull(deleteEntity);
                    if (!await DeleteEntityCoreAsync(deleteEntity, token).ConfigureAwait(false))
                        throw new InvalidOperationException($"Failed to delete {typeof(T).Name} in batch.");
                    deletedCount++;
                }
                return deletedCount;
            }, cancellationToken);
        }
        #endregion

        #region Private / Protected Methods

        private void ApplyOrEnqueueStoreAction(Action action)
        {
            var ctx = _tx.Value;
            if (ctx != null)
            {
                ctx.PendingStoreActions.Enqueue(action);
                return;
            }

            action();
        }

        private static void FlushPendingStoreActions(TxContext ctx)
        {
            if (ctx == null) return;

            while (ctx.PendingStoreActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch
                {
                }
            }
        }

        private void FlushPendingEntityEvents(TxContext ctx)
        {
            if (ctx == null) return;
            if (ctx.PendingEntityEvents.IsEmpty) return;

            var events = ctx.PendingEntityEvents.ToArray();

            while (ctx.PendingEntityEvents.TryDequeue(out _)) { }

            foreach (var e in events)
            {
                try
                {
                    EntityChanged?.Invoke(this, e);
                }
                catch
                {
                }
            }
        }

        private int InsertEntityCore<T>(T insertEntity) where T : IEntity
        {
            if (insertEntity.InsertTime <= DateTime.MinValue)
                insertEntity.InsertTime = DateTime.Now;

            List<KeyValuePair<string, object>> paramList = [];
            string sqlQuery = GetSqlQueryCommand<T>(DatabaseQueryType.Insert, insertEntity, ref paramList);

            if (string.IsNullOrEmpty(sqlQuery))
                return insertEntity.ID;

            object? returnObj = ExecuteScalar(sqlQuery, paramList);

            int newId = GetInsertedID(returnObj);

            if (newId <= 0)
                return insertEntity.ID;

            insertEntity.ID = newId;

            ApplyOrEnqueueStoreAction(() => _entityStore.InsertEntity<T>(insertEntity.Copy<T>()));

            CascadeEntityAction(insertEntity, nameof(BaseDataBaseService.CascadeInsertEntity));

            return insertEntity.ID;
        }

        private bool DeleteEntityCore<T>(T deleteEntity) where T : IEntity
        {
            CascadeEntityAction(deleteEntity, nameof(BaseDataBaseService.CascadeDeleteEntity));

            List<KeyValuePair<string, object>> paramList = [];
            string sqlQuery = GetSqlQueryCommand<T>(DatabaseQueryType.Delete, deleteEntity, ref paramList);

            if (!string.IsNullOrEmpty(sqlQuery) && ExecuteNonQuery(sqlQuery, paramList) > 0)
            {
                ApplyOrEnqueueStoreAction(() => _entityStore.DeleteEntity<T>(deleteEntity));
                return true;
            }

            return false;
        }

        private bool UpdateEntityCore<T>(T updateEntity) where T : IEntity
        {
            CascadeEntityAction(updateEntity, nameof(BaseDataBaseService.CascadeUpdateEntity));

            T? findEntity = _entityStore.FindEntity<T>(x => x.ID == updateEntity.ID);
            if (findEntity == null) return false;

            EntityChanges fieldChanges = findEntity.GetChangedFields<T>(updateEntity);
            if (fieldChanges.Count <= 0) return true;

            updateEntity.UpdateTime = DateTime.Now;

            List<KeyValuePair<string, object>> paramList = [];
            string sqlQuery = GetSqlQueryCommand<T>(DatabaseQueryType.Update, updateEntity, ref paramList);

            if (!string.IsNullOrEmpty(sqlQuery) && ExecuteNonQuery(sqlQuery, paramList) > 0)
            {
                ApplyOrEnqueueStoreAction(() => _entityStore.UpdateEntity<T>(updateEntity));
                return true;
            }

            return false;
        }

        private async Task<int> InsertEntityCoreAsync<T>(T insertEntity, CancellationToken cancellationToken) where T : IEntity
        {
            if (insertEntity.InsertTime <= DateTime.MinValue)
                insertEntity.InsertTime = DateTime.Now;

            List<KeyValuePair<string, object>> paramList = [];
            string sqlQuery = GetSqlQueryCommand<T>(DatabaseQueryType.Insert, insertEntity, ref paramList);
            if (string.IsNullOrEmpty(sqlQuery))
                return insertEntity.ID;

            int newID = GetInsertedID(await ExecuteScalarAsync(sqlQuery, paramList, cancellationToken).ConfigureAwait(false));
            if (newID <= 0)
                return insertEntity.ID;

            insertEntity.ID = newID;
            ApplyOrEnqueueStoreAction(() => _entityStore.InsertEntity<T>(insertEntity.Copy<T>()));
            await CascadeEntityActionAsync(insertEntity, nameof(CascadeInsertEntityAsync), cancellationToken).ConfigureAwait(false);
            return insertEntity.ID;
        }

        private async Task<bool> UpdateEntityCoreAsync<T>(T updateEntity, CancellationToken cancellationToken) where T : IEntity
        {
            await CascadeEntityActionAsync(updateEntity, nameof(CascadeUpdateEntityAsync), cancellationToken).ConfigureAwait(false);

            T? findEntity = _entityStore.FindEntity<T>(x => x.ID == updateEntity.ID);
            if (findEntity == null)
                return false;

            EntityChanges fieldChanges = findEntity.GetChangedFields<T>(updateEntity);
            if (fieldChanges.Count <= 0)
                return true;

            updateEntity.UpdateTime = DateTime.Now;
            List<KeyValuePair<string, object>> paramList = [];
            string sqlQuery = GetSqlQueryCommand<T>(DatabaseQueryType.Update, updateEntity, ref paramList);
            if (!string.IsNullOrEmpty(sqlQuery) && await ExecuteNonQueryAsync(sqlQuery, paramList, cancellationToken).ConfigureAwait(false) > 0)
            {
                ApplyOrEnqueueStoreAction(() => _entityStore.UpdateEntity<T>(updateEntity));
                return true;
            }

            return false;
        }

        private async Task<bool> DeleteEntityCoreAsync<T>(T deleteEntity, CancellationToken cancellationToken) where T : IEntity
        {
            await CascadeEntityActionAsync(deleteEntity, nameof(CascadeDeleteEntityAsync), cancellationToken).ConfigureAwait(false);

            List<KeyValuePair<string, object>> paramList = [];
            string sqlQuery = GetSqlQueryCommand<T>(DatabaseQueryType.Delete, deleteEntity, ref paramList);
            if (!string.IsNullOrEmpty(sqlQuery) && await ExecuteNonQueryAsync(sqlQuery, paramList, cancellationToken).ConfigureAwait(false) > 0)
            {
                ApplyOrEnqueueStoreAction(() => _entityStore.DeleteEntity<T>(deleteEntity));
                return true;
            }

            return false;
        }

        private static int GetInsertedID(object? value)
        {
            if (value is int intValue)
                return intValue;
            if (value is long longValue)
                return checked((int)longValue);
            if (value is decimal decimalValue)
                return checked((int)decimalValue);
            return value != null && int.TryParse(value.ToString(), out int parsed) ? parsed : 0;
        }

        protected abstract DbConnection CreateConnection(string connectionString);

        protected static async Task<DataTable> ReadDataTableAsync(DbDataReader reader, CancellationToken cancellationToken)
        {
            DataTable table = new();
            for (int index = 0; index < reader.FieldCount; index++)
                table.Columns.Add(reader.GetName(index), reader.GetFieldType(index));

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                object[] values = new object[reader.FieldCount];
                reader.GetValues(values);
                table.Rows.Add(values);
            }

            return table;
        }

        private void CascadeEntityAction<T>(T entity, string methodName) where T : IEntity
        {
            var propertySchemas = entity.GetRelatedListProps();
            foreach (var propertySchema in propertySchemas)
            {
                object? value = propertySchema.Property.GetValue(entity);
                if (value == null) continue;

                Type type = propertySchema.ForeignKeyAttribs.RelatedType;
                if (type == null) continue;

                string cacheKey = $"{methodName}_{type.FullName}";
                if (!_cachedGenericMethods.TryGetValue(cacheKey, out MethodInfo? methodInfo))
                {
                    MethodInfo? baseMethod = GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
                    if (baseMethod == null) continue;

                    methodInfo = baseMethod.MakeGenericMethod(type);
                    _cachedGenericMethods.TryAdd(cacheKey, methodInfo);
                }

                if (methodName == nameof(BaseDataBaseService.CascadeUpdateEntity))
                {
                    methodInfo.Invoke(this, [value, propertySchema.ForeignKeyAttribs.ForeignKeyField, entity.ID]);
                }
                else if (methodName == nameof(BaseDataBaseService.CascadeInsertEntity))
                {
                    methodInfo.Invoke(this, [value, propertySchema.ForeignKeyAttribs.ForeignKeyField, entity.ID]);
                }
                else
                {
                    methodInfo.Invoke(this, [value]);
                }
            }
        }

        private async Task CascadeEntityActionAsync<T>(T entity, string methodName, CancellationToken cancellationToken) where T : IEntity
        {
            foreach (var propertySchema in entity.GetRelatedListProps())
            {
                object? value = propertySchema.Property.GetValue(entity);
                if (value == null)
                    continue;

                Type type = propertySchema.ForeignKeyAttribs.RelatedType;
                string cacheKey = $"{methodName}_{type.FullName}";
                if (!_cachedGenericMethods.TryGetValue(cacheKey, out MethodInfo? methodInfo))
                {
                    MethodInfo? baseMethod = GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
                    if (baseMethod == null)
                        continue;

                    methodInfo = baseMethod.MakeGenericMethod(type);
                    _cachedGenericMethods.TryAdd(cacheKey, methodInfo);
                }

                object? result = methodName == nameof(CascadeDeleteEntityAsync) ? methodInfo.Invoke(this, [value, cancellationToken]) : methodInfo.Invoke(this, [value, propertySchema.ForeignKeyAttribs.ForeignKeyField, entity.ID, cancellationToken]);
                if (result is Task task)
                    await task.ConfigureAwait(false);
            }
        }

        protected void CascadeUpdateEntity<T>(ObservableCollection<T> updateEntities, string fkFieldName, int fkID) where T : Entity
        {
            foreach (var item in updateEntities)
            {
                if (item.ID <= 0)
                {
                    PropertyInfo? property = item.GetType().GetProperty(fkFieldName);
                    if (property != null && property.CanWrite) // 속성이 쓰기 가능한지 확인
                    {
                        // 새로운 값을 설정
                        property.SetValue(item, fkID);
                    }

                    InsertEntity<T>(item);
                }
                else
                {
                    UpdateEntity<T>(item);
                }
            }
        }

        protected void CascadeDeleteEntity<T>(ObservableCollection<T> deleteEntities) where T : Entity
        {
            foreach (var item in deleteEntities)
            {
                DeleteEntity<T>(item);
            }
        }

        protected void CascadeInsertEntity<T>(ObservableCollection<T> insertEntities, string fkFieldName, int fkID) where T : Entity
        {
            foreach (var item in insertEntities)
            {
                PropertyInfo? property = item.GetType().GetProperty(fkFieldName);
                if (property != null && property.CanWrite) // 속성이 쓰기 가능한지 확인
                {
                    // 새로운 값을 설정
                    property.SetValue(item, fkID);
                }

                InsertEntity<T>(item);
            }
        }

        protected async Task CascadeUpdateEntityAsync<T>(ObservableCollection<T> updateEntities, string fkFieldName, int fkID, CancellationToken cancellationToken) where T : Entity
        {
            foreach (T item in updateEntities)
            {
                if (item.ID <= 0)
                {
                    PropertyInfo? property = item.GetType().GetProperty(fkFieldName);
                    if (property != null && property.CanWrite)
                        property.SetValue(item, fkID);
                    await InsertEntityAsync(item, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await UpdateEntityAsync(item, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        protected async Task CascadeDeleteEntityAsync<T>(ObservableCollection<T> deleteEntities, CancellationToken cancellationToken) where T : Entity
        {
            foreach (T item in deleteEntities)
                await DeleteEntityAsync(item, cancellationToken).ConfigureAwait(false);
        }

        protected async Task CascadeInsertEntityAsync<T>(ObservableCollection<T> insertEntities, string fkFieldName, int fkID, CancellationToken cancellationToken) where T : Entity
        {
            foreach (T item in insertEntities)
            {
                PropertyInfo? property = item.GetType().GetProperty(fkFieldName);
                if (property != null && property.CanWrite)
                    property.SetValue(item, fkID);
                await InsertEntityAsync(item, cancellationToken).ConfigureAwait(false);
            }
        }


        private void EntityStore_EntityChanged(object sender, EntityChangedEventArgs e)
        {
            var ctx = _tx.Value;
            if (ctx != null)
            {
                ctx.PendingEntityEvents.Enqueue(e);
                return;
            }


            EntityChanged?.Invoke(this, e);
        }

        protected virtual string GetSqlQueryCommand<T>(DatabaseQueryType queryType, T entity, ref List<KeyValuePair<string, object>> paramList)
            where T : IEntity
        {
            return string.Empty;
        }

        protected override void OnDispose()
        {
            if (_entityStore != null)
                this._entityStore.EntityChanged -= EntityStore_EntityChanged;
        }

        protected static bool IsNullableType(Type type)
        {
            return Nullable.GetUnderlyingType(type) != null;
            //return type.IsGenericType && type.GetGenericTypeDefinition().Equals(typeof(Nullable<>));
        }

        protected static T SetPropertyValue<T>(DataRow row) where T : class
        {
            T result = Activator.CreateInstance<T>();
            try
            {
                Type type = typeof(T);
                PropertyInfo[] propertyInfos = type.GetProperties();

                foreach (DataColumn col in row.Table.Columns)
                {
                    string columnName = col.ColumnName;
                    PropertyInfo? property = propertyInfos.FirstOrDefault(x => x.Name == columnName);
                    if ((property != null) && (property.CanWrite))
                    {
                        object value = row[columnName];
                        if (value is DBNull)
                        {
                            property.SetValue(result, null, null);
                        }
                        else
                        {
                            var targetType = IsNullableType(property.PropertyType) ? Nullable.GetUnderlyingType(property.PropertyType) : property.PropertyType;

                            object? propertyVal = null;
                            try
                            {
                                propertyVal = Convert.ChangeType(value, targetType!);
                            }
                            catch
                            {
                            }

                            property.SetValue(result, propertyVal, null);

                            //System.TypeCode typeCode = Type.GetTypeCode(property.PropertyType);

                            //switch (typeCode)
                            //{
                            //    case TypeCode.Boolean:
                            //        {
                            //            property.SetValue(result, Convert.ToBoolean(value), null);
                            //        }
                            //        break;
                            //    case TypeCode.DateTime:
                            //        {
                            //            property.SetValue(result, Convert.ToDateTime(value), null);
                            //        }
                            //        break;
                            //    case TypeCode.Object:
                            //        {
                            //            var targetType = IsNullableType(property.PropertyType) ? Nullable.GetUnderlyingType(property.PropertyType) : property.PropertyType;

                            //            object propertyVal = null;
                            //            try
                            //            {
                            //                propertyVal = Convert.ChangeType(value, targetType);
                            //            }
                            //            catch (Exception ex)
                            //            {
                            //                LogWriter.WriteLogEntry(ex);
                            //            }

                            //            property.SetValue(result, propertyVal, null);
                            //        }
                            //        break;
                            //    case TypeCode.Double:
                            //        {
                            //            property.SetValue(result, Convert.ToDouble(value), null);
                            //        }
                            //        break;                   
                            //    default:
                            //        {
                            //            property.SetValue(result, value is System.DBNull ? null : value, null);
                            //        }
                            //        break;
                            //}
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        protected static T SetPropertyValue<T>(DbDataReader reader) where T : class
        {
            T result = Activator.CreateInstance<T>();
            try
            {
                Type type = typeof(T);
                PropertyInfo[] propertyInfos = type.GetProperties();
                for (int i = 0; i <= reader.FieldCount - 1; i++)
                {
                    string columnName = reader.GetName(i);
                    PropertyInfo? property = propertyInfos.FirstOrDefault(x => x.Name == columnName);
                    if ((property != null) && (property.CanWrite))
                    {
                        object value = reader.GetValue(i);
                        if (value is DBNull)
                        {
                            property.SetValue(result, null, null);
                        }
                        else
                        {
                            var targetType = IsNullableType(property.PropertyType) ? Nullable.GetUnderlyingType(property.PropertyType) : property.PropertyType;

                            object? propertyVal = null;
                            try
                            {
                                propertyVal = Convert.ChangeType(value, targetType!);
                            }
                            catch
                            {
                            }

                            property.SetValue(result, propertyVal, null);

                            //System.TypeCode typeCode = Type.GetTypeCode(property.PropertyType);

                            //switch (typeCode)
                            //{
                            //    case TypeCode.Boolean:
                            //        {
                            //            property.SetValue(result, Convert.ToBoolean(value), null);
                            //        }
                            //        break;
                            //    case TypeCode.DateTime:
                            //        {
                            //            property.SetValue(result, Convert.ToDateTime(value), null);
                            //        }
                            //        break;
                            //    case TypeCode.Object:
                            //        {
                            //            var targetType = IsNullableType(property.PropertyType) ? Nullable.GetUnderlyingType(property.PropertyType) : property.PropertyType;

                            //            object propertyVal = null;
                            //            try
                            //            {
                            //                propertyVal = Convert.ChangeType(value, targetType);
                            //            }
                            //            catch (Exception ex)
                            //            {
                            //                LogWriter.WriteLogEntry(ex);
                            //            }

                            //            property.SetValue(result, propertyVal, null);
                            //        }
                            //        break;
                            //    case TypeCode.Double:
                            //        {
                            //            property.SetValue(result, Convert.ToDouble(value), null);
                            //        }
                            //        break;                   
                            //    default:
                            //        {
                            //            property.SetValue(result, value is System.DBNull ? null : value, null);
                            //        }
                            //        break;
                            //}
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }
        #endregion
    }
}




