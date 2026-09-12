using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OPNX.Lib.Data.ORM.EventHandlers
{
    public enum DataChangedTypes { Insert, Update, Delete, PropertyChanged }

    public delegate void EntityChangedEventHandler(object sender, EntityChangedEventArgs e);

    public sealed class EntityChangedEventArgs : EventArgs
    {
        #region Fields
        private static readonly IReadOnlyDictionary<string, object?> EmptyValues =
            FrozenDictionary<string, object?>.Empty;
        private static readonly IReadOnlySet<string> EmptyProperties =
            FrozenSet<string>.Empty;
        #endregion

        #region Constructors
        internal EntityChangedEventArgs(DataChangedTypes changedType, Type entityType, object entityID,
            IReadOnlyDictionary<string, object?>? originalValues = null,
            IReadOnlyDictionary<string, object?>? currentValues = null,
            IReadOnlySet<string>? changedProperties = null)
        {
            ChangedType = changedType;
            EntityType = entityType;
            EntityID = entityID;
            OriginalValues = FreezeValues(originalValues);
            CurrentValues = FreezeValues(currentValues);
            ChangedProperties = changedProperties?.ToFrozenSet(StringComparer.Ordinal) ?? EmptyProperties;
        }
        #endregion

        #region Properties
        public DataChangedTypes ChangedType { get; }
        public Type EntityType { get; }
        public object EntityID { get; }
        public IReadOnlyDictionary<string, object?> OriginalValues { get; }
        public IReadOnlyDictionary<string, object?> CurrentValues { get; }
        public IReadOnlySet<string> ChangedProperties { get; }
        #endregion

        #region Public Methods
        public bool Is<TEntity>() where TEntity : IDatabaseEntity => EntityType == typeof(TEntity);
        public bool IsChanged(string propertyName) => ChangedProperties.Contains(propertyName);
        public bool IsChangedAny(params string[] propertyNames) => propertyNames.Any(ChangedProperties.Contains);
        public T? GetOriginalValue<T>(string propertyName) => ConvertValue<T>(OriginalValues, propertyName);
        public T? GetCurrentValue<T>(string propertyName) => ConvertValue<T>(CurrentValues, propertyName);

        public TEntity? FindCurrent<TEntity, TKey>(IEntityStore entityStore)
            where TEntity : IEntity<TKey> where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(entityStore);
            if (EntityType != typeof(TEntity)) return default;
            TKey key = (TKey)(EntityChangeTracker.ConvertValue(EntityID, typeof(TKey))
                ?? throw new InvalidOperationException($"Unable to convert key for {EntityType.Name}."));
            return entityStore.FindEntity<TEntity, TKey>(key);
        }
        #endregion

        #region Private Methods
        private static T? ConvertValue<T>(IReadOnlyDictionary<string, object?> values, string propertyName)
        {
            if (!values.TryGetValue(propertyName, out object? value) || value == null) return default;
            object? converted = EntityChangeTracker.ConvertValue(value, typeof(T));
            return converted is T typed ? typed : default;
        }

        private static IReadOnlyDictionary<string, object?> FreezeValues(
            IReadOnlyDictionary<string, object?>? values)
        {
            if (values == null || values.Count == 0)
                return EmptyValues;
            return values.ToFrozenDictionary(
                pair => pair.Key,
                pair => EntityChangeTracker.CaptureValue(pair.Value),
                StringComparer.Ordinal);
        }
        #endregion
    }

    public static class EntityChangeTracker
    {
        #region Fields
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> MappedProperties = new();
        private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, PropertyInfo>> PropertyMaps = new();
        #endregion

        #region Public Methods
        public static EntityChangedEventArgs CreateChange(DataChangedTypes changedType, Type entityType, object entityID,
            IReadOnlyDictionary<string, object?>? originalValues = null,
            IReadOnlyDictionary<string, object?>? currentValues = null,
            IEnumerable<string>? changedProperties = null) =>
            new(changedType, entityType, entityID, originalValues, currentValues,
                changedProperties == null ? null : new HashSet<string>(changedProperties, StringComparer.Ordinal));

        public static EntityChangedEventArgs CreateInsert(IDatabaseEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            Dictionary<string, object?> current = CaptureValues(entity);
            return new EntityChangedEventArgs(DataChangedTypes.Insert, entity.GetType(), GetKey(entity),
                currentValues: current,
                changedProperties: new HashSet<string>(current.Keys, StringComparer.Ordinal));
        }

        public static EntityChangedEventArgs CreateUpdate(IDatabaseEntity currentEntity, IDatabaseEntity incomingEntity)
        {
            ArgumentNullException.ThrowIfNull(currentEntity);
            ArgumentNullException.ThrowIfNull(incomingEntity);
            if (currentEntity.GetType() != incomingEntity.GetType())
                throw new ArgumentException("Update entities must have the same runtime type.", nameof(incomingEntity));

            var original = new Dictionary<string, object?>(StringComparer.Ordinal);
            var current = new Dictionary<string, object?>(StringComparer.Ordinal);
            var changed = new HashSet<string>(StringComparer.Ordinal);
            foreach (PropertyInfo property in GetMappedProperties(currentEntity.GetType()))
            {
                object? oldValue = property.GetValue(currentEntity);
                object? newValue = property.GetValue(incomingEntity);
                if (AreEqual(oldValue, newValue)) continue;
                original[property.Name] = CaptureValue(oldValue);
                current[property.Name] = CaptureValue(newValue);
                changed.Add(property.Name);
            }
            return new EntityChangedEventArgs(DataChangedTypes.Update, currentEntity.GetType(), GetKey(currentEntity), original, current, changed);
        }

        public static EntityChangedEventArgs CreateUpdate(IDatabaseEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            Dictionary<string, object?> current = CaptureValues(entity);
            return new EntityChangedEventArgs(DataChangedTypes.Update, entity.GetType(), GetKey(entity),
                currentValues: current,
                changedProperties: new HashSet<string>(current.Keys, StringComparer.Ordinal));
        }

        public static EntityChangedEventArgs CreateDelete(IDatabaseEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return new EntityChangedEventArgs(DataChangedTypes.Delete, entity.GetType(), GetKey(entity),
                originalValues: CaptureValues(entity));
        }

        public static EntityChangedEventArgs CreatePropertyChanged(IDatabaseEntity entity, string propertyName)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
            PropertyInfo? property = GetMappedProperties(entity.GetType()).FirstOrDefault(candidate => candidate.Name == propertyName);
            var current = new Dictionary<string, object?>(StringComparer.Ordinal);
            var changed = new HashSet<string>(StringComparer.Ordinal);
            if (property != null)
            {
                current[property.Name] = CaptureValue(property.GetValue(entity));
                changed.Add(property.Name);
            }
            return new EntityChangedEventArgs(DataChangedTypes.PropertyChanged, entity.GetType(), GetKey(entity),
                currentValues: current, changedProperties: changed);
        }

        public static Dictionary<string, object?> CaptureValues(IDatabaseEntity entity)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (PropertyInfo property in GetMappedProperties(entity.GetType()))
                values[property.Name] = CaptureValue(property.GetValue(entity));
            return values;
        }

        public static void ApplyValues(IDatabaseEntity entity, IReadOnlyDictionary<string, object?> values)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(values);
            IReadOnlyDictionary<string, PropertyInfo> propertyMap = PropertyMaps.GetOrAdd(entity.GetType(),
                static type => GetMappedProperties(type).ToDictionary(property => property.Name, StringComparer.Ordinal));
            foreach ((string name, object? value) in values)
                if (propertyMap.TryGetValue(name, out PropertyInfo? property))
                    property.SetValue(entity, ConvertValue(value, property.PropertyType));
        }

        public static object? ConvertValue(object? value, Type targetType)
        {
            if (value == null) return null;
            Type actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (actualTarget.IsInstanceOfType(value)) return value;
            if (value is JsonElement element) return JsonSerializer.Deserialize(element.GetRawText(), targetType);
            if (actualTarget.IsEnum)
                return value is string text ? Enum.Parse(actualTarget, text, true) : Enum.ToObject(actualTarget, value);
            return Convert.ChangeType(value, actualTarget);
        }
        #endregion

        #region Private Methods
        private static PropertyInfo[] GetMappedProperties(Type entityType) =>
            MappedProperties.GetOrAdd(entityType, static type =>
                [.. type.GetProperties().Where(property => property.CanRead && property.CanWrite &&
                    property.GetIndexParameters().Length == 0 &&
                    IsMappedProperty(property))]);

        private static bool IsMappedProperty(PropertyInfo property)
        {
            // 파생 클래스에서 제외한 속성은 변경 추적에도 포함하지 않습니다.
            if (property.GetCustomAttribute<JsonIgnoreAttribute>(inherit: true)
                is { Condition: JsonIgnoreCondition.Always })
            {
                return false;
            }

            if (property.IsDefined(typeof(EntityColumnAttribute), inherit: true) ||
                property.IsDefined(typeof(CustomEntityPropertyAttribute), inherit: true))
            {
                return true;
            }

            MethodInfo? accessor = property.GetMethod ?? property.SetMethod;
            MethodInfo? baseAccessor = accessor?.GetBaseDefinition();

            if (accessor == null || baseAccessor == null || baseAccessor == accessor)
                return false;

            PropertyInfo? baseProperty = baseAccessor.DeclaringType?.GetProperty(
                property.Name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            return baseProperty != null &&
                (baseProperty.IsDefined(typeof(EntityColumnAttribute), inherit: true) ||
                 baseProperty.IsDefined(typeof(CustomEntityPropertyAttribute), inherit: true));
        }

        private static object GetKey(IDatabaseEntity entity)
        {
            PropertyInfo key = GetMappedProperties(entity.GetType()).FirstOrDefault(property =>
                property.GetCustomAttribute<EntityColumnAttribute>(inherit: true)?.IsPrimaryKey == true)
                ?? entity.GetType().GetProperty("ID")
                ?? throw new InvalidOperationException($"{entity.GetType().Name} does not define a key.");
            return key.GetValue(entity) ?? throw new InvalidOperationException($"{entity.GetType().Name} key is null.");
        }

        internal static object? CaptureValue(object? value) => value switch
        {
            byte[] bytes => bytes.ToArray(),
            Array array => array.Clone(),
            _ => value
        };

        private static bool AreEqual(object? left, object? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            if (left is Array && right is Array)
                return StructuralComparisons.StructuralEqualityComparer.Equals(left, right);
            return left.Equals(right);
        }
        #endregion
    }
}
