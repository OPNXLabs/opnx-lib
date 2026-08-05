using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Enums;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Mapping;

public sealed class DataRowMapper
{
    private static readonly ConcurrentDictionary<(Type TargetType, DatabaseNamingStyle NamingStyle), IReadOnlyDictionary<string, PropertyInfo>> _propertyMaps = new();

    public T? Map<T>(DataRow row, IEntityStore? entityStore = null) => (T?)Map(typeof(T), row, entityStore);

    public object? Map(Type targetType, DataRow row, IEntityStore? entityStore = null)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(row);

        object? instance = CreateInstance(targetType, entityStore);
        if (instance == null)
            return null;

        IReadOnlyDictionary<string, PropertyInfo> properties = _propertyMaps.GetOrAdd((targetType, DatabaseNaming.Style), static key => key.TargetType.GetProperties().Where(property => property.CanWrite).ToDictionary(DatabaseNaming.GetColumnName, StringComparer.OrdinalIgnoreCase));
        foreach (DataColumn column in row.Table.Columns)
        {
            if (!properties.TryGetValue(column.ColumnName, out PropertyInfo? property))
                continue;

            property.SetValue(instance, ConvertValue(row[column], property.PropertyType));
        }

        if (instance is IEntity entity)
            entity.EntityStore = entityStore;

        return instance;
    }

    public IReadOnlyList<T> Map<T>(DataTable? table, IEntityStore? entityStore = null)
    {
        if (table == null || table.Rows.Count == 0)
            return [];

        List<T> result = new(table.Rows.Count);
        foreach (DataRow row in table.Rows)
        {
            T? item = Map<T>(row, entityStore);
            if (item != null)
                result.Add(item);
        }

        return result;
    }

    public IReadOnlyList<object> Map(Type targetType, DataTable? table, IEntityStore? entityStore = null)
    {
        if (table == null || table.Rows.Count == 0)
            return [];

        List<object> result = new(table.Rows.Count);
        foreach (DataRow row in table.Rows)
        {
            object? item = Map(targetType, row, entityStore);
            if (item != null)
                result.Add(item);
        }

        return result;
    }

    private static object? CreateInstance(Type targetType, IEntityStore? entityStore)
    {
        if (entityStore != null)
        {
            ConstructorInfo? entityStoreConstructor = targetType.GetConstructor([typeof(IEntityStore)]);
            if (entityStoreConstructor != null)
                return entityStoreConstructor.Invoke([entityStore]);
        }

        return Activator.CreateInstance(targetType);
    }

    private static object? ConvertValue(object value, Type propertyType)
    {
        if (value is DBNull)
            return null;

        Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (targetType.IsInstanceOfType(value))
            return value;

        if (targetType.IsEnum)
        {
            if (value is string enumName)
                return Enum.Parse(targetType, enumName, true);

            object enumValue = Convert.ChangeType(value, Enum.GetUnderlyingType(targetType), CultureInfo.InvariantCulture);
            return Enum.ToObject(targetType, enumValue);
        }

        if (targetType == typeof(Guid))
            return value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        if (targetType == typeof(DateTimeOffset))
            return value is DateTimeOffset dateTimeOffset ? dateTimeOffset : DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        if (targetType == typeof(TimeSpan))
            return value is TimeSpan timeSpan ? timeSpan : TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        if (targetType == typeof(DateOnly))
            return value is DateOnly dateOnly ? dateOnly : DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture));
        if (targetType == typeof(TimeOnly))
            return value is TimeOnly timeOnly ? timeOnly : TimeOnly.FromTimeSpan(value is TimeSpan sourceTimeSpan ? sourceTimeSpan : TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture));

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }
}
