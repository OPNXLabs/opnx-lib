using OnEyes.DataBase.Datas;
using OPNX.Lib.Data.ORM.Datas;

namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityMetadata
    {
        IEnumerable<PropertySchema> GetRelatedListProps();

        EntityChanges GetChangedFields<T>(T comparedEntity) where T : IEntity;

        Dictionary<FieldKey, object> GetFieldValues();
    }
}
