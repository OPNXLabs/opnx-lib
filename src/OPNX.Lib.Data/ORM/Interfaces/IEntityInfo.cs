namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityInfo : IEntityIdentity
    {
        string EntityTypeName { get; }

        string CustomKey { get; }
    }
}
