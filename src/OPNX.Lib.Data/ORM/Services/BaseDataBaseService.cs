using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Common.Network;
using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Enums;
using OPNX.Lib.Data.ORM.EventHandlers;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Services
{
    public abstract class BaseDataBaseService : DisposableBase, IDataBaseService
    {
        #region Fields
        private int _commandTimeout = 10000;
        private string _connectionString = string.Empty;

        private static readonly ConcurrentDictionary<string, MethodInfo> _cachedGenericMethods = new();

        private readonly IEntityStore _entityStore;

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
        public BaseDataBaseService(string connectionString)
            : this(connectionString, new EntityStore())
        {
        }

        public BaseDataBaseService(string connectionString, IEntityStore? entityStore)
            : base()
        {
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

        public DbConnection? OpenDataBase() => OpenDataBase(_connectionString);

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

                LogManager.Error(ex);
            }

            return null;
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
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }

        public void ExecuteInTransaction(Action work)
        {
            ExecuteInTransaction(() =>
            {
                work();
                return true;
            });
        }

        public TResult ExecuteInTransaction<TResult>(Func<TResult> work)
        {
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
                return default!;

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
                LogManager.Error(ex);
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

        public virtual int ExecuteNonQuery(string sqlQuery, List<KeyValuePair<string, object>> paramList) => int.MinValue;

        public virtual DataTable? ExecuteReader(string sqlQuery, List<KeyValuePair<string, object>> paramList) => null;

        public virtual object? ExecuteScalar(string sqlQuery, List<KeyValuePair<string, object>> paramList) => null;

        public virtual int InsertEntity<T>(T insertEntity) where T : IEntity
        {
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransaction(() => InsertEntityCore(insertEntity));

            return InsertEntityCore(insertEntity);
        }

        public virtual bool UpdateEntity<T>(T updateEntity) where T : IEntity
        {
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransaction(() => UpdateEntityCore(updateEntity));

            return UpdateEntityCore(updateEntity);
        }

        public virtual bool DeleteEntity<T>(T deleteEntity) where T : IEntity
        {
            if (AutoTransactionForEntityOperations && CurrentTransaction == null)
                return ExecuteInTransaction(() => DeleteEntityCore(deleteEntity));

            return DeleteEntityCore(deleteEntity);
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
                catch (Exception ex)
                {
                    LogManager.Error(ex);
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
                catch (Exception ex)
                {
                    LogManager.Error(ex);
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

            int newId = 0;

            if (returnObj is int intValue)
            {
                newId = intValue;
            }
            else if (returnObj is long longValue)
            {
                newId = (int)longValue; // 주의: long이 int 범위 넘어가면 Overflow 발생
            }
            else if (returnObj is decimal decimalValue)
            {
                newId = (int)decimalValue;
            }
            else if (returnObj != null && int.TryParse(returnObj.ToString(), out int parsed))
            {
                newId = parsed;
            }

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
        protected abstract DbConnection CreateConnection(string connectionString);
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
                            catch (Exception ex)
                            {
                                LogManager.Error(ex);
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
            catch (Exception ex)
            {
                LogManager.Error(ex);
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
                            catch (Exception ex)
                            {
                                LogManager.Error(ex);
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
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }

            return result;
        }
        #endregion
    }
}
