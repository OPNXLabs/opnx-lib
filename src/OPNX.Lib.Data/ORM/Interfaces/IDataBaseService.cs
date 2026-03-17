using OPNX.Lib.Data.ORM.EventHandlers;
using System.Data;

namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IDataBaseService : IDisposable
    {
        int ExecuteNonQuery(string sqlQuery, List<KeyValuePair<string, object>> paramList);
        DataTable? ExecuteReader(string sqlQuery, List<KeyValuePair<string, object>> paramList);
        object? ExecuteScalar(string sqlQuery, List<KeyValuePair<string, object>> paramList);

        int InsertEntity<T>(T insertEntity) where T : IEntity;
        bool DeleteEntity<T>(T deleteEntity) where T : IEntity;
        bool UpdateEntity<T>(T updateEntity) where T : IEntity;

        event EntityChangedEventHandler? EntityChanged;

        IEntityStore EntityStore { get; }
    }
}
