using OPNX.Lib.Data.ORM.Interfaces;
using OPNX.Lib.Data.ORM.Query;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Generators;

public interface IEntitySqlGenerator
{
    CompiledDbCommand Select<T>(SelectQuery<T> query) where T : IDatabaseEntity;
    CompiledDbCommand Count<T>(SelectQuery<T> query) where T : IDatabaseEntity;
    CompiledDbCommand Exists<T>(SelectQuery<T> query) where T : IDatabaseEntity;
    CompiledDbCommand Insert<T, TKey>(T entity) where T : IEntity<TKey> where TKey : notnull;
    CompiledDbCommand Update<T, TKey>(T entity) where T : IEntity<TKey> where TKey : notnull;
    CompiledDbCommand Update<T, TKey>(T entity, IReadOnlyList<PropertyInfo> properties) where T : IEntity<TKey> where TKey : notnull;
    CompiledDbCommand Delete<T, TKey>(T entity) where T : IEntity<TKey> where TKey : notnull;
}
