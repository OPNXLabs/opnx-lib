using OPNX.Lib.Data.ORM.EventHandlers;
using System.Data;

namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IDataBaseService : IDisposable
    {
        string GetTableIdentifier(Type entityType);
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

        int InsertEntity<T>(T insertEntity) where T : IEntity;
        Task<int> InsertEntityAsync<T>(T insertEntity, CancellationToken cancellationToken = default) where T : IEntity;
        int BatchInsert<T>(IReadOnlyList<T> insertEntities) where T : IEntity;
        Task<int> BatchInsertAsync<T>(IReadOnlyList<T> insertEntities, CancellationToken cancellationToken = default) where T : IEntity;
        bool DeleteEntity<T>(T deleteEntity) where T : IEntity;
        Task<bool> DeleteEntityAsync<T>(T deleteEntity, CancellationToken cancellationToken = default) where T : IEntity;
        int BatchDelete<T>(IReadOnlyList<T> deleteEntities) where T : IEntity;
        Task<int> BatchDeleteAsync<T>(IReadOnlyList<T> deleteEntities, CancellationToken cancellationToken = default) where T : IEntity;
        bool UpdateEntity<T>(T updateEntity) where T : IEntity;
        Task<bool> UpdateEntityAsync<T>(T updateEntity, CancellationToken cancellationToken = default) where T : IEntity;
        int BatchUpdate<T>(IReadOnlyList<T> updateEntities) where T : IEntity;
        Task<int> BatchUpdateAsync<T>(IReadOnlyList<T> updateEntities, CancellationToken cancellationToken = default) where T : IEntity;

        event EntityChangedEventHandler? EntityChanged;

        IEntityStore EntityStore { get; }
    }
}
