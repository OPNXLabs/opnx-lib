namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityState
    {
        bool IsAuditable { get; }

        bool IsDeleted { get; set; }

        bool IsLogTable { get; }

        bool IsClone { get; set; }

        bool IsSelected { get; set; }

        DateTime InsertTime { get; set; }

        DateTime UpdateTime { get; set; }

    }
}
