using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.Interfaces;
using OPNX.Lib.Data.ORM.Query;
using System.Collections;
using System.Data;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Generators;

public abstract class EntitySqlGenerator : IEntitySqlGenerator
{
    public CompiledDbCommand Select<T>(SelectQuery<T> query) where T : IDatabaseEntity
    {
        ArgumentNullException.ThrowIfNull(query);
        List<KeyValuePair<string, object>> parameters = [];
        PropertyInfo[] mappedProperties = [.. GetMappedProperties(typeof(T))];
        if (mappedProperties.Length == 0)
            throw new InvalidOperationException($"{typeof(T).Name} does not define any mapped columns.");
        string columns = string.Join(",", mappedProperties.Select(property => QuoteIdentifier(DatabaseNaming.GetColumnName(property))));
        string sql = $"SELECT {columns} FROM {GetTableIdentifier(typeof(T))}{BuildWhere(query.Conditions, parameters)}{BuildOrder(query.Orders)}{PaginationCommand(query.LimitValue, query.OffsetValue, query.Orders.Count > 0, parameters)};";
        return new CompiledDbCommand(sql, parameters);
    }

    public CompiledDbCommand Count<T>(SelectQuery<T> query) where T : IDatabaseEntity
    {
        ArgumentNullException.ThrowIfNull(query);
        List<KeyValuePair<string, object>> parameters = [];
        return new CompiledDbCommand($"SELECT COUNT(*) FROM {GetTableIdentifier(typeof(T))}{BuildWhere(query.Conditions, parameters)};", parameters);
    }

    public CompiledDbCommand Exists<T>(SelectQuery<T> query) where T : IDatabaseEntity
    {
        ArgumentNullException.ThrowIfNull(query);
        List<KeyValuePair<string, object>> parameters = [];
        return new CompiledDbCommand($"SELECT EXISTS(SELECT 1 FROM {GetTableIdentifier(typeof(T))}{BuildWhere(query.Conditions, parameters)});", parameters);
    }

    public CompiledDbCommand Insert<T, TKey>(T entity) where T : IEntity<TKey> where TKey : notnull { ValidateKeyedEntity<T, TKey>(); return InsertCore(entity); }

    private CompiledDbCommand InsertCore<T>(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        List<KeyValuePair<string, object>> parameters = [];
        Type entityType = typeof(T);
        PropertyInfo idProperty = GetIdProperty(entityType);
        EntityColumnAttribute idAttribute = GetColumnAttribute(idProperty);
        string table = GetTableIdentifier(entityType);
        string idColumn = QuoteIdentifier(DatabaseNaming.GetColumnName(idProperty));
        List<PropertyInfo> properties = [.. GetMappedProperties(entityType).Where(property => !GetColumnAttribute(property).IsIdentity && !GetColumnAttribute(property).IsReadOnly)];
        if (properties.Count == 0)
            return new CompiledDbCommand(EmptyInsert(table, idColumn), parameters);
        foreach (PropertyInfo property in properties)
            parameters.Add(new($"@{property.Name}", NormalizeValue(property, property.GetValue(entity))));
        string columns = string.Join(",", properties.Select(property => QuoteIdentifier(DatabaseNaming.GetColumnName(property))));
        string values = string.Join(",", properties.Select(property => $"@{property.Name}"));
        return new CompiledDbCommand(InsertCommand(table, columns, values, idColumn, idProperty, idAttribute, entity, parameters), parameters);
    }

    public CompiledDbCommand Update<T, TKey>(T entity) where T : IEntity<TKey> where TKey : notnull { ValidateKeyedEntity<T, TKey>(); return UpdateCore(entity, [.. GetMappedProperties(typeof(T)).Where(property => IsUpdateProperty(property))]); }

    public CompiledDbCommand Update<T, TKey>(T entity, IReadOnlyList<PropertyInfo> properties) where T : IEntity<TKey> where TKey : notnull { ValidateKeyedEntity<T, TKey>(); return UpdateCore(entity, properties); }

    private CompiledDbCommand UpdateCore<T>(T entity, IReadOnlyList<PropertyInfo> properties)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(properties);
        List<KeyValuePair<string, object>> parameters = [];
        Type entityType = typeof(T);
        PropertyInfo idProperty = GetIdProperty(entityType);
        string table = GetTableIdentifier(entityType);
        string idColumn = QuoteIdentifier(DatabaseNaming.GetColumnName(idProperty));
        List<PropertyInfo> updateProperties = [.. properties.Select(property => ValidateUpdateProperty(entityType, property)).Distinct()];
        PropertyInfo? updateTimeProperty = GetMappedProperties(entityType).FirstOrDefault(property => string.Equals(property.Name, nameof(IAuditableEntity.UpdateTime), StringComparison.OrdinalIgnoreCase));
        if (entity is IAuditableEntity { IsAuditable: true } && updateTimeProperty != null && !updateProperties.Contains(updateTimeProperty))
            updateProperties.Add(updateTimeProperty);
        parameters.Add(new("@ID", idProperty.GetValue(entity) ?? DBNull.Value));
        foreach (PropertyInfo property in updateProperties)
            parameters.Add(new($"@{property.Name}", NormalizeValue(property, property.GetValue(entity))));
        string assignments = updateProperties.Count == 0 ? $"{idColumn}={idColumn}" : string.Join(",", updateProperties.Select(property => $"{QuoteIdentifier(DatabaseNaming.GetColumnName(property))}=@{property.Name}"));
        return new CompiledDbCommand(UpdateCommand(table, assignments, idColumn), parameters);
    }

    public CompiledDbCommand Delete<T, TKey>(T entity) where T : IEntity<TKey> where TKey : notnull { ValidateKeyedEntity<T, TKey>(); return DeleteCore(entity); }

    private CompiledDbCommand DeleteCore<T>(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        PropertyInfo idProperty = GetIdProperty(typeof(T));
        string idColumn = QuoteIdentifier(DatabaseNaming.GetColumnName(idProperty));
        List<KeyValuePair<string, object>> parameters = [new("@ID", idProperty.GetValue(entity) ?? DBNull.Value)];
        return new CompiledDbCommand(DeleteCommand(GetTableIdentifier(typeof(T)), idColumn), parameters);
    }

    protected abstract string QuoteIdentifier(string identifier);
    protected abstract string GetTableIdentifier(Type entityType);
    protected abstract string EmptyInsert(string table, string idColumn);
    protected abstract string InsertCommand<T>(string table, string columns, string values, string idColumn, PropertyInfo idProperty, EntityColumnAttribute idAttribute, T entity, List<KeyValuePair<string, object>> parameters);
    protected virtual string UpdateCommand(string table, string assignments, string idColumn) => $"UPDATE {table} SET {assignments} WHERE {idColumn}=@ID;";
    protected virtual string DeleteCommand(string table, string idColumn) => $"DELETE FROM {table} WHERE {idColumn}=@ID;";
    protected virtual string PaginationCommand(int? limit, int? offset, bool hasOrder, List<KeyValuePair<string, object>> parameters)
    {
        if (limit == null)
            return string.Empty;
        parameters.Add(new("@Limit", limit.Value));
        parameters.Add(new("@Offset", offset ?? 0));
        return " LIMIT @Limit OFFSET @Offset";
    }

    private string BuildWhere(IReadOnlyList<QueryNode> conditions, List<KeyValuePair<string, object>> parameters)
    {
        string predicates = BuildPredicates(conditions, parameters);
        return string.IsNullOrEmpty(predicates) ? string.Empty : $" WHERE {predicates}";
    }

    private string BuildPredicates(IReadOnlyList<QueryNode> nodes, List<KeyValuePair<string, object>> parameters)
    {
        List<string> predicates = [];
        foreach (QueryNode node in nodes)
        {
            string predicate = node switch { QueryCondition condition => BuildCondition(condition, parameters), QueryConditionGroup group => BuildGroup(group, parameters), _ => string.Empty };
            if (string.IsNullOrEmpty(predicate))
                continue;
            string logicalOperator = predicates.Count == 0 ? string.Empty : node.LogicalOperator == QueryLogicalOperator.Or ? " OR " : " AND ";
            predicates.Add($"{logicalOperator}{predicate}");
        }
        return string.Concat(predicates);
    }

    private string BuildGroup(QueryConditionGroup group, List<KeyValuePair<string, object>> parameters)
    {
        string predicates = BuildPredicates(group.Conditions, parameters);
        return string.IsNullOrEmpty(predicates) ? string.Empty : $"({predicates})";
    }

    private string BuildCondition(QueryCondition condition, List<KeyValuePair<string, object>> parameters)
    {
        string column = QuoteIdentifier(DatabaseNaming.GetColumnName(condition.Property));
        if (condition.Operator == QueryOperator.IsNull || condition.Value == null && condition.Operator == QueryOperator.Equal)
            return $"{column} IS NULL";
        if (condition.Operator == QueryOperator.IsNotNull || condition.Value == null && condition.Operator == QueryOperator.NotEqual)
            return $"{column} IS NOT NULL";
        if (condition.Operator is QueryOperator.In or QueryOperator.NotIn)
        {
            IEnumerable values = condition.Value as IEnumerable ?? throw new InvalidOperationException($"{condition.Operator} requires a value collection.");
            List<string> names = [];
            foreach (object? value in values) { string name = $"@p{parameters.Count}"; names.Add(name); parameters.Add(new(name, NormalizeQueryValue(value))); }
            return names.Count == 0 ? condition.Operator == QueryOperator.In ? "1=0" : "1=1" : $"{column} {(condition.Operator == QueryOperator.In ? "IN" : "NOT IN")} ({string.Join(",", names)})";
        }
        if (condition.Operator is QueryOperator.Between or QueryOperator.NotBetween)
        {
            object?[] range = condition.Value as object?[] ?? throw new InvalidOperationException($"{condition.Operator} requires two values.");
            string minimumName = $"@p{parameters.Count}";
            parameters.Add(new(minimumName, NormalizeQueryValue(range[0])));
            string maximumName = $"@p{parameters.Count}";
            parameters.Add(new(maximumName, NormalizeQueryValue(range[1])));
            return $"{column} {(condition.Operator == QueryOperator.Between ? "BETWEEN" : "NOT BETWEEN")} {minimumName} AND {maximumName}";
        }
        string parameterName = $"@p{parameters.Count}";
        parameters.Add(new(parameterName, NormalizeQueryValue(GetComparisonValue(condition.Operator, condition.Value))));
        return $"{column} {GetOperator(condition.Operator)} {parameterName}";
    }

    private string BuildOrder(IReadOnlyList<QueryOrder> orders) => orders.Count == 0 ? string.Empty : $" ORDER BY {string.Join(",", orders.Select(order => $"{QuoteIdentifier(DatabaseNaming.GetColumnName(order.Property))} {(order.Direction == QuerySortDirection.Descending ? "DESC" : "ASC")}"))}";
    private static IEnumerable<PropertyInfo> GetMappedProperties(Type entityType) => entityType.GetProperties().Where(property => property.CanRead && property.IsDefined(typeof(EntityColumnAttribute), true)).OrderBy(property => GetColumnAttribute(property).ColIndex);
    private static bool IsUpdateProperty(PropertyInfo property) => !GetColumnAttribute(property).IsPrimaryKey && !GetColumnAttribute(property).IsIdentity && !GetColumnAttribute(property).IsReadOnly && !string.Equals(property.Name, nameof(IAuditableEntity.InsertTime), StringComparison.OrdinalIgnoreCase);
    private static PropertyInfo ValidateUpdateProperty(Type entityType, PropertyInfo property)
    {
        if (property.DeclaringType?.IsAssignableFrom(entityType) != true || !property.CanRead || !property.IsDefined(typeof(EntityColumnAttribute), true) || !IsUpdateProperty(property))
            throw new ArgumentException($"{property.Name} is not an updatable mapped property of {entityType.Name}.", nameof(property));
        return property;
    }
    private static PropertyInfo GetIdProperty(Type entityType)
    {
        PropertyInfo[] keys = [.. GetMappedProperties(entityType).Where(property => GetColumnAttribute(property).IsPrimaryKey)];
        return keys.Length == 1 ? keys[0] : throw new InvalidOperationException($"{entityType.FullName} must define exactly one mapped primary key; found {keys.Length}.");
    }

    private void ValidateKeyedEntity<T, TKey>() where T : IEntity<TKey> where TKey : notnull
    {
        if (typeof(T).IsValueType)
            throw new InvalidOperationException($"{typeof(T).Name} must be a reference type. Struct entities are not supported.");
        PropertyInfo keyProperty = GetIdProperty(typeof(T));
        if (keyProperty.PropertyType != typeof(TKey))
            throw new InvalidOperationException($"{typeof(T).Name}.{keyProperty.Name} is {keyProperty.PropertyType.Name}, but IEntity key type is {typeof(TKey).Name}.");
        ValidateIdentityKey(keyProperty, GetColumnAttribute(keyProperty));
    }

    protected virtual void ValidateIdentityKey(PropertyInfo keyProperty, EntityColumnAttribute attribute) { }
    private static EntityColumnAttribute GetColumnAttribute(PropertyInfo property) => property.GetCustomAttributes(typeof(EntityColumnAttribute), true).Cast<EntityColumnAttribute>().First();
    private static string GetOperator(QueryOperator queryOperator) => queryOperator switch { QueryOperator.Equal => "=", QueryOperator.NotEqual => "<>", QueryOperator.GreaterThan => ">", QueryOperator.GreaterThanOrEqual => ">=", QueryOperator.LessThan => "<", QueryOperator.LessThanOrEqual => "<=", QueryOperator.Like or QueryOperator.Contains or QueryOperator.StartsWith or QueryOperator.EndsWith => "LIKE", QueryOperator.NotLike => "NOT LIKE", _ => throw new NotSupportedException($"{queryOperator} is not a scalar comparison operator.") };
    private static object? GetComparisonValue(QueryOperator queryOperator, object? value) => queryOperator switch { QueryOperator.Contains => $"%{value}%", QueryOperator.StartsWith => $"{value}%", QueryOperator.EndsWith => $"%{value}", _ => value };
    private static object NormalizeQueryValue(object? value) => value == null ? DBNull.Value : value.GetType().IsEnum ? Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType())) : value;

    private static object NormalizeValue(PropertyInfo property, object? value)
    {
        EntityColumnAttribute attribute = GetColumnAttribute(property);
        if (attribute.ForeignType != null && value is int foreignKey && foreignKey <= 0 || value == null || value is string text && string.IsNullOrEmpty(text) || value is int number && number < 0 || value is DateTime dateTime && dateTime <= DateTime.MinValue || value is Guid guid && guid == Guid.Empty)
            return DBNull.Value;
        if (!value.GetType().IsEnum)
            return value;
        return attribute.SqlDataType switch { SqlDbType.TinyInt or SqlDbType.SmallInt => Convert.ToInt16(value), SqlDbType.Int => Convert.ToInt32(value), SqlDbType.BigInt => Convert.ToInt64(value), _ => Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType())) };
    }
}
