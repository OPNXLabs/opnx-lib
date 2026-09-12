using OPNX.Lib.Data.ORM.EventHandlers;
using System.Reflection;
using OnEyes.DataBase.Datas;
using OPNX.Lib.Data.ORM.Datas;

namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityMetadata
    {
        IEnumerable<PropertySchema> GetRelatedListProps();

        bool IsRelatedChange(EntityChangedEventArgs change,
            Func<Entity, PropertyInfo, bool>? shouldTraverse = null);

        EntityChanges GetChangedFields<T>(T comparedEntity) where T : IEntity;

        Dictionary<FieldKey, object> GetFieldValues();
    }
}
