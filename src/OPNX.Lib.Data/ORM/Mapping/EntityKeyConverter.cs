namespace OPNX.Lib.Data.ORM.Mapping;

internal static class EntityKeyConverter
{
    public static TKey ConvertTo<TKey>(object? value) where TKey : notnull => (TKey)ConvertTo(value, typeof(TKey));

    public static object ConvertTo(object? value, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        Type actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value == null || value == DBNull.Value)
            throw new InvalidOperationException($"A null database key cannot be assigned to {targetType.Name}.");
        if (actualType.IsInstanceOfType(value))
            return value;
        if (actualType == typeof(Guid))
            return value is string text && Guid.TryParse(text, out Guid guid) ? guid : throw new InvalidCastException($"{value} cannot be converted to Guid.");
        if (actualType == typeof(string))
            return value.ToString() ?? string.Empty;
        try { return Convert.ChangeType(value, actualType); }
        catch (Exception ex) { throw new InvalidCastException($"Database key value of type {value.GetType().Name} cannot be converted to {targetType.Name}.", ex); }
    }

    public static void SetForeignKey(object entity, System.Reflection.PropertyInfo property, object key)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);
        if (!property.CanWrite || property.GetIndexParameters().Length != 0)
            throw new InvalidOperationException($"{property.DeclaringType?.Name}.{property.Name} is not a writable foreign key property.");
        property.SetValue(entity, ConvertForeignKey(key, property.PropertyType));
    }

    private static object ConvertForeignKey(object key, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(key);
        Type sourceType = key.GetType();
        Type actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (actualTarget == sourceType)
            return key;
        if (IsSafeNumericWidening(sourceType, actualTarget))
            return Convert.ChangeType(key, actualTarget);
        throw new InvalidCastException($"Cascade key type mismatch: {sourceType.Name} cannot be assigned to {targetType.Name}. Only identical types, nullable equivalents, and safe numeric widening are allowed.");
    }

    private static bool IsSafeNumericWidening(Type source, Type target) => source == typeof(byte) && target is not null && (target == typeof(short) || target == typeof(int) || target == typeof(long)) || source == typeof(short) && (target == typeof(int) || target == typeof(long)) || source == typeof(int) && target == typeof(long);
}
