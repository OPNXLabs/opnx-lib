using OnEyes.DataBase.Datas;
using OPNX.Lib.Data.ORM.Datas;
using System.ComponentModel;

namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntity : IDisposable, INotifyPropertyChanged
    {
        IEntityStore? EntityStore { get; set; }

        int ID { get; set; }
        bool IsDeleted { get; set; }
        bool IsLogTable { get; }
        bool IsClone { get; set; }
        bool IsSelected { get; set; }
        DateTime InsertTime { get; set; }
        DateTime UpdateTime { get; set; }

        string EntityTypeName { get; }

        string? DisplayText { get; }

        void Insert<T>() where T : IEntity;
        void Delete<T>() where T : IEntity;
        T Update<T>(T newEntity) where T : IEntity;
        void Refresh();
        void Refresh(string propertyName);

        string CustomKey { get; }

        IEnumerable<PropertySchema> GetRelatedListProps();

        EntityChanges GetChangedFields<T>(T comparedEntity) where T : IEntity;
        Dictionary<FieldKey, object> GetFieldValues();

        IEntity? Clone();
        T? Clone<T>() where T : IEntity;

        IEntity? Copy();
        T? Copy<T>() where T : IEntity;

        void Initialize(IEntityStore entityStore);

    }
}
