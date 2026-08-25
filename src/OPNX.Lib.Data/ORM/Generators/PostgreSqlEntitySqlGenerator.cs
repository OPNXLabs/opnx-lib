using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Datas.Attributes;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Generators;

public sealed class PostgreSqlEntitySqlGenerator : EntitySqlGenerator
{
    protected override void ValidateIdentityKey(PropertyInfo keyProperty, EntityColumnAttribute attribute)
    {
        if (attribute.IsIdentity && keyProperty.PropertyType != typeof(int) && keyProperty.PropertyType != typeof(long) && keyProperty.PropertyType != typeof(Guid))
            throw new InvalidOperationException($"PostgreSQL generated keys must be Int32, Int64, or Guid; {keyProperty.DeclaringType?.Name}.{keyProperty.Name} is {keyProperty.PropertyType.Name}.");
    }

    protected override string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
    protected override string GetTableIdentifier(Type entityType) => QuoteIdentifier(DatabaseNaming.GetTableName(entityType).ToLowerInvariant());
    protected override string EmptyInsert(string table, string idColumn) => $"INSERT INTO {table} DEFAULT VALUES RETURNING {idColumn};";
    protected override string InsertCommand<T>(string table, string columns, string values, string idColumn, PropertyInfo idProperty, EntityColumnAttribute idAttribute, T entity, List<KeyValuePair<string, object>> parameters) => $"INSERT INTO {table}({columns}) VALUES({values}) RETURNING {idColumn};";
}
