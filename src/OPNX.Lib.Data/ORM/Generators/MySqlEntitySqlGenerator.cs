using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Datas.Attributes;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Generators;

public sealed class MySqlEntitySqlGenerator : EntitySqlGenerator
{
    protected override void ValidateIdentityKey(PropertyInfo keyProperty, EntityColumnAttribute attribute)
    {
        if (attribute.IsIdentity && keyProperty.PropertyType != typeof(int) && keyProperty.PropertyType != typeof(long))
            throw new InvalidOperationException($"MySQL identity keys must be Int32 or Int64; {keyProperty.DeclaringType?.Name}.{keyProperty.Name} is {keyProperty.PropertyType.Name}.");
    }

    protected override string QuoteIdentifier(string identifier) => $"`{identifier.Replace("`", "``")}`";
    protected override string GetTableIdentifier(Type entityType) => QuoteIdentifier(DatabaseNaming.GetTableName(entityType));
    protected override string EmptyInsert(string table, string idColumn) => $"INSERT INTO {table}() VALUES(); SELECT LAST_INSERT_ID();";
    protected override string InsertCommand<T>(string table, string columns, string values, string idColumn, PropertyInfo idProperty, EntityColumnAttribute idAttribute, T entity, List<KeyValuePair<string, object>> parameters)
    {
        if (idAttribute.IsIdentity)
            return $"INSERT INTO {table}({columns}) VALUES({values}); SELECT LAST_INSERT_ID();";
        if (!parameters.Any(parameter => parameter.Key == $"@{idProperty.Name}"))
            parameters.Add(new($"@{idProperty.Name}", idProperty.GetValue(entity) ?? DBNull.Value));
        return $"INSERT INTO {table}({columns}) VALUES({values}); SELECT @{idProperty.Name};";
    }
}
