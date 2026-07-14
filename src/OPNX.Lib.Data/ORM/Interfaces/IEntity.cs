namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntity :
        IEntityInfo,
        IEntityState,
        IEntityChangeNotifier,
        IEntityMetadata,
        IEntityCopyable,
        IDisposable
    {
        IEntityStore? EntityStore { get; set; }

        void Refresh();

        void Refresh(string propertyName);

        void Initialize(IEntityStore entityStore);
    }
}
