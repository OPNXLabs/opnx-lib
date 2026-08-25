using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Linq.Expressions;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Query;

public sealed class SelectQuery<T> where T : IDatabaseEntity
{
    internal List<QueryNode> Conditions { get; } = [];
    internal List<QueryOrder> Orders { get; } = [];
    internal int? LimitValue { get; private set; }
    internal int? OffsetValue { get; private set; }

    public static SelectQuery<T> Create() => new();

    public SelectQuery<T> Where<TValue>(Expression<Func<T, TValue>> property, QueryOperator queryOperator, TValue value)
    {
        if (queryOperator is QueryOperator.IsNull or QueryOperator.IsNotNull or QueryOperator.In or QueryOperator.NotIn or QueryOperator.Between or QueryOperator.NotBetween)
            throw new ArgumentException($"{queryOperator} requires a dedicated method.", nameof(queryOperator));
        Conditions.Add(new QueryCondition(QueryLogicalOperator.And, GetProperty(property), queryOperator, value));
        return this;
    }

    public SelectQuery<T> OrWhere<TValue>(Expression<Func<T, TValue>> property, QueryOperator queryOperator, TValue value)
    {
        if (queryOperator is QueryOperator.IsNull or QueryOperator.IsNotNull or QueryOperator.In or QueryOperator.NotIn or QueryOperator.Between or QueryOperator.NotBetween)
            throw new ArgumentException($"{queryOperator} requires a dedicated method.", nameof(queryOperator));
        Conditions.Add(new QueryCondition(QueryLogicalOperator.Or, GetProperty(property), queryOperator, value));
        return this;
    }

    public SelectQuery<T> WhereIn<TValue>(Expression<Func<T, TValue>> property, IEnumerable<TValue> values) => AddCollectionCondition(property, values, QueryOperator.In);
    public SelectQuery<T> WhereNotIn<TValue>(Expression<Func<T, TValue>> property, IEnumerable<TValue> values) => AddCollectionCondition(property, values, QueryOperator.NotIn);
    public SelectQuery<T> OrWhereIn<TValue>(Expression<Func<T, TValue>> property, IEnumerable<TValue> values) => AddCollectionCondition(property, values, QueryOperator.In, QueryLogicalOperator.Or);
    public SelectQuery<T> OrWhereNotIn<TValue>(Expression<Func<T, TValue>> property, IEnumerable<TValue> values) => AddCollectionCondition(property, values, QueryOperator.NotIn, QueryLogicalOperator.Or);

    public SelectQuery<T> WhereNull<TValue>(Expression<Func<T, TValue>> property)
    {
        Conditions.Add(new QueryCondition(QueryLogicalOperator.And, GetProperty(property), QueryOperator.IsNull, null));
        return this;
    }

    public SelectQuery<T> WhereNotNull<TValue>(Expression<Func<T, TValue>> property)
    {
        Conditions.Add(new QueryCondition(QueryLogicalOperator.And, GetProperty(property), QueryOperator.IsNotNull, null));
        return this;
    }

    public SelectQuery<T> OrWhereNull<TValue>(Expression<Func<T, TValue>> property) { Conditions.Add(new QueryCondition(QueryLogicalOperator.Or, GetProperty(property), QueryOperator.IsNull, null)); return this; }
    public SelectQuery<T> OrWhereNotNull<TValue>(Expression<Func<T, TValue>> property) { Conditions.Add(new QueryCondition(QueryLogicalOperator.Or, GetProperty(property), QueryOperator.IsNotNull, null)); return this; }

    public SelectQuery<T> WhereBetween<TValue>(Expression<Func<T, TValue>> property, TValue minimum, TValue maximum) { Conditions.Add(new QueryCondition(QueryLogicalOperator.And, GetProperty(property), QueryOperator.Between, new object?[] { minimum, maximum })); return this; }
    public SelectQuery<T> OrWhereBetween<TValue>(Expression<Func<T, TValue>> property, TValue minimum, TValue maximum) { Conditions.Add(new QueryCondition(QueryLogicalOperator.Or, GetProperty(property), QueryOperator.Between, new object?[] { minimum, maximum })); return this; }
    public SelectQuery<T> WhereNotBetween<TValue>(Expression<Func<T, TValue>> property, TValue minimum, TValue maximum) { Conditions.Add(new QueryCondition(QueryLogicalOperator.And, GetProperty(property), QueryOperator.NotBetween, new object?[] { minimum, maximum })); return this; }
    public SelectQuery<T> OrWhereNotBetween<TValue>(Expression<Func<T, TValue>> property, TValue minimum, TValue maximum) { Conditions.Add(new QueryCondition(QueryLogicalOperator.Or, GetProperty(property), QueryOperator.NotBetween, new object?[] { minimum, maximum })); return this; }
    public SelectQuery<T> WhereGroup(Action<QueryGroup<T>> configure) => AddGroup(QueryLogicalOperator.And, configure);
    public SelectQuery<T> OrWhereGroup(Action<QueryGroup<T>> configure) => AddGroup(QueryLogicalOperator.Or, configure);

    public SelectQuery<T> OrderBy<TValue>(Expression<Func<T, TValue>> property, QuerySortDirection direction = QuerySortDirection.Ascending)
    {
        Orders.Add(new QueryOrder(GetProperty(property), direction));
        return this;
    }

    public SelectQuery<T> OrderByDescending<TValue>(Expression<Func<T, TValue>> property) => OrderBy(property, QuerySortDirection.Descending);

    public SelectQuery<T> Page(int pageNumber, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        LimitValue = pageSize;
        OffsetValue = checked((pageNumber - 1) * pageSize);
        return this;
    }

    public SelectQuery<T> Limit(int limit, int offset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        LimitValue = limit;
        OffsetValue = offset;
        return this;
    }

    internal SelectQuery<T> CopyWithLimit(int limit)
    {
        SelectQuery<T> copy = new() { LimitValue = limit, OffsetValue = OffsetValue };
        copy.Conditions.AddRange(Conditions);
        copy.Orders.AddRange(Orders);
        return copy;
    }

    private SelectQuery<T> AddCollectionCondition<TValue>(Expression<Func<T, TValue>> property, IEnumerable<TValue> values, QueryOperator queryOperator, QueryLogicalOperator logicalOperator = QueryLogicalOperator.And)
    {
        ArgumentNullException.ThrowIfNull(values);
        object?[] items = [.. values.Cast<object?>()];
        Conditions.Add(new QueryCondition(logicalOperator, GetProperty(property), queryOperator, items));
        return this;
    }

    private SelectQuery<T> AddGroup(QueryLogicalOperator logicalOperator, Action<QueryGroup<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        QueryGroup<T> group = new();
        configure(group);
        if (group.Conditions.Count > 0)
            Conditions.Add(new QueryConditionGroup(logicalOperator, group.Conditions));
        return this;
    }

    private static PropertyInfo GetProperty<TValue>(Expression<Func<T, TValue>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Body is MemberExpression { Member: PropertyInfo property } && property.DeclaringType?.IsAssignableFrom(typeof(T)) == true && property.CanRead && property.IsDefined(typeof(EntityColumnAttribute), true))
            return property;
        throw new ArgumentException("The expression must select a directly mapped entity property.", nameof(expression));
    }
}

internal abstract record QueryNode(QueryLogicalOperator LogicalOperator);
internal sealed record QueryCondition(QueryLogicalOperator LogicalOperator, PropertyInfo Property, QueryOperator Operator, object? Value) : QueryNode(LogicalOperator);
internal sealed record QueryConditionGroup(QueryLogicalOperator LogicalOperator, IReadOnlyList<QueryNode> Conditions) : QueryNode(LogicalOperator);
internal sealed record QueryOrder(PropertyInfo Property, QuerySortDirection Direction);
