namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityCopyable
    {
        IEntity? Clone();

        T? Clone<T>() where T : IEntity;

        IEntity? Copy();

        T? Copy<T>() where T : IEntity;
    }
}
