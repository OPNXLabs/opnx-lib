namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface ISoftDeletableEntity
    {
        bool IsDeleted { get; set; }
    }
}
