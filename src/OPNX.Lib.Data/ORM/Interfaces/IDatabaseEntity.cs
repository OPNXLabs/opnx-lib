namespace OPNX.Lib.Data.ORM.Interfaces
{
    /// <summary>Marks a type that can be mapped by the database services.</summary>
    public interface IDatabaseEntity
    {
    }

    /// <summary>Defines a database entity with a single strongly typed key.</summary>
    public interface IEntity<TKey> : IDatabaseEntity where TKey : notnull
    {
        TKey ID { get; set; }
    }

    /// <summary>Defines a query-only model without a primary key.</summary>
    public interface IKeylessEntity : IDatabaseEntity
    {
    }
}
