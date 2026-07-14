namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityChangeNotifier
    {
        void NotifyInserted<T>() where T : IEntity;

        void NotifyDeleted<T>() where T : IEntity;

        T NotifyUpdated<T>(T newEntity) where T : IEntity;
    }
}
