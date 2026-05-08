using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Common.Serialization;
using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.EventHandlers;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OPNX.Lib.Data.ORM
{
    [Serializable]
    public partial class EntityStore : DisposableBase, IEntityStore, INotifyPropertyChanged
    {
        #region Fields
        private readonly ConcurrentDictionary<Type, object> _allEntitis = new();

        private static readonly ConcurrentDictionary<Type, MethodInfo> _cachedFindEntityMethods = new();

        protected static readonly ConcurrentDictionary<(Type typeT, Type typeU), MethodInfo> _cachedRefreshMethods = new();
        protected static readonly ConcurrentDictionary<(string methodName, Type type), MethodInfo> _cachedGenericHandlers = new();
        #endregion

        #region Constructors
        public EntityStore()
        {

        }
        #endregion

        #region Properties
        public ConcurrentDictionary<Type, object> AllEntitis
        {
            get { return _allEntitis; }
        }
        #endregion

        #region Events
        public event EntityChangedEventHandler? EntityChanged;
        protected void OnEntityChanged(DataChangedTypes changedType, IEntity? oldEntity, IEntity? newEntity)
        {
            EntityChanged?.Invoke(this, new EntityChangedEventArgs(changedType, oldEntity, newEntity));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Public Methods        

        public void ClearAllData()
        {
            try
            {
                _allEntitis.Clear();
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }

        public virtual int InsertEntity<T>(T? insertEntity) where T : IEntity
        {
            if (insertEntity is null)
                throw new ArgumentNullException(nameof(insertEntity));

            try
            {
                if (insertEntity.IsLogTable)
                    return insertEntity.ID;

                var items = GetEntities<T>();
                if (items == null)
                    return insertEntity.ID;

                if (insertEntity.ID <= 0 || items.Any(x => x.ID == insertEntity.ID))
                    return insertEntity.ID;

                insertEntity.Initialize(this);
                items.Add(insertEntity);

                insertEntity.Insert<T>();
                insertEntity.PropertyChanged += Entity_PropertyChanged;

                RefreshRelationProperties(insertEntity);
                OnEntityChanged(DataChangedTypes.Insert, null, insertEntity);
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to insert the entity. EntityType={typeof(T).Name}, Error={ex}.");
            }

            return insertEntity.ID;
        }

        public virtual bool UpdateEntity<T>(T? updateEntity) where T : IEntity
        {
            if (updateEntity is null)
                throw new ArgumentNullException(nameof(updateEntity));

            try
            {
                if (updateEntity.IsLogTable)
                    return false;

                T? findEntity = FindEntity<T>(updateEntity.ID);
                if (findEntity == null)
                    return false;

                findEntity.PropertyChanged -= Entity_PropertyChanged;

                if (updateEntity.IsDeleted)
                {
                    DeleteEntity<T>(findEntity);
                }
                else
                {
                    findEntity.Update<T>(updateEntity);
                    RefreshRelationProperties(findEntity);
                    OnEntityChanged(DataChangedTypes.Update, findEntity, findEntity.Update<T>(updateEntity));
                    findEntity.PropertyChanged += Entity_PropertyChanged;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to update the entity. EntityType={typeof(T).Name}, Error={ex}.");
            }

            return false;
        }

        public virtual bool DeleteEntity<T>(T? deleteEntity) where T : IEntity
        {
            if (deleteEntity is null)
                throw new ArgumentNullException(nameof(deleteEntity));

            try
            {
                var items = GetEntities<T>();
                if (items == null)
                    return false;

                var findEntity = items.FirstOrDefault(x => x.ID == deleteEntity.ID);
                if (findEntity == null)
                    return false;

                items.Remove(findEntity);

                findEntity.PropertyChanged -= Entity_PropertyChanged;
                findEntity.Delete<T>();

                RefreshRelationProperties(findEntity);
                OnEntityChanged(DataChangedTypes.Delete, null, findEntity);

                return true;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to delete the entity. EntityType={typeof(T).Name}, Error={ex}.");

                return false;
            }
        }

        public ObservableCollection<T> GetEntities<T>() where T : IEntity
        {
            return (ObservableCollection<T>)_allEntitis.GetOrAdd(typeof(T), _ => new ObservableCollection<T>());
        }

        public IEntity? FindEntity(Type entityType, int id)
        {
            try
            {
                if (!_cachedFindEntityMethods.TryGetValue(entityType, out MethodInfo? genericMethod))
                {
                    MethodInfo? method = GetType().GetMethod(
                        nameof(FindEntity),
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        [typeof(int)],
                        null
                    );

                    if (method == null)
                        return null;

                    genericMethod = method.MakeGenericMethod(entityType);
                    _cachedFindEntityMethods.TryAdd(entityType, genericMethod);
                }

                object? result = genericMethod.Invoke(this, [id]);
                return result != null ? result as IEntity : null;
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }

            return null;
        }

        public T? FindEntity<T>(Type entityType, int id) where T : IEntity
        {
            try
            {
                if (!_cachedFindEntityMethods.TryGetValue(entityType, out MethodInfo? genericMethod))
                {
                    MethodInfo? methodInfo = GetType().GetMethod(
                        nameof(this.FindEntity),
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        [typeof(int)],
                        null
                    );

                    if (methodInfo == null)
                        return default;

                    genericMethod = methodInfo.MakeGenericMethod(entityType);
                    _cachedFindEntityMethods.TryAdd(entityType, genericMethod);
                }

                object? obj = genericMethod.Invoke(this, [id]);
                return obj != null ? (T)obj : default;
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }

            return default;
        }

        public T? FindEntity<T>(int id) where T : IEntity
        {
            return FindEntity<T>(x => x.ID == id);
        }

        public T? FindEntity<T>(Func<T, bool> predicate)
            where T : IEntity
        {
            try
            {

                return GetEntities<T>().FirstOrDefault(predicate);
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return default;
            }
        }

        public ObservableCollection<T> FindEntities<T>(Func<T, bool> predicate)
            where T : IEntity
        {
            try
            {
                var entities = GetEntities<T>();
                if (entities != null)
                    return new ObservableCollection<T>(entities.Where(predicate));
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to find entities. EntityType={typeof(T).Name}, Error={ex.Message}.");
            }
            return [];
        }
        #endregion        

        #region Private / Protected Methods
        private void Entity_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is IEntity entity)
            {
                EntityChanged?.Invoke(this, new EntityChangedEventArgs(DataChangedTypes.PropertyChanged, null, entity, e.PropertyName));
            }
        }

        protected void SetEntities<T>(string strEntityData) where T : Entity
        {
            try
            {
                ObservableCollection<T>? entityItems = JsonSerialize.Deserialize<ObservableCollection<T>>(strEntityData);

                SetEntities<T>(entityItems);
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }
        protected void SetEntities<T>(ObservableCollection<T>? entityData) where T : Entity
        {
            try
            {
                if (entityData?.Count <= 0)
                    return;

                Type entityType = typeof(T);

                _allEntitis.TryRemove(entityType, out _);

                foreach (var entity in entityData!)
                {
                    entity.Initialize(this);
                    entity.PropertyChanged += Entity_PropertyChanged;
                }

                _allEntitis.TryAdd(entityType, entityData);
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }

        protected override void OnDispose()
        {
            ClearAllData();
        }

        protected void RefreshRelationProperties<T>(T entity) where T : IEntity
        {
            var propertiesWithForeignType = GetPropertiesWithForeignType<T>();

            foreach (var property in propertiesWithForeignType)
            {
                Type? typeT = property.Attribute.ForeignType;
                Type typeU = typeof(T);

                var methodKey = (typeT, typeU);

                if (!_cachedRefreshMethods.TryGetValue(methodKey!, out var genericMethodInfo))
                {
                    var methodInfo = typeof(EntityStore).GetMethod(
                        nameof(EntityStore.RefreshRelationProperty),
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    if (methodInfo != null)
                    {
                        genericMethodInfo = methodInfo.MakeGenericMethod(typeT!, typeU);
                        _cachedRefreshMethods.TryAdd(methodKey!, genericMethodInfo);
                    }
                }

                if (genericMethodInfo != null)
                {
                    var obj = property.Property.GetValue(entity);

                    if (obj is int value)
                    {
                        genericMethodInfo.Invoke(this, [value]);
                    }
                }
            }
        }

        protected static IReadOnlyList<(PropertyInfo Property, DataColumnAttribute Attribute)> GetPropertiesWithForeignType<T>()
        {
            return typeof(T).GetProperties()
                .Select(p => (Property: p, Attribute: p.GetCustomAttribute<DataColumnAttribute>(inherit: true)))
                .Where(x => x.Attribute?.ForeignType != null)          // Attribute != null 이고 ForeignType != null
                .Select(x => (x.Property, x.Attribute!))               // 여기서 Attribute는 null 아님을 확정
                .ToList();
            //return typeof(T).GetProperties()
            //    .Select(p => (
            //        p,
            //        p.GetCustomAttribute<DataColumnAttribute>(inherit: true)
            //    ))
            //    .Where(x => x.Item2?.ForeignType != null)
            //    .ToList();
        }

        //protected static List<(PropertyInfo Property, DataColumnAttribute Attribute)> GetPropertiesWithForeignType<T>()
        //{
        //    return typeof(T).GetProperties()
        //                    .Where(p => p.IsDefined(typeof(DataColumnAttribute), true)) // DataColumnAttribute가 정의된 속성만 선택
        //                    .Select(p => new
        //                    {
        //                        Property = p,
        //                        Attribute = p.GetCustomAttributes(typeof(DataColumnAttribute), false)
        //                                     .Cast<DataColumnAttribute>()
        //                                     .FirstOrDefault()
        //                    })
        //                    .Where(x => x.Attribute != null && x.Attribute.ForeignType != null) // ForeignType이 null이 아닌 속성만 필터링
        //                    .Select(x => (x.Property, x.Attribute))
        //                    .ToList();
        //}

        protected void RefreshRelationProperty<T, U>(int id)
            where T : Entity where U : Entity
        {
            var findEntity = FindEntity<T>(id);
            if (findEntity != null)
            {
                var relatedListProps = findEntity.GetRelatedListProps();
                foreach (var relatedProp in relatedListProps)
                {
                    if (relatedProp.ForeignKeyAttribs.RelatedType == typeof(U))
                    {
                        relatedProp.Property.SetValue(findEntity, null);
                    }
                }
            }
        }
        #endregion
    }
}
