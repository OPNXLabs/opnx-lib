using OPNX.Lib.Data.ORM.Interfaces;

namespace OPNX.Lib.Data.ORM.EventHandlers
{
    public enum DataChangedTypes { Insert, Update, Delete, PropertyChanged };

    public delegate void EntityChangedEventHandler(object sender, EntityChangedEventArgs e);

    public class EntityChangedEventArgs : EventArgs
    {
        public EntityChangedEventArgs(DataChangedTypes changedType, IEntity? oldEntity = null, IEntity? newEntity = null, string? propertyName = null)
        {
            ChangedType = changedType;
            NewEntity = newEntity;
            OldEntity = oldEntity;
            PropertyName = propertyName;

            if (changedType == DataChangedTypes.PropertyChanged &&
               !string.IsNullOrEmpty(propertyName) &&
               newEntity != null)
            {
                try
                {
                    var propertyInfo = newEntity.GetType().GetProperty(propertyName);
                    if (propertyInfo != null)
                        Value = propertyInfo.GetValue(newEntity);
                }
                catch
                {
                }
            }
        }

        public DataChangedTypes ChangedType { get; private set; }

        public IEntity? NewEntity { get; private set; }
        public IEntity? OldEntity { get; private set; }

        public string? PropertyName { get; private set; }
        public object? Value { get; private set; }

        public int NewEntityID => NewEntity != null ? NewEntity.ID : int.MinValue;

        public bool IsLogTable => NewEntity != null && NewEntity.IsLogTable;

        public T GetEntityType<T>() where T : struct, Enum
        {
            return NewEntity != null && Enum.TryParse(NewEntity.GetType().Name, out T value) ?
                value : default;
            //try
            //{
            //    if (NewEntity != null)
            //        return (T)Enum.Parse(typeof(T), NewEntity.GetType().Name);
            //}
            //catch (Exception ex)
            //{
            //}
            //return default(T);
        }
    }
}


