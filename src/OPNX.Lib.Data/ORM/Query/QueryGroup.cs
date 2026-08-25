using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Linq.Expressions;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Query;

public sealed class QueryGroup<T> where T : IDatabaseEntity
{
    internal List<QueryNode> Conditions { get; } = [];
    internal QueryGroup() { }

    public QueryGroup<T> Where<TValue>(Expression<Func<T, TValue>> property, QueryOperator queryOperator, TValue value) => Add(QueryLogicalOperator.And, property, queryOperator, value);
    public QueryGroup<T> OrWhere<TValue>(Expression<Func<T, TValue>> property, QueryOperator queryOperator, TValue value) => Add(QueryLogicalOperator.Or, property, queryOperator, value);
    public QueryGroup<T> WhereBetween<TValue>(Expression<Func<T, TValue>> property, TValue minimum, TValue maximum) => Add(QueryLogicalOperator.And, property, QueryOperator.Between, new object?[] { minimum, maximum });
    public QueryGroup<T> OrWhereBetween<TValue>(Expression<Func<T, TValue>> property, TValue minimum, TValue maximum) => Add(QueryLogicalOperator.Or, property, QueryOperator.Between, new object?[] { minimum, maximum });
    public QueryGroup<T> WhereNotBetween<TValue>(Expression<Func<T, TValue>> property, TValue minimum, TValue maximum) => Add(QueryLogicalOperator.And, property, QueryOperator.NotBetween, new object?[] { minimum, maximum });
    public QueryGroup<T> OrWhereNotBetween<TValue>(Expression<Func<T, TValue>> property, TValue minimum, TValue maximum) => Add(QueryLogicalOperator.Or, property, QueryOperator.NotBetween, new object?[] { minimum, maximum });
    public QueryGroup<T> WhereIn<TValue>(Expression<Func<T, TValue>> property, IEnumerable<TValue> values) => AddCollection(QueryLogicalOperator.And, property, QueryOperator.In, values);
    public QueryGroup<T> OrWhereIn<TValue>(Expression<Func<T, TValue>> property, IEnumerable<TValue> values) => AddCollection(QueryLogicalOperator.Or, property, QueryOperator.In, values);
    public QueryGroup<T> WhereNotIn<TValue>(Expression<Func<T, TValue>> property, IEnumerable<TValue> values) => AddCollection(QueryLogicalOperator.And, property, QueryOperator.NotIn, values);
    public QueryGroup<T> OrWhereNotIn<TValue>(Expression<Func<T, TValue>> property, IEnumerable<TValue> values) => AddCollection(QueryLogicalOperator.Or, property, QueryOperator.NotIn, values);
    public QueryGroup<T> WhereNull<TValue>(Expression<Func<T, TValue>> property) => Add(QueryLogicalOperator.And, property, QueryOperator.IsNull, null);
    public QueryGroup<T> OrWhereNull<TValue>(Expression<Func<T, TValue>> property) => Add(QueryLogicalOperator.Or, property, QueryOperator.IsNull, null);
    public QueryGroup<T> WhereNotNull<TValue>(Expression<Func<T, TValue>> property) => Add(QueryLogicalOperator.And, property, QueryOperator.IsNotNull, null);
    public QueryGroup<T> OrWhereNotNull<TValue>(Expression<Func<T, TValue>> property) => Add(QueryLogicalOperator.Or, property, QueryOperator.IsNotNull, null);
    public QueryGroup<T> WhereGroup(Action<QueryGroup<T>> configure) => AddGroup(QueryLogicalOperator.And, configure);
    public QueryGroup<T> OrWhereGroup(Action<QueryGroup<T>> configure) => AddGroup(QueryLogicalOperator.Or, configure);

    private QueryGroup<T> Add<TValue>(QueryLogicalOperator logicalOperator, Expression<Func<T, TValue>> expression, QueryOperator queryOperator, object? value)
    {
        bool requiresDedicatedMethod = queryOperator is QueryOperator.In or QueryOperator.NotIn or QueryOperator.IsNull or QueryOperator.IsNotNull or QueryOperator.Between or QueryOperator.NotBetween;
        bool dedicatedValue = value is object?[] || value == null && queryOperator is QueryOperator.IsNull or QueryOperator.IsNotNull;
        if (requiresDedicatedMethod && !dedicatedValue)
            throw new ArgumentException($"{queryOperator} requires a dedicated method.", nameof(queryOperator));
        Conditions.Add(new QueryCondition(logicalOperator, GetProperty(expression), queryOperator, value));
        return this;
    }

    private QueryGroup<T> AddCollection<TValue>(QueryLogicalOperator logicalOperator, Expression<Func<T, TValue>> property, QueryOperator queryOperator, IEnumerable<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        object?[] items = [.. values.Cast<object?>()];
        Conditions.Add(new QueryCondition(logicalOperator, GetProperty(property), queryOperator, items));
        return this;
    }

    private QueryGroup<T> AddGroup(QueryLogicalOperator logicalOperator, Action<QueryGroup<T>> configure)
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
        Expression body = expression.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary ? unary.Operand : expression.Body;
        if (body is MemberExpression { Member: PropertyInfo property } && property.DeclaringType?.IsAssignableFrom(typeof(T)) == true && property.CanRead && property.IsDefined(typeof(EntityColumnAttribute), true))
            return property;
        throw new ArgumentException("The expression must select a directly mapped entity property.", nameof(expression));
    }
}
