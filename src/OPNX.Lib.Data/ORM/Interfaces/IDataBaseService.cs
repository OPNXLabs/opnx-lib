using OPNX.Lib.Data.ORM.EventHandlers;
using OPNX.Lib.Data.ORM.Generators;
using OPNX.Lib.Data.ORM.Query;
using System.Data;
using System.Linq.Expressions;

namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IDataBaseService : IDisposable
    {
        IEntitySqlGenerator SqlGenerator { get; }
        string GetTableIdentifier(Type entityType);
        IReadOnlyList<T> Select<T>(SelectQuery<T> query) where T : IDatabaseEntity;
        Task<IReadOnlyList<T>> SelectAsync<T>(SelectQuery<T> query, CancellationToken cancellationToken = default) where T : IDatabaseEntity;
        long Count<T>(SelectQuery<T> query) where T : IDatabaseEntity;
        Task<long> CountAsync<T>(SelectQuery<T> query, CancellationToken cancellationToken = default) where T : IDatabaseEntity;
        T First<T>(SelectQuery<T> query) where T : IDatabaseEntity;
        Task<T> FirstAsync<T>(SelectQuery<T> query, CancellationToken cancellationToken = default) where T : IDatabaseEntity;
        T? FirstOrDefault<T>(SelectQuery<T> query) where T : IDatabaseEntity;
        Task<T?> FirstOrDefaultAsync<T>(SelectQuery<T> query, CancellationToken cancellationToken = default) where T : IDatabaseEntity;
        bool Any<T>(SelectQuery<T> query) where T : IDatabaseEntity;
        Task<bool> AnyAsync<T>(SelectQuery<T> query, CancellationToken cancellationToken = default) where T : IDatabaseEntity;
        int ExecuteNonQuery(string sqlQuery, List<KeyValuePair<string, object>> paramList);
        Task<int> ExecuteNonQueryAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default);
        DataTable? ExecuteReader(string sqlQuery, List<KeyValuePair<string, object>> paramList);
        Task<DataTable?> ExecuteReaderAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default);
        IReadOnlyList<T> Query<T>(string sqlQuery, List<KeyValuePair<string, object>> paramList);
        Task<IReadOnlyList<T>> QueryAsync<T>(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default);
        object? ExecuteScalar(string sqlQuery, List<KeyValuePair<string, object>> paramList);
        Task<object?> ExecuteScalarAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default);

        /// <summary>Executes database operations sequentially in a single transaction. Commits on success and rolls back and rethrows on failure. Parallel commands within the callback are not supported.</summary>
        void ExecuteInTransaction(Action<IDataBaseService> work);
        /// <summary>Executes database operations sequentially in a single transaction and returns a result. Commits on success and rolls back and rethrows on failure. Parallel commands within the callback are not supported.</summary>
        TResult ExecuteInTransaction<TResult>(Func<IDataBaseService, TResult> work);
        /// <summary>Executes database operations sequentially in a single transaction. Commits on success and rolls back and rethrows on failure. Do not run commands in parallel within the callback.</summary>
        Task ExecuteInTransactionAsync(Func<IDataBaseService, CancellationToken, Task> work, CancellationToken cancellationToken = default);
        /// <summary>Executes database operations sequentially in a single transaction and returns a result. Commits on success and rolls back and rethrows on failure. Do not run commands in parallel within the callback.</summary>
        Task<TResult> ExecuteInTransactionAsync<TResult>(Func<IDataBaseService, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default);

        TKey InsertEntity<T, TKey>(T insertEntity) where T : IEntity<TKey> where TKey : notnull;
        Task<TKey> InsertEntityAsync<T, TKey>(T insertEntity, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull;
        int BatchInsert<T, TKey>(IReadOnlyList<T> insertEntities) where T : IEntity<TKey> where TKey : notnull;
        Task<int> BatchInsertAsync<T, TKey>(IReadOnlyList<T> insertEntities, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull;
        Task<int> BulkInsertAsync<T, TKey>(IReadOnlyList<T> insertEntities, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull;
        bool DeleteEntity<T, TKey>(T deleteEntity) where T : IEntity<TKey> where TKey : notnull;
        Task<bool> DeleteEntityAsync<T, TKey>(T deleteEntity, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull;
        int BatchDelete<T, TKey>(IReadOnlyList<T> deleteEntities) where T : IEntity<TKey> where TKey : notnull;
        Task<int> BatchDeleteAsync<T, TKey>(IReadOnlyList<T> deleteEntities, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull;
        bool CascadeInsert<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children, Expression<Func<TChild, object?>> foreignKey) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull;
        bool CascadeUpdate<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children, Expression<Func<TChild, object?>> foreignKey) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull;
        bool CascadeDelete<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull;
        Task<bool> CascadeInsertAsync<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children, Expression<Func<TChild, object?>> foreignKey, CancellationToken cancellationToken = default) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull;
        Task<bool> CascadeUpdateAsync<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children, Expression<Func<TChild, object?>> foreignKey, CancellationToken cancellationToken = default) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull;
        Task<bool> CascadeDeleteAsync<TParent, TParentKey, TChild, TChildKey>(TParent parent, IReadOnlyList<TChild> children, CancellationToken cancellationToken = default) where TParent : IEntity<TParentKey> where TParentKey : notnull where TChild : IEntity<TChildKey> where TChildKey : notnull;
        bool UpdateEntity<T, TKey>(T updateEntity) where T : IEntity<TKey> where TKey : notnull;
        bool UpdateEntity<T, TKey>(T updateEntity, params Expression<Func<T, object?>>[] properties) where T : IEntity<TKey> where TKey : notnull;
        Task<bool> UpdateEntityAsync<T, TKey>(T updateEntity, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull;
        Task<bool> UpdateEntityAsync<T, TKey>(T updateEntity, CancellationToken cancellationToken = default, params Expression<Func<T, object?>>[] properties) where T : IEntity<TKey> where TKey : notnull;
        int BatchUpdate<T, TKey>(IReadOnlyList<T> updateEntities) where T : IEntity<TKey> where TKey : notnull;
        Task<int> BatchUpdateAsync<T, TKey>(IReadOnlyList<T> updateEntities, CancellationToken cancellationToken = default) where T : IEntity<TKey> where TKey : notnull;

        event EntityChangedEventHandler? EntityChanged;
        event EventHandler<EntityStoreSynchronizationFailedEventArgs>? EntityStoreSynchronizationFailed;

        IEntityStore EntityStore { get; }
    }
}
