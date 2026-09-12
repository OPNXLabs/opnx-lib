using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Network;
using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.Enums;
using OPNX.Lib.Data.ORM.EventHandlers;
using OPNX.Lib.Data.ORM.Generators;
using OPNX.Lib.Data.ORM.Interfaces;
using OPNX.Lib.Data.ORM.Mapping;
using OPNX.Lib.Data.ORM.Query;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Services
{
    public abstract class BaseDataBaseService : DisposableObject, IDataBaseService
    {
        #region Fields
        private int _commandTimeout = 10;
        private string _connectionString = string.Empty;

        private static readonly ConcurrentDictionary<string, MethodInfo> _cachedGenericMethods = new();
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _cachedDirtyCheckProperties = new();

        private readonly IEntityStore? _entityStore;
        private readonly ILogger _logger;
        private readonly DataRowMapper _dataRowMapper = new();

        private sealed class TxContext
        {
            public DbConnection? Connection { get; init; }
            public DbTransaction? Transaction { get; init; }
            public int Depth { get; set; }

            public ConcurrentQueue<Action> PendingStoreActions { get; } = [];
            public ConcurrentQueue<EntityChangedEventArgs> PendingEntityEvents { get; } = [];
            public ConcurrentStack<Action> PendingRollbackActions { get; } = [];
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
            : this(connectionString, entityStore, true, logger)
        {
        }

        protected BaseDataBaseService(string connectionString, bool useEntityStore, ILogger? logger = null)
            : this(connectionString, useEntityStore ? new EntityStore() : null, useEntityStore, logger)
        {
        }

        private BaseDataBaseService(string connectionString, IEntityStore? entityStore, bool useEntityStore, ILogger? logger)
            : base()
        {
            _logger = logger ?? NullLogger.Instance;

            if (!string.IsNullOrEmpty(connectionString))
                ConnectionString = connectionString;

            _entityStore = useEntityStore ? entityStore ?? throw new ArgumentNullException(nameof(entityStore)) : null;
            _entityStore?.EntityChanged += EntityStore_EntityChanged;
        }
        #endregion

        #region Properties
        public abstract DatabaseType DBType { get; }
        public abstract IEntitySqlGenerator SqlGenerator { get; }
        public bool AutoTransactionForEntityOperations { get; set; } = true;

        public bool UsesEntityStore => _entityStore != null;
        public IEntityStore EntityStore => _entityStore ?? throw new InvalidOperationException("This database service was created without an EntityStore.");

        public event EventHandler<EntityStoreSynchronizationFailedEventArgs>? EntityStoreSynchronizationFailed;

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

        private IEntityStore RequiredEntityStore => _entityStore
            ?? throw new InvalidOperationException("EntityStore synchronization requires a configured EntityStore.");
        #endregion

        #region Events
        public event EntityChangedEventHandler? EntityChanged;
        #endregion

        #region Public Methods
        public virtual void LoadDataBase() { }
        public virtual void LoadEntity(Type entityType) { }
        public virtual string GetTableIdentifier(Type entityType) => DatabaseNaming.GetTableName(entityType);

        public IReadOnlyList<T> Select<T>(SelectQuery<T> query) where T : IDatabaseEntity
        {
            ArgumentNullException.ThrowIfNull(query);
            CompiledDbCommand command = SqlGenerator.Select(query);
            return Query<T>(command.CommandText, command.Parameters);
        }

        public Task<IReadOnlyList<T>> SelectAsync<T>(SelectQuery<T> query, CancellationToken cancellationToken = default) where T : IDatabaseEntity
        {
            ArgumentNullException.ThrowIfNull(query);
            CompiledDbCommand command = SqlGenerator.Select(query);
            return QueryAsync<T>(command.CommandText, command.Parameters, cancellationToken);
        }

        public long Count<T>(SelectQuery<T> query) where T : IDatabaseEntity
        {
            ArgumentNullException.ThrowIfNull(query);
            CompiledDbCommand command = SqlGenerator.Count(query);
            return Convert.ToInt64(ExecuteScalar(command.CommandText, command.Parameters) ?? 0);
        }

        public async Task<long> CountAsync<T>(SelectQuery<T> query, CancellationToken cancellationToken = default) where T : IDatabaseEntity
        {
            ArgumentNullException.ThrowIfNull(query);
            CompiledDbCommand command = SqlGenerator.Count(query);
            object? result = await ExecuteScalarAsync(command.CommandText, command.Parameters, cancellationToken).ConfigureAwait(false);
            return Convert.ToInt64(result ?? 0);
        }

        public T First<T>(SelectQuery<T> query) where T : IDatabaseEntity => FirstOrDefault(query) ?? throw new InvalidOperationException($"The query returned no {typeof(T).Name} entity.");
        public async Task<T> FirstAsync<T>(SelectQuery<T> query, CancellationToken cancellationToken = default) where T : IDatabaseEntity => await FirstOrDefaultAsync(query, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException($"The query returned no {typeof(T).Name} entity.");
        public T? FirstOrDefault<T>(SelectQuery<T> query) where T : IDatabaseEntity
        {
            ArgumentNullException.ThrowIfNull(query);
            IReadOnlyList<T> result = Select(query.CopyWithLimit(1));
            return result.Count > 0 ? result[0] : default;
        }

        public async Task<T?> FirstOrDefaultAsync<T>(SelectQuery<T> query, CancellationToken cancellationToken = default) where T : IDatabaseEntity
        {
            ArgumentNullException.ThrowIfNull(query);
            IReadOnlyList<T> result = await SelectAsync(query.CopyWithLimit(1), cancellationToken).ConfigureAwait(false);
            return result.Count > 0 ? result[0] : default;
        }
        public bool Any<T>(SelectQuery<T> query) where T : IDatabaseEntity
        {
            ArgumentNullException.ThrowIfNull(query);
            CompiledDbCommand command = SqlGenerator.Exists(query);
            return Convert.ToBoolean(ExecuteScalar(command.CommandText, command.Parameters) ?? false);
        }

        public async Task<bool> AnyAsync<T>(SelectQuery<T> query, CancellationToken cancellationToken = default) where T : IDatabaseEntity
        {
            ArgumentNullException.ThrowIfNull(query);
            CompiledDbCommand command = SqlGenerator.Exists(query);
            object? result = await ExecuteScalarAsync(command.CommandText, command.Parameters, cancellationToken).ConfigureAwait(false);
            return Convert.ToBoolean(result ?? false);
        }

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
            var currentContext = _tx.Value;
            if (currentContext != null)
            {
                currentContext.Depth++;
                try
                {
                    return work();
                }
                finally
                {
                    currentContext.Depth--;
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
            TxContext? ctx = null;
            TResult result;

            try
            {
                // Only database work and commit failures may roll back.
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

                    result = work();

                    tx.Commit();

                }
                catch (Exception ex)
                {
                    try
                    {
                        tx?.Rollback();
                    }
                    catch { }
                    if (ctx != null)
                        RunPendingRollbackActions(ctx);
                    _logger.LogError(ex, "{Message}", ex.Message);
                    if (throwOnError)
                        throw;
                    return default!;
                }

                // Synchronize memory while collecting its change notifications.
                FlushPendingStoreActions(ctx!);
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

            // Publish after cleanup so subscribers cannot inherit this transaction.
            FlushPendingEntityEvents(ctx!);
            return result;
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
            var currentContext = _tx.Value;
            if (currentContext != null)
            {
                currentContext.Depth++;
                try
                {
                    return await work(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    currentContext.Depth--;
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
            TResult result;
            try
            {
                // Only database work and commit failures may roll back.
                try
                {
                    transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    context = new TxContext { Connection = connection, Transaction = transaction, Depth = 1 };
                    _tx.Value = context;

                    result = await work(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (transaction != null)
                    {
                        try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    }
                    if (context != null)
                        RunPendingRollbackActions(context);
                    throw;
                }
                catch (Exception ex)
                {
                    if (transaction != null)
                    {
                        try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    }
                    if (context != null)
                        RunPendingRollbackActions(context);
                    _logger.LogError(ex, "{Message}", ex.Message);
                    if (throwOnError)
                        throw;
                    return default!;
                }

                // Synchronize memory while collecting its change notifications.
                FlushPendingStoreActions(context!);
            }
            finally
            {
                _tx.Value = null;
                try
                {
                    if (transaction != null)
                        await transaction.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispose the database transaction.");
                }
                finally
                {
                    await CloseDataBaseAsync(connection).ConfigureAwait(false);
                }
            }

            // Publish after cleanup so subscribers cannot inherit this transaction.
            FlushPendingEntityEvents(context!);
            return result;
        }

        public virtual int ExecuteNonQuery(string sqlQuery, List<KeyValuePair<string, object>> paramList) => int.MinValue;

        public virtual Task<int> ExecuteNonQueryAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default) => Task.FromResult(int.MinValue);

        public virtual DataTable? ExecuteReader(string sqlQuery, List<KeyValuePair<string, object>> paramList) => null;

        public virtual Task<DataTable?> ExecuteReaderAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default) => Task.FromResult<DataTable?>(null);

        public virtual IReadOnlyList<T> Query<T>(string sqlQuery, List<KeyValuePair<string, object>> paramList) => _dataRowMapper.Map<T>(ExecuteReader(sqlQuery, paramList), typeof(IEntity).IsAssignableFrom(typeof(T)) ? _entityStore : null);

        public virtual async Task<IReadOnlyList<T>> QueryAsync<T>(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default) => _dataRowMapper.Map<T>(await ExecuteReaderAsync(sqlQuery, paramList, cancellationToken).ConfigureAwait(false), typeof(IEntity).IsAssignableFrom(typeof(T)) ? _entityStore : null);

        public virtual object? ExecuteScalar(string sqlQuery, List<KeyValuePair<string, object>> paramList) => null;

        public virtual Task<object?> ExecuteScalarAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);

        public virtual TKey InsertEntity<T, TKey>(T insertEntity) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(insertEntity);
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransaction(() => InsertKeyedEntityCore<T, TKey>(insertEntity));
            return InsertKeyedEntityCore<T, TKey>(insertEntity);
        }

        public virtual Task<TKey> InsertEntityAsync<T, TKey>(T insertEntity, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(insertEntity);
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransactionAsync(token => InsertKeyedEntityCoreAsync<T, TKey>(insertEntity, token), cancellationToken);
            return InsertKeyedEntityCoreAsync<T, TKey>(insertEntity, cancellationToken);
        }

        public virtual bool UpdateEntity<T, TKey>(T updateEntity) where T : IEntity<TKey> where TKey : notnull => AutoTransactionForEntityOperations && CurrentTransaction == null ? ExecuteInTransaction(() => UpdateKeyedEntityCore<T, TKey>(updateEntity, null)) : UpdateKeyedEntityCore<T, TKey>(updateEntity, null);

        public virtual bool UpdateEntity<T, TKey>(T updateEntity, params Expression<Func<T, object?>>[] properties) where T : IEntity<TKey> where TKey : notnull
        {
            PropertyInfo[] selectedProperties = GetSelectedDatabaseProperties(properties);
            return AutoTransactionForEntityOperations && CurrentTransaction == null ? ExecuteInTransaction(() => UpdateKeyedEntityCore<T, TKey>(updateEntity, selectedProperties)) : UpdateKeyedEntityCore<T, TKey>(updateEntity, selectedProperties);
        }

        public virtual Task<bool> UpdateEntityAsync<T, TKey>(T updateEntity, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull => AutoTransactionForEntityOperations && CurrentTransaction == null ? ExecuteInTransactionAsync(token => UpdateKeyedEntityCoreAsync<T, TKey>(updateEntity, null, token), cancellationToken) : UpdateKeyedEntityCoreAsync<T, TKey>(updateEntity, null, cancellationToken);

        public virtual Task<bool> UpdateEntityAsync<T, TKey>(T updateEntity, CancellationToken cancellationToken = default, params Expression<Func<T, object?>>[] properties) where T : IEntity<TKey> where TKey : notnull
        {
            PropertyInfo[] selectedProperties = GetSelectedDatabaseProperties(properties);
            return AutoTransactionForEntityOperations && CurrentTransaction == null ? ExecuteInTransactionAsync(token => UpdateKeyedEntityCoreAsync<T, TKey>(updateEntity, selectedProperties, token), cancellationToken) : UpdateKeyedEntityCoreAsync<T, TKey>(updateEntity, selectedProperties, cancellationToken);
        }

        public virtual bool DeleteEntity<T, TKey>(T deleteEntity) where T : IEntity<TKey> where TKey : notnull => AutoTransactionForEntityOperations && CurrentTransaction == null ? ExecuteInTransaction(() => DeleteKeyedEntityCore<T, TKey>(deleteEntity)) : DeleteKeyedEntityCore<T, TKey>(deleteEntity);

        public virtual Task<bool> DeleteEntityAsync<T, TKey>(T deleteEntity, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull => AutoTransactionForEntityOperations && CurrentTransaction == null ? ExecuteInTransactionAsync(token => DeleteKeyedEntityCoreAsync<T, TKey>(deleteEntity, token), cancellationToken) : DeleteKeyedEntityCoreAsync<T, TKey>(deleteEntity, cancellationToken);

        public virtual int BatchDelete<T, TKey>(IReadOnlyList<T> entities) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(entities);
            return ExecuteInTransaction(() => { foreach (T entity in entities) if (!DeleteKeyedEntityCore<T, TKey>(entity)) throw new InvalidOperationException($"Failed to delete {typeof(T).Name} in batch."); return entities.Count; });
        }

        public virtual Task<int> BatchDeleteAsync<T, TKey>(IReadOnlyList<T> entities, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(entities);
            return ExecuteInTransactionAsync(async token => { foreach (T entity in entities) if (!await DeleteKeyedEntityCoreAsync<T, TKey>(entity, token).ConfigureAwait(false)) throw new InvalidOperationException($"Failed to delete {typeof(T).Name} in batch."); return entities.Count; }, cancellationToken);
        }

        public bool CascadeInsert<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children, Expression<Func<TChild, object?>> foreignKey) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull
        {
            ArgumentNullException.ThrowIfNull(parent);
            ArgumentNullException.ThrowIfNull(children);
            PropertyInfo foreignKeyProperty = GetExpressionProperty(foreignKey);
            return ExecuteInTransaction(() =>
            {
                TParentKey parentKey = InsertKeyedEntityCore<TParent, TParentKey>(parent);
                foreach (TChild child in children)
                {
                    EnqueuePropertyRollback(child!, foreignKeyProperty);
                    EntityKeyConverter.SetForeignKey(child!, foreignKeyProperty, parentKey);
                    InsertKeyedEntityCore<TChild, TChildKey>(child);
                }
                return true;
            });
        }

        public bool CascadeUpdate<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children, Expression<Func<TChild, object?>> foreignKey) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull
        {
            ArgumentNullException.ThrowIfNull(parent);
            ArgumentNullException.ThrowIfNull(children);
            PropertyInfo foreignKeyProperty = GetExpressionProperty(foreignKey);
            return ExecuteInTransaction(() =>
            {
                if (!UpdateKeyedEntityCore<TParent, TParentKey>(parent, null))
                    throw new InvalidOperationException($"Failed to update cascade parent {typeof(TParent).Name}.");
                foreach (TChild child in children)
                {
                    EnqueuePropertyRollback(child!, foreignKeyProperty);
                    EntityKeyConverter.SetForeignKey(child!, foreignKeyProperty, parent.ID);
                    if (!UpdateKeyedEntityCore<TChild, TChildKey>(child, null))
                        throw new InvalidOperationException($"Failed to update cascade child {typeof(TChild).Name}.");
                }
                return true;
            });
        }

        public bool CascadeDelete<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull
        {
            ArgumentNullException.ThrowIfNull(parent);
            ArgumentNullException.ThrowIfNull(children);
            return ExecuteInTransaction(() =>
            {
                foreach (TChild child in children)
                {
                    if (!DeleteKeyedEntityCore<TChild, TChildKey>(child))
                        throw new InvalidOperationException($"Failed to delete cascade child {typeof(TChild).Name}.");
                }
                if (!DeleteKeyedEntityCore<TParent, TParentKey>(parent))
                    throw new InvalidOperationException($"Failed to delete cascade parent {typeof(TParent).Name}.");
                return true;
            });
        }

        public Task<bool> CascadeInsertAsync<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children, Expression<Func<TChild, object?>> foreignKey, CancellationToken cancellationToken = default) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull
        {
            ArgumentNullException.ThrowIfNull(parent);
            ArgumentNullException.ThrowIfNull(children);
            PropertyInfo foreignKeyProperty = GetExpressionProperty(foreignKey);
            return ExecuteInTransactionAsync(async token =>
            {
                TParentKey parentKey = await InsertKeyedEntityCoreAsync<TParent, TParentKey>(parent, token).ConfigureAwait(false);
                foreach (TChild child in children)
                {
                    EnqueuePropertyRollback(child!, foreignKeyProperty);
                    EntityKeyConverter.SetForeignKey(child!, foreignKeyProperty, parentKey);
                    await InsertKeyedEntityCoreAsync<TChild, TChildKey>(child, token).ConfigureAwait(false);
                }
                return true;
            }, cancellationToken);
        }

        public virtual int BatchUpdate<T, TKey>(IReadOnlyList<T> entities) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(entities);
            return ExecuteInTransaction(() => { foreach (T entity in entities) if (!UpdateKeyedEntityCore<T, TKey>(entity, null)) throw new InvalidOperationException($"Failed to update {typeof(T).Name} in batch."); return entities.Count; });
        }

        public virtual Task<int> BatchUpdateAsync<T, TKey>(IReadOnlyList<T> entities, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(entities);
            return ExecuteInTransactionAsync(async token => { foreach (T entity in entities) if (!await UpdateKeyedEntityCoreAsync<T, TKey>(entity, null, token).ConfigureAwait(false)) throw new InvalidOperationException($"Failed to update {typeof(T).Name} in batch."); return entities.Count; }, cancellationToken);
        }

        public virtual int BatchInsert<T, TKey>(IReadOnlyList<T> entities) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(entities);
            return ExecuteInTransaction(() => { foreach (T entity in entities) InsertKeyedEntityCore<T, TKey>(entity); return entities.Count; });
        }

        public virtual Task<int> BatchInsertAsync<T, TKey>(IReadOnlyList<T> entities, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(entities);
            return ExecuteInTransactionAsync(async token => { foreach (T entity in entities) await InsertKeyedEntityCoreAsync<T, TKey>(entity, token).ConfigureAwait(false); return entities.Count; }, cancellationToken);
        }

        public virtual Task<int> BulkInsertAsync<T, TKey>(IReadOnlyList<T> entities, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(entities);
            if (entities.Count == 0) return Task.FromResult(0);
            foreach (T entity in entities) ArgumentNullException.ThrowIfNull(entity);
            _ = SqlGenerator.Insert<T, TKey>(entities[0]);
            if (typeof(T).GetCustomAttribute<EntityTableAttribute>()?.SupportsBulkInsert != true) throw new InvalidOperationException($"{typeof(T).Name} does not support bulk insert.");
            if (IsIdentityKey<T>()) throw new InvalidOperationException($"Bulk insert does not return generated keys; identity entity {typeof(T).Name} must use BatchInsert.");
            if (entities.Any(entity => EqualityComparer<TKey>.Default.Equals(entity.ID, default!))) throw new InvalidOperationException($"Bulk insert requires every {typeof(T).Name} entity to have a non-default key.");
            return ExecuteInTransactionAsync(async token =>
            {
                PropertyInfo[] properties = GetBulkInsertProperties<T>();
                if (properties.Length == 0) throw new InvalidOperationException($"{typeof(T).Name} has no columns available for bulk insert.");
                int rowsPerCommand = Math.Max(1, Math.Min(500, 10000 / properties.Length));
                int inserted = 0;
                for (int offset = 0; offset < entities.Count; offset += rowsPerCommand)
                {
                    int count = Math.Min(rowsPerCommand, entities.Count - offset);
                    inserted += await ExecuteBulkInsertChunkAsync(entities, offset, count, properties, token).ConfigureAwait(false);
                }
                if (inserted != entities.Count) throw new InvalidOperationException($"Bulk insert for {typeof(T).Name} inserted {inserted} of {entities.Count} rows.");
                return inserted;
            }, cancellationToken);
        }

        public Task<bool> CascadeUpdateAsync<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children, Expression<Func<TChild, object?>> foreignKey, CancellationToken cancellationToken = default) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull
        {
            ArgumentNullException.ThrowIfNull(parent);
            ArgumentNullException.ThrowIfNull(children);
            PropertyInfo foreignKeyProperty = GetExpressionProperty(foreignKey);
            return ExecuteInTransactionAsync(async token =>
            {
                if (!await UpdateKeyedEntityCoreAsync<TParent, TParentKey>(parent, null, token).ConfigureAwait(false))
                    throw new InvalidOperationException($"Failed to update cascade parent {typeof(TParent).Name}.");
                foreach (TChild child in children)
                {
                    EnqueuePropertyRollback(child!, foreignKeyProperty);
                    EntityKeyConverter.SetForeignKey(child!, foreignKeyProperty, parent.ID);
                    if (!await UpdateKeyedEntityCoreAsync<TChild, TChildKey>(child, null, token).ConfigureAwait(false))
                        throw new InvalidOperationException($"Failed to update cascade child {typeof(TChild).Name}.");
                }
                return true;
            }, cancellationToken);
        }

        public Task<bool> CascadeDeleteAsync<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children, CancellationToken cancellationToken = default) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull
        {
            ArgumentNullException.ThrowIfNull(parent);
            ArgumentNullException.ThrowIfNull(children);
            return ExecuteInTransactionAsync(async token =>
            {
                foreach (TChild child in children)
                {
                    if (!await DeleteKeyedEntityCoreAsync<TChild, TChildKey>(child, token).ConfigureAwait(false))
                        throw new InvalidOperationException($"Failed to delete cascade child {typeof(TChild).Name}.");
                }
                if (!await DeleteKeyedEntityCoreAsync<TParent, TParentKey>(parent, token).ConfigureAwait(false))
                    throw new InvalidOperationException($"Failed to delete cascade parent {typeof(TParent).Name}.");
                return true;
            }, cancellationToken);
        }

        #endregion

        #region Private / Protected Methods

        private void ApplyOrEnqueueStoreAction(Action action)
        {
            if (_entityStore == null)
                return;

            var ctx = _tx.Value;
            if (ctx != null)
            {
                ctx.PendingStoreActions.Enqueue(action);
                return;
            }

            action();
        }

        private void ApplyOrEnqueueStoreMutation(Func<bool> mutation, string operation, Type entityType)
        {
            ApplyOrEnqueueStoreAction(() =>
            {
                try
                {
                    if (!mutation())
                        throw new InvalidOperationException($"EntityStore {operation} failed for {entityType.Name} after the database operation succeeded.");
                }
                catch (Exception ex)
                {
                    OnEntityStoreSynchronizationFailed(operation, entityType, ex);
                    throw;
                }
            });
        }

        private void OnEntityStoreSynchronizationFailed(string operation, Type entityType, Exception exception)
        {
            EventHandler<EntityStoreSynchronizationFailedEventArgs>? handlers = EntityStoreSynchronizationFailed;
            if (handlers == null)
                return;

            var eventArgs = new EntityStoreSynchronizationFailedEventArgs(operation, entityType, exception);
            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                try
                {
                    var handler = (EventHandler<EntityStoreSynchronizationFailedEventArgs>)subscriber;
                    handler(this, eventArgs);
                }
                catch (Exception subscriberException)
                {
                    _logger.LogError(subscriberException,
                        "EntityStore synchronization failure subscriber threw an exception. Operation={Operation}, EntityType={EntityType}.",
                        operation, entityType.Name);
                }
            }
        }

        private void FlushPendingStoreActions(TxContext ctx)
        {
            if (ctx == null) return;

            while (ctx.PendingStoreActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "EntityStore synchronization failed after the database transaction was committed.");
                }
            }
        }

        private void EnqueueRollbackAction(Action action) => _tx.Value?.PendingRollbackActions.Push(action);

        private void EnqueuePropertyRollback(object entity, PropertyInfo property)
        {
            object? originalValue = property.GetValue(entity);
            EnqueueRollbackAction(() => property.SetValue(entity, originalValue));
        }

        private void RunPendingRollbackActions(TxContext ctx)
        {
            while (ctx.PendingRollbackActions.TryPop(out Action? action))
            {
                try { action(); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to restore in-memory entity state after transaction rollback."); }
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

        private void PublishOrEnqueueEntityEvent(EntityChangedEventArgs eventArgs)
        {
            TxContext? context = _tx.Value;
            if (context != null)
            {
                context.PendingEntityEvents.Enqueue(eventArgs);
                return;
            }
            EntityChanged?.Invoke(this, eventArgs);
        }

        private TKey InsertKeyedEntityCore<T, TKey>(T insertEntity) where T : IEntity<TKey> where TKey : notnull
        {
            bool synchronizeStore = ShouldSynchronizeEntityStore(insertEntity);
            TKey originalKey = insertEntity.ID;
            EnqueueRollbackAction(() => insertEntity.ID = originalKey);
            PrepareClientGeneratedKey<T, TKey>(insertEntity);
            if (insertEntity is IAuditableEntity { IsAuditable: true } auditable && auditable.InsertTime <= DateTime.MinValue)
            {
                DateTime originalInsertTime = auditable.InsertTime;
                EnqueueRollbackAction(() => auditable.InsertTime = originalInsertTime);
                auditable.InsertTime = DateTime.Now;
            }
            if (synchronizeStore)
            {
                if (!IsIdentityKey<T>() && RequiredEntityStore.FindEntity<T, TKey>(insertEntity.ID) != null)
                    throw new InvalidOperationException($"{typeof(T).Name} with key {insertEntity.ID} is already loaded in EntityStore.");
            }
            CompiledDbCommand command = SqlGenerator.Insert<T, TKey>(insertEntity);
            object? result = ExecuteScalar(command.CommandText, command.Parameters);
            if (IsIdentityKey<T>() && (result == null || result == DBNull.Value))
                throw new InvalidOperationException($"The database did not return the generated key for {typeof(T).Name}.");
            TKey newKey = EntityKeyConverter.ConvertTo<TKey>(result ?? insertEntity.ID);
            insertEntity.ID = newKey;
            if (synchronizeStore)
            {
                ApplyOrEnqueueStoreMutation(() => RequiredEntityStore.InsertEntity<T, TKey>(insertEntity), "insert", typeof(T));
            }
            else if (_entityStore != null)
                ApplyOrEnqueueStoreMutation(() => _entityStore.InsertEntity<T, TKey>(insertEntity), "insert", typeof(T));
            if (insertEntity is IEntity legacyEntity)
            {
                CascadeEntityAction(legacyEntity, nameof(CascadeInsertEntity), CascadeType.Insert);
            }
            if (_entityStore == null)
                PublishOrEnqueueEntityEvent(EntityChangeTracker.CreateInsert(insertEntity));
            return newKey;
        }

        private async Task<TKey> InsertKeyedEntityCoreAsync<T, TKey>(T insertEntity, CancellationToken cancellationToken) where T : IEntity<TKey> where TKey : notnull
        {
            bool synchronizeStore = ShouldSynchronizeEntityStore(insertEntity);
            TKey originalKey = insertEntity.ID;
            EnqueueRollbackAction(() => insertEntity.ID = originalKey);
            PrepareClientGeneratedKey<T, TKey>(insertEntity);
            if (insertEntity is IAuditableEntity { IsAuditable: true } auditable && auditable.InsertTime <= DateTime.MinValue)
            {
                DateTime originalInsertTime = auditable.InsertTime;
                EnqueueRollbackAction(() => auditable.InsertTime = originalInsertTime);
                auditable.InsertTime = DateTime.Now;
            }
            if (synchronizeStore)
            {
                if (!IsIdentityKey<T>() && RequiredEntityStore.FindEntity<T, TKey>(insertEntity.ID) != null)
                    throw new InvalidOperationException($"{typeof(T).Name} with key {insertEntity.ID} is already loaded in EntityStore.");
            }
            CompiledDbCommand command = SqlGenerator.Insert<T, TKey>(insertEntity);
            object? result = await ExecuteScalarAsync(command.CommandText, command.Parameters, cancellationToken).ConfigureAwait(false);
            if (IsIdentityKey<T>() && (result == null || result == DBNull.Value))
                throw new InvalidOperationException($"The database did not return the generated key for {typeof(T).Name}.");
            TKey newKey = EntityKeyConverter.ConvertTo<TKey>(result ?? insertEntity.ID);
            insertEntity.ID = newKey;
            if (synchronizeStore)
            {
                ApplyOrEnqueueStoreMutation(() => RequiredEntityStore.InsertEntity<T, TKey>(insertEntity), "insert", typeof(T));
            }
            else if (_entityStore != null)
                ApplyOrEnqueueStoreMutation(() => _entityStore.InsertEntity<T, TKey>(insertEntity), "insert", typeof(T));
            if (insertEntity is IEntity legacyEntity)
            {
                await CascadeEntityActionAsync(legacyEntity, nameof(CascadeInsertEntityAsync), CascadeType.Insert, cancellationToken).ConfigureAwait(false);
            }
            if (_entityStore == null)
                PublishOrEnqueueEntityEvent(EntityChangeTracker.CreateInsert(insertEntity));
            return newKey;
        }

        private static void PrepareClientGeneratedKey<T, TKey>(T entity) where T : IEntity<TKey> where TKey : notnull
        {
            PropertyInfo idProperty = GetKeyProperty<T>();
            EntityColumnAttribute? attribute = idProperty.GetCustomAttribute<EntityColumnAttribute>();
            if (attribute?.IsIdentity != true && typeof(TKey) == typeof(Guid) && EqualityComparer<TKey>.Default.Equals(entity.ID, default!))
                entity.ID = (TKey)(object)Guid.NewGuid();
        }

        private static bool IsIdentityKey<T>() => GetKeyProperty<T>().GetCustomAttribute<EntityColumnAttribute>()?.IsIdentity == true;

        private static PropertyInfo GetKeyProperty<T>() => typeof(T).GetProperties().FirstOrDefault(property => property.GetCustomAttribute<EntityColumnAttribute>()?.IsPrimaryKey == true) ?? typeof(T).GetProperty("ID") ?? throw new InvalidOperationException($"{typeof(T).Name} does not define a primary key.");

        private static bool HasMappedColumnChanges<T>(T current, T incoming, IReadOnlyList<PropertyInfo>? selectedProperties)
        {
            PropertyInfo[] properties = selectedProperties == null
                ? _cachedDirtyCheckProperties.GetOrAdd(typeof(T), static entityType =>
                    [.. entityType.GetProperties()
                        .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                        .Select(property => (Property: property, Attribute: property.GetCustomAttribute<EntityColumnAttribute>(inherit: true)))
                        .Where(item => item.Attribute != null &&
                                       !item.Attribute.IsIdentity &&
                                       !item.Attribute.IsPrimaryKey &&
                                       !item.Attribute.IsReadOnly &&
                                       !IsAuditTimestamp(item.Property.Name))
                        .Select(item => item.Property)])
                : [.. selectedProperties.Where(property => property.CanRead && !IsAuditTimestamp(property.Name))];

            foreach (PropertyInfo property in properties)
            {
                object? currentValue = property.GetValue(current);
                object? incomingValue = property.GetValue(incoming);
                if (!AreMappedValuesEqual(currentValue, incomingValue))
                    return true;
            }

            return false;
        }

        private static bool IsAuditTimestamp(string propertyName) =>
            string.Equals(propertyName, nameof(IAuditableEntity.InsertTime), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(propertyName, nameof(IAuditableEntity.UpdateTime), StringComparison.OrdinalIgnoreCase);

        private static bool AreMappedValuesEqual(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            if (left is Array && right is Array)
                return StructuralComparisons.StructuralEqualityComparer.Equals(left, right);
            return left.Equals(right);
        }

        private bool UpdateKeyedEntityCore<T, TKey>(T entity, IReadOnlyList<PropertyInfo>? properties) where T : IEntity<TKey> where TKey : notnull
        {
            bool synchronizeStore = ShouldSynchronizeEntityStore(entity);
            if (entity is IEntity legacyEntity)
            {
                IEntity cascadeEntity = legacyEntity;
                if (synchronizeStore && _entityStore!.FindEntity<T, TKey>(entity.ID) is IEntity storedLegacyEntity)
                    cascadeEntity = storedLegacyEntity;
                bool softDeletes = legacyEntity.IsAuditable && legacyEntity.IsDeleted && (properties == null || properties.Any(property => string.Equals(property.Name, nameof(IEntity.IsDeleted), StringComparison.OrdinalIgnoreCase)));
                if (softDeletes)
                    CascadeEntityAction(cascadeEntity, nameof(CascadeSoftDeleteEntity), CascadeType.SoftDelete);
                else if (properties == null)
                    CascadeEntityAction(legacyEntity, nameof(CascadeUpdateEntity), CascadeType.Update);
            }
            if (synchronizeStore &&
                _entityStore!.FindEntity<T, TKey>(entity.ID) is T currentEntity &&
                !HasMappedColumnChanges(currentEntity, entity, properties))
            {
                return true;
            }
            IAuditableEntity? auditEntity = null;
            DateTime originalUpdateTime = default;
            if (entity is IAuditableEntity { IsAuditable: true } auditable)
            {
                auditEntity = auditable;
                originalUpdateTime = auditable.UpdateTime;
                EnqueueRollbackAction(() => auditable.UpdateTime = originalUpdateTime);
                auditable.UpdateTime = DateTime.Now;
            }
            T storeEntity = synchronizeStore && properties != null ? CreateGenericStoreUpdateEntity<T, TKey>(entity, properties) : entity;
            CompiledDbCommand command = properties == null ? SqlGenerator.Update<T, TKey>(entity) : SqlGenerator.Update<T, TKey>(entity, properties);
            if (ExecuteNonQuery(command.CommandText, command.Parameters) <= 0)
            {
                auditEntity?.UpdateTime = originalUpdateTime;
                return false;
            }
            if (synchronizeStore)
            {
                ApplyOrEnqueueStoreMutation(() => RequiredEntityStore.UpdateEntity<T, TKey>(storeEntity), "update", typeof(T));
            }
            else if (_entityStore != null)
                ApplyOrEnqueueStoreMutation(() => _entityStore.UpdateEntity<T, TKey>(entity), "update", typeof(T));
            else
                PublishOrEnqueueEntityEvent(EntityChangeTracker.CreateUpdate(entity));
            return true;
        }

        private async Task<bool> UpdateKeyedEntityCoreAsync<T, TKey>(T entity, IReadOnlyList<PropertyInfo>? properties, CancellationToken cancellationToken) where T : IEntity<TKey> where TKey : notnull
        {
            bool synchronizeStore = ShouldSynchronizeEntityStore(entity);
            if (entity is IEntity legacyEntity)
            {
                IEntity cascadeEntity = legacyEntity;
                if (synchronizeStore && _entityStore!.FindEntity<T, TKey>(entity.ID) is IEntity storedLegacyEntity)
                    cascadeEntity = storedLegacyEntity;
                bool softDeletes = legacyEntity.IsAuditable && legacyEntity.IsDeleted && (properties == null || properties.Any(property => string.Equals(property.Name, nameof(IEntity.IsDeleted), StringComparison.OrdinalIgnoreCase)));
                if (softDeletes)
                    await CascadeEntityActionAsync(cascadeEntity, nameof(CascadeSoftDeleteEntityAsync), CascadeType.SoftDelete, cancellationToken).ConfigureAwait(false);
                else if (properties == null)
                    await CascadeEntityActionAsync(legacyEntity, nameof(CascadeUpdateEntityAsync), CascadeType.Update, cancellationToken).ConfigureAwait(false);
            }
            if (synchronizeStore &&
                _entityStore!.FindEntity<T, TKey>(entity.ID) is T currentEntity &&
                !HasMappedColumnChanges(currentEntity, entity, properties))
            {
                return true;
            }
            IAuditableEntity? auditEntity = null;
            DateTime originalUpdateTime = default;
            if (entity is IAuditableEntity { IsAuditable: true } auditable)
            {
                auditEntity = auditable;
                originalUpdateTime = auditable.UpdateTime;
                EnqueueRollbackAction(() => auditable.UpdateTime = originalUpdateTime);
                auditable.UpdateTime = DateTime.Now;
            }
            T storeEntity = synchronizeStore && properties != null ? CreateGenericStoreUpdateEntity<T, TKey>(entity, properties) : entity;
            CompiledDbCommand command = properties == null ? SqlGenerator.Update<T, TKey>(entity) : SqlGenerator.Update<T, TKey>(entity, properties);
            if (await ExecuteNonQueryAsync(command.CommandText, command.Parameters, cancellationToken).ConfigureAwait(false) <= 0)
            {
                auditEntity?.UpdateTime = originalUpdateTime;
                return false;
            }
            if (synchronizeStore)
            {
                ApplyOrEnqueueStoreMutation(() => RequiredEntityStore.UpdateEntity<T, TKey>(storeEntity), "update", typeof(T));
            }
            else if (_entityStore != null)
                ApplyOrEnqueueStoreMutation(() => _entityStore.UpdateEntity<T, TKey>(entity), "update", typeof(T));
            else
                PublishOrEnqueueEntityEvent(EntityChangeTracker.CreateUpdate(entity));
            return true;
        }

        private bool DeleteKeyedEntityCore<T, TKey>(T entity) where T : IEntity<TKey> where TKey : notnull
        {
            bool synchronizeStore = ShouldSynchronizeEntityStore(entity);
            object? storedEntity = synchronizeStore ? _entityStore!.FindEntity<T, TKey>(entity.ID) : null;
            if (entity is IEntity legacyEntity)
                CascadeEntityAction(storedEntity as IEntity ?? legacyEntity, nameof(CascadeDeleteEntity), CascadeType.Delete);
            else if (synchronizeStore && storedEntity == null)
                throw new InvalidOperationException($"{typeof(T).Name} with key {entity.ID} is not loaded in EntityStore.");
            CompiledDbCommand command = SqlGenerator.Delete<T, TKey>(entity);
            if (ExecuteNonQuery(command.CommandText, command.Parameters) <= 0)
                return false;
            if (synchronizeStore)
                ApplyOrEnqueueStoreMutation(() => _entityStore!.DeleteEntity<T, TKey>(entity), "delete", typeof(T));
            else if (_entityStore != null)
                ApplyOrEnqueueStoreMutation(() => _entityStore.DeleteEntity<T, TKey>(entity), "delete", typeof(T));
            else
                PublishOrEnqueueEntityEvent(EntityChangeTracker.CreateDelete(entity));
            return true;
        }

        private async Task<bool> DeleteKeyedEntityCoreAsync<T, TKey>(T entity, CancellationToken cancellationToken) where T : IEntity<TKey> where TKey : notnull
        {
            bool synchronizeStore = ShouldSynchronizeEntityStore(entity);
            object? storedEntity = synchronizeStore ? _entityStore!.FindEntity<T, TKey>(entity.ID) : null;
            if (entity is IEntity legacyEntity)
                await CascadeEntityActionAsync(storedEntity as IEntity ?? legacyEntity, nameof(CascadeDeleteEntityAsync), CascadeType.Delete, cancellationToken).ConfigureAwait(false);
            else if (synchronizeStore && storedEntity == null)
                throw new InvalidOperationException($"{typeof(T).Name} with key {entity.ID} is not loaded in EntityStore.");
            CompiledDbCommand command = SqlGenerator.Delete<T, TKey>(entity);
            if (await ExecuteNonQueryAsync(command.CommandText, command.Parameters, cancellationToken).ConfigureAwait(false) <= 0)
                return false;
            if (synchronizeStore)
                ApplyOrEnqueueStoreMutation(() => _entityStore!.DeleteEntity<T, TKey>(entity), "delete", typeof(T));
            else if (_entityStore != null)
                ApplyOrEnqueueStoreMutation(() => _entityStore.DeleteEntity<T, TKey>(entity), "delete", typeof(T));
            else
                PublishOrEnqueueEntityEvent(EntityChangeTracker.CreateDelete(entity));
            return true;
        }

        private bool ShouldSynchronizeEntityStore<T>(T entity) where T : IDatabaseEntity =>
            _entityStore != null && entity is not IEntity { IsLogTable: true };

        private static PropertyInfo[] GetSelectedDatabaseProperties<T>(IReadOnlyList<Expression<Func<T, object?>>> properties) where T : IDatabaseEntity
        {
            ArgumentNullException.ThrowIfNull(properties);
            if (properties.Count == 0)
                throw new ArgumentException("At least one update property must be selected.", nameof(properties));
            return [.. properties.Select(expression => expression.Body is UnaryExpression unary ? unary.Operand : expression.Body).Select(body => body is MemberExpression { Member: PropertyInfo property } ? property : throw new ArgumentException("Each expression must select a mapped property.", nameof(properties)))];
        }

        private static PropertyInfo GetExpressionProperty<T>(Expression<Func<T, object?>> expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            Expression body = expression.Body is UnaryExpression unary ? unary.Operand : expression.Body;
            if (body is not MemberExpression { Member: PropertyInfo property } || property.DeclaringType?.IsAssignableFrom(typeof(T)) != true)
                throw new ArgumentException("The expression must select a direct property of the child entity.", nameof(expression));
            if (!property.CanWrite || property.GetIndexParameters().Length != 0 || !property.IsDefined(typeof(EntityColumnAttribute), true))
                throw new ArgumentException($"{typeof(T).Name}.{property.Name} must be a writable mapped column.", nameof(expression));
            return property;
        }

        private T CreateGenericStoreUpdateEntity<T, TKey>(T updateEntity, IReadOnlyList<PropertyInfo>? properties) where T : IEntity<TKey> where TKey : notnull
        {
            if (_entityStore == null || properties == null)
                return CreateDatabaseSnapshot(updateEntity);
            T current = _entityStore.FindEntity<T, TKey>(updateEntity.ID) ?? throw new InvalidOperationException($"{typeof(T).Name} with key {updateEntity.ID} is not loaded in EntityStore.");
            T merged = CreateDatabaseSnapshot(current);
            foreach (PropertyInfo property in properties)
                property.SetValue(merged, property.GetValue(updateEntity));
            if (updateEntity is IAuditableEntity sourceAudit && merged is IAuditableEntity targetAudit)
                targetAudit.UpdateTime = sourceAudit.UpdateTime;
            return merged;
        }

        private static T CreateDatabaseSnapshot<T>(T source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (typeof(T).IsValueType || typeof(T).IsAbstract || typeof(T).IsInterface)
                throw new InvalidOperationException($"{typeof(T).Name} must be a concrete reference type for EntityStore snapshots.");
            T? snapshot;
            try
            {
                snapshot = Activator.CreateInstance<T>()
                    ?? throw new InvalidOperationException($"Failed to create an EntityStore snapshot for {typeof(T).Name}.");
            }
            catch (MissingMethodException ex)
            {
                throw new InvalidOperationException($"{typeof(T).Name} requires a public parameterless constructor when EntityStore is enabled.", ex);
            }

            foreach (PropertyInfo property in typeof(T).GetProperties().Where(property => property.CanRead && property.CanWrite && property.IsDefined(typeof(EntityColumnAttribute), true)))
                property.SetValue(snapshot, property.GetValue(source));
            return snapshot;
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

        private void CascadeEntityAction<T>(T entity, string methodName, CascadeType cascadeType) where T : IEntity
        {
            var propertySchemas = entity.GetRelatedListProps();
            foreach (var propertySchema in propertySchemas)
            {
                if (!propertySchema.ForeignKeyAttribs.Cascade.HasFlag(cascadeType))
                    continue;

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

        private async Task CascadeEntityActionAsync<T>(T entity, string methodName, CascadeType cascadeType, CancellationToken cancellationToken) where T : IEntity
        {
            foreach (var propertySchema in entity.GetRelatedListProps())
            {
                if (!propertySchema.ForeignKeyAttribs.Cascade.HasFlag(cascadeType))
                    continue;

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

                object? result = methodName == nameof(CascadeDeleteEntityAsync) || methodName == nameof(CascadeSoftDeleteEntityAsync)
                    ? methodInfo.Invoke(this, [value, cancellationToken])
                    : methodInfo.Invoke(this, [value, propertySchema.ForeignKeyAttribs.ForeignKeyField, entity.ID, cancellationToken]);
                if (result is Task task)
                    await task.ConfigureAwait(false);
            }
        }

        protected void CascadeUpdateEntity<T>(ObservableCollection<T> updateEntities, string fkFieldName, object fkID) where T : Entity
        {
            foreach (var item in updateEntities)
            {
                if (item.ID <= 0)
                {
                    PropertyInfo? property = item.GetType().GetProperty(fkFieldName);
                    if (property != null && property.CanWrite) // 속성이 쓰기 가능한지 확인
                    {
                        // 새로운 값을 설정
                        EntityKeyConverter.SetForeignKey(item, property, fkID);
                    }

                    InsertEntity<T, int>(item);
                }
                else
                {
                    UpdateEntity<T, int>(item);
                }
            }
        }

        protected void CascadeDeleteEntity<T>(ObservableCollection<T> deleteEntities) where T : Entity
        {
            foreach (var item in deleteEntities)
            {
                DeleteEntity<T, int>(item);
            }
        }

        protected void CascadeSoftDeleteEntity<T>(ObservableCollection<T> deleteEntities) where T : Entity
        {
            foreach (T item in deleteEntities)
            {
                if (!item.IsAuditable || item.IsDeleted)
                    continue;

                T softDeleteEntity = item.Copy<T>()
                    ?? throw new InvalidOperationException($"Failed to copy {typeof(T).Name} for cascading soft-delete.");
                softDeleteEntity.IsDeleted = true;
                UpdateEntity<T, int>(softDeleteEntity);
            }
        }

        protected void CascadeInsertEntity<T>(ObservableCollection<T> insertEntities, string fkFieldName, object fkID) where T : Entity
        {
            foreach (var item in insertEntities)
            {
                PropertyInfo? property = item.GetType().GetProperty(fkFieldName);
                if (property != null && property.CanWrite) // 속성이 쓰기 가능한지 확인
                {
                    // 새로운 값을 설정
                    EntityKeyConverter.SetForeignKey(item, property, fkID);
                }

                InsertEntity<T, int>(item);
            }
        }

        protected async Task CascadeUpdateEntityAsync<T>(ObservableCollection<T> updateEntities, string fkFieldName, object fkID, CancellationToken cancellationToken) where T : Entity
        {
            foreach (T item in updateEntities)
            {
                if (item.ID <= 0)
                {
                    PropertyInfo? property = item.GetType().GetProperty(fkFieldName);
                    if (property != null && property.CanWrite)
                        EntityKeyConverter.SetForeignKey(item, property, fkID);
                    await InsertEntityAsync<T, int>(item, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await UpdateEntityAsync<T, int>(item, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        protected async Task CascadeDeleteEntityAsync<T>(ObservableCollection<T> deleteEntities, CancellationToken cancellationToken) where T : Entity
        {
            foreach (T item in deleteEntities)
                await DeleteEntityAsync<T, int>(item, cancellationToken).ConfigureAwait(false);
        }

        protected async Task CascadeSoftDeleteEntityAsync<T>(ObservableCollection<T> deleteEntities, CancellationToken cancellationToken) where T : Entity
        {
            foreach (T item in deleteEntities)
            {
                if (!item.IsAuditable || item.IsDeleted)
                    continue;

                T softDeleteEntity = item.Copy<T>()
                    ?? throw new InvalidOperationException($"Failed to copy {typeof(T).Name} for cascading soft-delete.");
                softDeleteEntity.IsDeleted = true;
                await UpdateEntityAsync<T, int>(softDeleteEntity, cancellationToken).ConfigureAwait(false);
            }
        }

        protected async Task CascadeInsertEntityAsync<T>(ObservableCollection<T> insertEntities, string fkFieldName, object fkID, CancellationToken cancellationToken) where T : Entity
        {
            foreach (T item in insertEntities)
            {
                PropertyInfo? property = item.GetType().GetProperty(fkFieldName);
                if (property != null && property.CanWrite)
                    EntityKeyConverter.SetForeignKey(item, property, fkID);
                await InsertEntityAsync<T, int>(item, cancellationToken).ConfigureAwait(false);
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

        protected virtual Task<int> ExecuteBulkInsertChunkAsync<T>(IReadOnlyList<T> entities, int offset, int count, IReadOnlyList<PropertyInfo> properties, CancellationToken cancellationToken) where T : IDatabaseEntity
            => throw new NotSupportedException($"{GetType().Name} does not support bulk insert.");

        protected static PropertyInfo[] GetBulkInsertProperties<T>() where T : IDatabaseEntity =>
            [.. typeof(T).GetProperties().Where(property =>
                property.CanWrite &&
                property.IsDefined(typeof(EntityColumnAttribute), inherit: true) &&
                !property.GetCustomAttributes(typeof(EntityColumnAttribute), true).Cast<EntityColumnAttribute>().First().IsIdentity &&
                !property.GetCustomAttributes(typeof(EntityColumnAttribute), true).Cast<EntityColumnAttribute>().First().IsReadOnly)];

        protected static object NormalizeBulkInsertValue(PropertyInfo property, object? value)
        {
            EntityColumnAttribute? attribute = property.GetCustomAttributes(typeof(EntityColumnAttribute), true).Cast<EntityColumnAttribute>().FirstOrDefault();
            if (attribute?.ForeignType != null && value is int foreignKey && foreignKey <= 0)
                return DBNull.Value;
            if (value == null || value is string { Length: 0 })
                return DBNull.Value;
            if (value is int integer && integer < 0)
                return DBNull.Value;
            if (value is DateTime dateTime && dateTime <= DateTime.MinValue)
                return DBNull.Value;
            if (value is Guid guid && guid == Guid.Empty)
                return DBNull.Value;
            if (value.GetType().IsEnum)
            {
                return attribute?.SqlDataType switch
                {
                    SqlDbType.TinyInt or SqlDbType.SmallInt => Convert.ToInt16(value),
                    SqlDbType.Int => Convert.ToInt32(value),
                    SqlDbType.BigInt => Convert.ToInt64(value),
                    _ => Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()))
                };
            }

            return value;
        }

        protected override void OnDispose()
        {
            if (_entityStore != null)
            {
                this._entityStore.EntityChanged -= EntityStore_EntityChanged;
            }
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




