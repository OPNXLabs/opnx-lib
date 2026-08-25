using OPNX.Lib.Data.ORM.EventHandlers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityStore : IDisposable
    {
        bool InsertEntity<T, TKey>(T insertEntity) where T : IEntity<TKey> where TKey : notnull;
        bool DeleteEntity<T, TKey>(T deleteEntity) where T : IEntity<TKey> where TKey : notnull;
        bool UpdateEntity<T, TKey>(T updateEntity) where T : IEntity<TKey> where TKey : notnull;

        ObservableCollection<T> GetEntities<T, TKey>() where T : IEntity<TKey> where TKey : notnull;

        T? FindEntity<T, TKey>(Func<T, bool> predicate) where T : IEntity<TKey> where TKey : notnull;
        T? FindEntity<T, TKey>(TKey id) where T : IEntity<TKey> where TKey : notnull;
        T? FindEntity<T, TKey>(Type entityType, TKey id) where T : IEntity<TKey> where TKey : notnull;
        IDatabaseEntity? FindEntity<TKey>(Type entityType, TKey id) where TKey : notnull;

        ObservableCollection<T> FindEntities<T, TKey>(Func<T, bool> predicate) where T : IEntity<TKey> where TKey : notnull;

        ConcurrentDictionary<Type, object> AllEntitis { get; }

        event EntityChangedEventHandler? EntityChanged;
    }
}
