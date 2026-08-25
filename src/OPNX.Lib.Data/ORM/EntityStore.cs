using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Common.LifeCycle;
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
    public partial class EntityStore(ILogger<EntityStore>? logger = null) : DisposableObject, IEntityStore, INotifyPropertyChanged
    {
        #region Fields
        private readonly ConcurrentDictionary<Type, object> _allEntitis = new();
        private readonly ILogger<EntityStore> _logger = logger ?? NullLogger<EntityStore>.Instance;

        protected static readonly ConcurrentDictionary<(Type typeT, Type typeU), MethodInfo> _cachedRefreshMethods = new();
        protected static readonly ConcurrentDictionary<(string methodName, Type type), MethodInfo> _cachedGenericHandlers = new();
        #endregion

        #region Properties
        public ConcurrentDictionary<Type, object> AllEntitis
        {
            get { return _allEntitis; }
        }
        #endregion

        #region Events
        public event EntityChangedEventHandler? EntityChanged;
        protected void OnEntityChanged(DataChangedTypes changedType, IDatabaseEntity? oldEntity, IDatabaseEntity? newEntity)
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
                _logger.LogError(ex, "{Message}", ex.Message);
            }
        }

        public virtual bool InsertEntity<T, TKey>(T insertEntity) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(insertEntity);
            if (insertEntity is IEntity { IsLogTable: true } logEntity)
            {
                OnEntityChanged(DataChangedTypes.Insert, null, logEntity);
                return true;
            }
            ObservableCollection<T> entities = GetEntities<T, TKey>();
            if (entities.Any(entity => EqualityComparer<TKey>.Default.Equals(entity.ID, insertEntity.ID)))
                return false;
            if (insertEntity is IEntity legacyEntity)
            {
                if (legacyEntity.ID <= 0)
                    return false;
                legacyEntity.Initialize(this);
                entities.Add(insertEntity);
                legacyEntity.NotifyInserted<IEntity>();
                legacyEntity.PropertyChanged += Entity_PropertyChanged;
                RefreshRelationProperties(legacyEntity, typeof(T));
                OnEntityChanged(DataChangedTypes.Insert, null, legacyEntity);
                return true;
            }
            T snapshot = CreateDatabaseSnapshot(insertEntity);
            entities.Add(snapshot);
            OnEntityChanged(DataChangedTypes.Insert, null, snapshot);
            return true;
        }

        public virtual bool UpdateEntity<T, TKey>(T updateEntity) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(updateEntity);
            if (updateEntity is IEntity { IsLogTable: true } logEntity)
            {
                OnEntityChanged(DataChangedTypes.Update, null, logEntity);
                return true;
            }
            ObservableCollection<T> entities = GetEntities<T, TKey>();
            T? current = entities.FirstOrDefault(entity => EqualityComparer<TKey>.Default.Equals(entity.ID, updateEntity.ID));
            if (current == null)
                return false;
            if (current is IEntity currentLegacy && updateEntity is IEntity updateLegacy)
            {
                currentLegacy.PropertyChanged -= Entity_PropertyChanged;
                if (updateLegacy.IsAuditable && updateLegacy.IsDeleted)
                    return DeleteEntity<T, TKey>(current);
                IEntity updatedEntity = currentLegacy.NotifyUpdated<IEntity>(updateLegacy);
                RefreshRelationProperties(currentLegacy, typeof(T));
                OnEntityChanged(DataChangedTypes.Update, currentLegacy, updatedEntity);
                currentLegacy.PropertyChanged += Entity_PropertyChanged;
                return true;
            }
            int index = entities.IndexOf(current);
            T snapshot = CreateDatabaseSnapshot(updateEntity);
            entities[index] = snapshot;
            OnEntityChanged(DataChangedTypes.Update, current, snapshot);
            return true;
        }

        public virtual bool DeleteEntity<T, TKey>(T deleteEntity) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(deleteEntity);
            if (deleteEntity is IEntity { IsLogTable: true } logEntity)
            {
                OnEntityChanged(DataChangedTypes.Delete, null, logEntity);
                return true;
            }
            ObservableCollection<T> entities = GetEntities<T, TKey>();
            T? current = entities.FirstOrDefault(entity => EqualityComparer<TKey>.Default.Equals(entity.ID, deleteEntity.ID));
            if (current == null)
                return false;
            if (current is IEntity currentLegacy)
            {
                if (!entities.Remove(current))
                    return false;
                currentLegacy.PropertyChanged -= Entity_PropertyChanged;
                currentLegacy.NotifyDeleted<IEntity>();
                RefreshRelationProperties(currentLegacy, typeof(T));
                OnEntityChanged(DataChangedTypes.Delete, null, currentLegacy);
                return true;
            }
            if (!entities.Remove(current))
                return false;
            OnEntityChanged(DataChangedTypes.Delete, current, null);
            return true;
        }

        public ObservableCollection<T> GetEntities<T, TKey>() where T : IEntity<TKey> where TKey : notnull => (ObservableCollection<T>)_allEntitis.GetOrAdd(typeof(T), _ => new ObservableCollection<T>());

        public T? FindEntity<T, TKey>(TKey id) where T : IEntity<TKey> where TKey : notnull => GetEntities<T, TKey>().FirstOrDefault(entity => EqualityComparer<TKey>.Default.Equals(entity.ID, id));

        public IDatabaseEntity? FindEntity<TKey>(Type entityType, TKey id) where TKey : notnull
        {
            if (!_allEntitis.TryGetValue(entityType, out object? collection) || collection is not System.Collections.IEnumerable entities)
                return null;
            return entities.Cast<object>().OfType<IEntity<TKey>>().FirstOrDefault(entity => EqualityComparer<TKey>.Default.Equals(entity.ID, id));
        }

        public T? FindEntity<T, TKey>(Type entityType, TKey id) where T : IEntity<TKey> where TKey : notnull => FindEntity<TKey>(entityType, id) is T entity ? entity : default;

        private static T CreateDatabaseSnapshot<T>(T source)
        {
            if (typeof(T).IsValueType || typeof(T).IsAbstract || typeof(T).IsInterface)
                throw new InvalidOperationException($"{typeof(T).Name} must be a concrete reference type for EntityStore snapshots.");
            object snapshot;
            try { snapshot = Activator.CreateInstance(typeof(T)) ?? throw new InvalidOperationException($"Failed to create an EntityStore snapshot for {typeof(T).Name}."); }
            catch (MissingMethodException ex) { throw new InvalidOperationException($"{typeof(T).Name} requires a public parameterless constructor when EntityStore is enabled.", ex); }
            foreach (PropertyInfo property in typeof(T).GetProperties().Where(property => property.CanRead && property.CanWrite && property.IsDefined(typeof(EntityColumnAttribute), true)))
                property.SetValue(snapshot, property.GetValue(source));
            return (T)snapshot;
        }

        public T? FindEntity<T, TKey>(Func<T, bool> predicate) where T : IEntity<TKey> where TKey : notnull
        {
            try { return GetEntities<T, TKey>().FirstOrDefault(predicate); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to find entity. EntityType={EntityType}.", typeof(T).Name); return default; }
        }

        public ObservableCollection<T> FindEntities<T, TKey>(Func<T, bool> predicate) where T : IEntity<TKey> where TKey : notnull
        {
            try { return new ObservableCollection<T>(GetEntities<T, TKey>().Where(predicate)); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to find entities. EntityType={EntityType}.", typeof(T).Name); return []; }
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
                _logger.LogError(ex, "{Message}", ex.Message);
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
                _logger.LogError(ex, "{Message}", ex.Message);
            }
        }

        protected override void OnDispose()
        {
            ClearAllData();
        }

        protected void RefreshRelationProperties<T>(T entity) where T : IEntity
            => RefreshRelationProperties(entity, typeof(T));

        private void RefreshRelationProperties(IEntity entity, Type entityType)
        {
            var propertiesWithForeignType = GetPropertiesWithForeignType(entityType);

            foreach (var property in propertiesWithForeignType)
            {
                Type? typeT = property.Attribute.ForeignType;
                Type typeU = entityType;

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

        protected static IReadOnlyList<(PropertyInfo Property, EntityColumnAttribute Attribute)> GetPropertiesWithForeignType<T>()
            => GetPropertiesWithForeignType(typeof(T));

        private static IReadOnlyList<(PropertyInfo Property, EntityColumnAttribute Attribute)> GetPropertiesWithForeignType(Type entityType)
        {
            return entityType.GetProperties()
                .Select(p => (Property: p, Attribute: p.GetCustomAttribute<EntityColumnAttribute>(inherit: true)))
                .Where(x => x.Attribute?.ForeignType != null)          // Attribute != null 이고 ForeignType != null
                .Select(x => (x.Property, x.Attribute!))               // 여기서 Attribute는 null 아님을 확정
                .ToList();
            //return typeof(T).GetProperties()
            //    .Select(p => (
            //        p,
            //        p.GetCustomAttribute<EntityColumnAttribute>(inherit: true)
            //    ))
            //    .Where(x => x.Item2?.ForeignType != null)
            //    .ToList();
        }

        //protected static List<(PropertyInfo Property, EntityColumnAttribute Attribute)> GetPropertiesWithForeignType<T>()
        //{
        //    return typeof(T).GetProperties()
        //                    .Where(p => p.IsDefined(typeof(EntityColumnAttribute), true)) // EntityColumnAttribute가 정의된 속성만 선택
        //                    .Select(p => new
        //                    {
        //                        Property = p,
        //                        Attribute = p.GetCustomAttributes(typeof(EntityColumnAttribute), false)
        //                                     .Cast<EntityColumnAttribute>()
        //                                     .FirstOrDefault()
        //                    })
        //                    .Where(x => x.Attribute != null && x.Attribute.ForeignType != null) // ForeignType이 null이 아닌 속성만 필터링
        //                    .Select(x => (x.Property, x.Attribute))
        //                    .ToList();
        //}

        protected void RefreshRelationProperty<T, U>(int id)
            where T : Entity where U : Entity
        {
            var findEntity = FindEntity<T, int>(id);
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



