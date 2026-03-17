using OPNX.Lib.Data.ORM.EventHandlers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityStore : IDisposable
    {
        int InsertEntity<T>(T? insertEntity) where T : IEntity;
        bool DeleteEntity<T>(T? deleteEntity) where T : IEntity;
        bool UpdateEntity<T>(T? updateEntity) where T : IEntity;

        ObservableCollection<T> GetEntities<T>() where T : IEntity;

        T? FindEntity<T>(Func<T, bool> predicate) where T : IEntity;
        T? FindEntity<T>(int id) where T : IEntity;
        T? FindEntity<T>(Type entityType, int id) where T : IEntity;
        IEntity? FindEntity(Type entityType, int id);

        ObservableCollection<T> FindEntities<T>(Func<T, bool> predicate) where T : IEntity;

        ConcurrentDictionary<Type, object> AllEntitis { get; }

        event EntityChangedEventHandler? EntityChanged;
    }
}
