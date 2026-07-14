using System.ComponentModel;

namespace OPNX.Lib.Data.ORM.Interfaces
{
    public interface IEntityIdentity : INotifyPropertyChanged
    {
        int ID { get; set; }

        string? DisplayText { get; }
    }
}
