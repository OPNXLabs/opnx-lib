using System.ComponentModel;

namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityIdentity : IEntity<int>, INotifyPropertyChanged
    {
        string? DisplayText { get; }
    }
}
