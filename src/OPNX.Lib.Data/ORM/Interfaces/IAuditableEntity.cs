namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IAuditableEntity
    {
        bool IsAuditable { get; }
        DateTime InsertTime { get; set; }
        DateTime UpdateTime { get; set; }
    }
}
