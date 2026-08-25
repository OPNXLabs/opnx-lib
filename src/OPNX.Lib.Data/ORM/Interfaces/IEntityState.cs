namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityState : IAuditableEntity, ISoftDeletableEntity
    {
        bool IsLogTable { get; }

        bool IsClone { get; set; }

        bool IsSelected { get; set; }

    }
}
