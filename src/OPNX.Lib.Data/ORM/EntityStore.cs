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
        private int _externalChangeDepth;
        protected bool IsApplyingExternalChange
        {
            get => Volatile.Read(ref _externalChangeDepth) > 0;
            set
            {
                if (value)
                    Interlocked.Increment(ref _externalChangeDepth);
                else
                    ExitExternalChange();
            }
        }

        protected static readonly ConcurrentDictionary<(Type typeT, Type typeU), MethodInfo> _cachedRefreshMethods = new();
        protected static readonly ConcurrentDictionary<(string methodName, Type type), MethodInfo> _cachedGenericHandlers = new();
        private static readonly ConcurrentDictionary<Type, IReadOnlyList<(PropertyInfo Property, EntityColumnAttribute Attribute)>> _cachedForeignKeyProperties = new();
        #endregion

        #region Properties
        public ConcurrentDictionary<Type, object> AllEntitis
        {
            get { return _allEntitis; }
        }
        #endregion

        #region Events
        public event EntityChangedEventHandler? EntityChanged;
        protected void OnEntityChanged(EntityChangedEventArgs eventArgs)
        {
            EntityChanged?.Invoke(this, eventArgs);
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
                OnEntityChanged(EntityChangeTracker.CreateInsert(logEntity));
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
                OnEntityChanged(EntityChangeTracker.CreateInsert(legacyEntity));
                return true;
            }
            entities.Add(insertEntity);
            OnEntityChanged(EntityChangeTracker.CreateInsert(insertEntity));
            return true;
        }

        public virtual bool UpdateEntity<T, TKey>(T updateEntity) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(updateEntity);
            if (updateEntity is IEntity { IsLogTable: true } logEntity)
            {
                OnEntityChanged(EntityChangeTracker.CreateUpdate(logEntity, logEntity));
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
                EntityChangedEventArgs change = EntityChangeTracker.CreateUpdate(currentLegacy, updateLegacy);
                IReadOnlyDictionary<PropertyInfo, int?> originalRelations = CaptureRelationReferences(currentLegacy, typeof(T));
                currentLegacy.NotifyUpdated<IEntity>(updateLegacy);
                RefreshChangedRelationProperties(currentLegacy, typeof(T), originalRelations);
                OnEntityChanged(change);
                currentLegacy.PropertyChanged += Entity_PropertyChanged;
                return true;
            }
            EntityChangedEventArgs genericChange = EntityChangeTracker.CreateUpdate(current, updateEntity);
            CopyDatabaseColumns(updateEntity, current);
            OnEntityChanged(genericChange);
            return true;
        }

        public virtual bool DeleteEntity<T, TKey>(T deleteEntity) where T : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(deleteEntity);
            if (deleteEntity is IEntity { IsLogTable: true } logEntity)
            {
                OnEntityChanged(EntityChangeTracker.CreateDelete(logEntity));
                return true;
            }
            ObservableCollection<T> entities = GetEntities<T, TKey>();
            T? current = entities.FirstOrDefault(entity => EqualityComparer<TKey>.Default.Equals(entity.ID, deleteEntity.ID));
            if (current == null)
                return false;
            if (current is IEntity currentLegacy)
            {
                EntityChangedEventArgs change = EntityChangeTracker.CreateDelete(currentLegacy);
                if (!entities.Remove(current))
                    return false;
                currentLegacy.PropertyChanged -= Entity_PropertyChanged;
                currentLegacy.NotifyDeleted<IEntity>();
                RefreshRelationProperties(currentLegacy, typeof(T));
                OnEntityChanged(change);
                return true;
            }
            EntityChangedEventArgs genericChange = EntityChangeTracker.CreateDelete(current);
            if (!entities.Remove(current))
                return false;
            OnEntityChanged(genericChange);
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

        private static void CopyDatabaseColumns<T>(T source, T target)
        {
            foreach (PropertyInfo property in typeof(T).GetProperties().Where(property => property.CanRead && property.CanWrite && property.IsDefined(typeof(EntityColumnAttribute), true)))
                property.SetValue(target, property.GetValue(source));
        }

        protected IDisposable BeginExternalChange()
        {
            Interlocked.Increment(ref _externalChangeDepth);
            return new ExternalChangeScope(this);
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
        private sealed class ExternalChangeScope(EntityStore owner) : IDisposable
        {
            private EntityStore? _owner = owner;

            public void Dispose()
            {
                EntityStore? current = Interlocked.Exchange(ref _owner, null);
                if (current != null)
                    current.ExitExternalChange();
            }
        }

        private void ExitExternalChange()
        {
            int current;
            do
            {
                current = Volatile.Read(ref _externalChangeDepth);
                if (current <= 0)
                    return;
            }
            while (Interlocked.CompareExchange(ref _externalChangeDepth, current - 1, current) != current);
        }

        private void Entity_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!IsApplyingExternalChange && sender is IEntity entity)
            {
                if (!string.IsNullOrWhiteSpace(e.PropertyName))
                    EntityChanged?.Invoke(this, EntityChangeTracker.CreatePropertyChanged(entity, e.PropertyName));
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

        protected IReadOnlyDictionary<PropertyInfo, int?> CaptureRelationReferences<T>(T entity) where T : IEntity
            => CaptureRelationReferences(entity, typeof(T));

        protected void RefreshChangedRelationProperties<T>(
            T entity,
            IReadOnlyDictionary<PropertyInfo, int?> originalRelations) where T : IEntity
            => RefreshChangedRelationProperties(entity, typeof(T), originalRelations);

        private static IReadOnlyDictionary<PropertyInfo, int?> CaptureRelationReferences(IEntity entity, Type entityType)
        {
            var references = new Dictionary<PropertyInfo, int?>();
            foreach (var property in GetPropertiesWithForeignType(entityType))
                references[property.Property] = GetRelationID(property.Property, entity);

            return references;
        }

        private void RefreshChangedRelationProperties(
            IEntity entity,
            Type entityType,
            IReadOnlyDictionary<PropertyInfo, int?> originalRelations)
        {
            foreach (var property in GetPropertiesWithForeignType(entityType))
            {
                originalRelations.TryGetValue(property.Property, out int? originalID);
                int? currentID = GetRelationID(property.Property, entity);
                if (originalID == currentID)
                    continue;

                if (originalID.HasValue)
                    RefreshRelationProperty(property.Attribute.ForeignType!, entityType, originalID.Value);
                if (currentID.HasValue)
                    RefreshRelationProperty(property.Attribute.ForeignType!, entityType, currentID.Value);
            }
        }

        private void RefreshRelationProperties(IEntity entity, Type entityType)
        {
            foreach (var property in GetPropertiesWithForeignType(entityType))
            {
                int? relationID = GetRelationID(property.Property, entity);
                if (relationID.HasValue)
                    RefreshRelationProperty(property.Attribute.ForeignType!, entityType, relationID.Value);
            }
        }

        private void RefreshRelationProperty(Type foreignType, Type entityType, int id)
        {
            var methodKey = (foreignType, entityType);
            if (!_cachedRefreshMethods.TryGetValue(methodKey, out MethodInfo? genericMethodInfo))
            {
                MethodInfo? methodInfo = typeof(EntityStore).GetMethod(
                    nameof(EntityStore.RefreshRelationProperty),
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    [typeof(int)]);
                if (methodInfo == null)
                    return;

                genericMethodInfo = methodInfo.MakeGenericMethod(foreignType, entityType);
                _cachedRefreshMethods.TryAdd(methodKey, genericMethodInfo);
            }

            genericMethodInfo.Invoke(this, [id]);
        }

        private static int? GetRelationID(PropertyInfo property, IEntity entity)
            => property.GetValue(entity) is int id ? id : null;

        protected static IReadOnlyList<(PropertyInfo Property, EntityColumnAttribute Attribute)> GetPropertiesWithForeignType<T>()
            => GetPropertiesWithForeignType(typeof(T));

        private static IReadOnlyList<(PropertyInfo Property, EntityColumnAttribute Attribute)> GetPropertiesWithForeignType(Type entityType)
        {
            return _cachedForeignKeyProperties.GetOrAdd(entityType, static type =>
                type.GetProperties()
                    .Select(p => (Property: p, Attribute: p.GetCustomAttribute<EntityColumnAttribute>(inherit: true)))
                    .Where(x => x.Attribute?.ForeignType != null)
                    .Select(x => (x.Property, x.Attribute!))
                    .ToArray());
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



