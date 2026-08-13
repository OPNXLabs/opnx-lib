using OnEyes.DataBase.Datas;
using OPNX.Lib.Common.Serialization;
using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace OPNX.Lib.Data.ORM
{
    [Serializable]
    public abstract class Entity(IEntityStore? entityStore) : IEntity
    {
        #region Fields
        private IEntityStore? _entityStore = entityStore;

        private int id = 0;
        private bool isDeleted = false;
        private bool isSelected = false;
        private bool isChecked = false;
        private bool isClone = false;

        private DateTime insertTime = DateTime.MinValue;
        private DateTime updateTime = DateTime.MinValue;


        private ObservableCollection<PropertyInfo>? properties = null;

        private List<ColumnSchema>? columnSchemas = null;
        #endregion

        #region Constructors
        public Entity()
            : this(null)
        {
        }
        #endregion

        #region Properties          
        [JsonIgnore]
        public virtual string? DisplayText { get; }

        [JsonIgnore]
        public string CustomKey => $"{EntityTypeName}_{ID}";

        [JsonIgnore]
        public abstract bool IsLogTable { get; }

        [JsonIgnore]
        public virtual bool IsAuditable => false;

        [JsonIgnore]
        public bool IsClone
        {
            get => isClone;
            set => SetProperty(ref isClone, value);
        }

        [JsonIgnore]
        public IEntityStore? EntityStore
        {
            get => _entityStore;
            set => SetProperty(ref _entityStore, value);
        }

        [JsonIgnore]
        public bool IsSelected
        {
            get => isSelected;
            set => SetProperty(ref isSelected, value);
        }

        [JsonIgnore]
        public bool IsChecked
        {
            get => isChecked;
            set => SetProperty(ref isChecked, value);
        }

        [JsonIgnore]
        public virtual string EntityTypeName => this.GetType().FullName!;

        [EntityColumn(ColIndex = 0, SqlDataType = System.Data.SqlDbType.Int, AllowNull = false, IsIdentity = true, IsPrimaryKey = true)]
        public int ID
        {
            get => id;
            set => SetProperty(ref id, value);
        }

        [JsonIgnore]
        public virtual bool IsDeleted
        {
            get => isDeleted;
            set => SetProperty(ref isDeleted, value);
        }

        [JsonIgnore]
        public virtual DateTime InsertTime
        {
            get => insertTime;
            set => SetProperty(ref insertTime, value);
        }

        [JsonIgnore]
        public virtual DateTime UpdateTime
        {
            get => updateTime;
            set => SetProperty(ref updateTime, value);
        }

        protected List<ColumnSchema> ColumnSchemas
        {
            get
            {
                columnSchemas ??= InitializeColumnSchemas();
                return columnSchemas;
            }
        }

        #endregion

        #region Events
        [field: NonSerialized]
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                OnPropertyChanged(propertyName);
                return true;
            }
            return false;
        }
        #endregion

        #region Public Methods       

        public virtual IEntity? Copy()
        {
            return Copy<IEntity>();
        }

        public virtual T? Copy<T>() where T : IEntity
        {
            return InternalClone<T>(setIsClone: false);
        }

        public virtual IEntity? Clone()
        {
            return Clone<IEntity>();
        }

        public virtual T? Clone<T>() where T : IEntity
        {
            return InternalClone<T>(setIsClone: true);
        }

        public virtual void Initialize(IEntityStore entityStore)
        {
            _entityStore = entityStore;

            var relatedListProps = GetRelatedListProps();
            foreach (var relatedProp in relatedListProps)
            {
                relatedProp.Property.SetValue(this, null);
            }
        }

        public virtual void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        public virtual T NotifyUpdated<T>(T newEntity) where T : IEntity
        {
            EntityChanges fieldChanges = this.GetChangedFields<T>(newEntity);
            if (fieldChanges.Count > 0)
            {
                UpdateChangedFields(fieldChanges);
            }

            return (T)(object)this;
        }

        public virtual void NotifyDeleted<T>() where T : IEntity
        {
            if (IsAuditable)
                IsDeleted = true;
        }

        public virtual void NotifyInserted<T>() where T : IEntity { }

        //public T clone<T>(T original)
        //{
        //    T tempMyClass = (T)Activator.CreateInstance(original.GetType());

        //    FieldInfo[] fis = original.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        //    foreach (FieldInfo fi in fis)
        //    {
        //        object fieldValue = fi.GetValue(original);
        //        if (fi.FieldType.Namespace != original.GetType().Namespace)
        //            fi.SetValue(tempMyClass, fieldValue);
        //        else
        //            fi.SetValue(tempMyClass, clone(fieldValue));
        //    }

        //    return tempMyClass;
        //}

        //public virtual T Clone<T>() where T : Entity
        //{
        //    BinaryFormatter formatter = new BinaryFormatter();
        //    MemoryStream s = new MemoryStream();
        //    T cloneEntity = null;
        //    formatter.Serialize(s, this);
        //    s.Position = 0;
        //    cloneEntity = (T)formatter.Deserialize(s);
        //    cloneEntity.EntityStore = EntityStore;

        //    return cloneEntity;
        //}


        //public Entity Clone()
        //{
        //    return DeepCopy<Entity>(this);
        //}

        //public virtual T Clone<T>() where T : Entity
        //{
        //    Type type = this.GetType();

        //    if (this is ICloneable)
        //        return (T)((ICloneable)this).Clone();

        //    List<MemberInfo> fields = new List<MemberInfo>();
        //    if (type.GetCustomAttributes(typeof(SerializableAttribute), false).Length == 0)
        //    {
        //        Type t = type;
        //        while (t != typeof(Object))
        //        {
        //            fields.AddRange(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        //            t = t.BaseType;
        //        }
        //    }
        //    else
        //    {
        //        fields.AddRange(FormatterServices.GetSerializableMembers(this.GetType()));
        //    }

        //    object copy = Activator.CreateInstance(this.GetType());// FormatterServices.GetUninitializedObject(this.GetType());
        //    object[] values = FormatterServices.GetObjectData(this, fields.ToArray());
        //    for (int i = 0; i < values.Length; i++)
        //    {
        //        if (values[i] != null)
        //        {
        //            values[i] = CloneObject(values[i], values[i].GetType());
        //            if (values[i] is IEntity)
        //                (values[i] as IEntity).IsClone = true;
        //        }
        //    }
        //    FormatterServices.PopulateObjectMembers(copy, fields.ToArray(), values);
        //    (copy as T).IsClone = true;

        //    return (T)copy;
        //}

        public virtual void Refresh()
        {
            Type type = this.GetType();

            foreach (var propInfo in type.GetProperties())
            {
                if (!propInfo.CanWrite)
                    OnPropertyChanged(propInfo.Name);
            }
        }

        public virtual void Refresh(string propertyName)
        {
            if (!string.IsNullOrEmpty(propertyName))
            {
                OnPropertyChanged(propertyName);
            }
        }

        [JsonIgnore]
        public ObservableCollection<PropertyInfo> Properties
        {
            get
            {
                properties ??= new ObservableCollection<PropertyInfo>(this.GetType().GetProperties());

                return properties;
            }
        }

        //public List<PropertySchema> GetRelatedListProps()
        //{
        //    List<PropertySchema> result = new List<PropertySchema>();
        //    foreach (PropertyInfo prop in this.Properties)
        //    {
        //        // if the property is a generic list with a SundanceBase as the param, we have a related list
        //        if (prop.PropertyType.IsGenericType)
        //        {
        //            //Type propEntityType = prop.PropertyType.GetGenericArguments()[0];

        //            ForeignKeyAttribute attrib = GetForeignKeyAttrib(prop);
        //            if (attrib != null)
        //                result.Add(new PropertySchema(this, prop, attrib));
        //        }
        //    }
        //    return result;
        //}

        public IEnumerable<PropertySchema> GetRelatedListProps()
        {
            return Properties
                .AsParallel() // 병렬 처리를 통해 성능 최적화
                .Where(prop => prop.PropertyType.IsGenericType) // 제네릭 타입인지 확인
                .Select(prop => new { Property = prop, ForeignKey = GetForeignKeyAttrib(prop) }) // ForeignKeyAttribute 가져오기
                .Where(x => x.ForeignKey != null) // ForeignKeyAttribute가 존재하는 경우만 필터링
                .Select(x => new PropertySchema(this, x.Property, x.ForeignKey!)); // PropertySchema 생성
        }

        //public BlockingCollection<PropertySchema> GetRelatedListProps()
        //{
        //    var result = new BlockingCollection<PropertySchema>();

        //    Parallel.ForEach(Properties, prop =>
        //    {
        //        if (prop.PropertyType.IsGenericType)
        //        {
        //            var attrib = GetForeignKeyAttrib(prop);
        //            if (attrib != null)
        //            {
        //                result.Add(new PropertySchema(this, prop, attrib));
        //            }
        //        }
        //    });

        //    result.CompleteAdding();
        //    return result;
        //}

        public EntityChanges GetChangedFields<T>(T comparedEntity) where T : IEntity
        {
            Dictionary<FieldKey, object> fieldValues = GetFieldValues();
            Dictionary<FieldKey, object> comparedValues = comparedEntity.GetFieldValues();
            EntityChanges changedValues = [];

            foreach (FieldKey fieldKey in fieldValues.Keys)
            {
                //if (!includeCustomFields && fieldKey.IsCustomProp)
                //    continue; // skip custom props if specified

                foreach (FieldKey compareKey in comparedValues.Keys)
                {
                    if (fieldKey.FieldName != compareKey.FieldName)
                        continue;

                    if (!Object.Equals(fieldValues[fieldKey], comparedValues[compareKey]))
                        changedValues.Add(fieldKey.FieldName, new FieldValueChange(fieldKey, fieldValues[fieldKey], comparedValues[compareKey]));

                    break;
                }
            }

            return changedValues;
        }
        #endregion

        #region Private / Protected Methods
        private T? InternalClone<T>(bool setIsClone) where T : IEntity
        {
            T? copiedEntity = DeepCopy<T>((T)(object)this);

            if (copiedEntity is Entity entity)
            {
                entity.EntityStore = _entityStore;
                if (setIsClone)
                    entity.IsClone = true;

                foreach (PropertySchema propertySchema in GetRelatedListProps())
                {
                    object? values = propertySchema.Property.GetValue(entity);

                    if (values is System.Collections.IEnumerable collection)
                    {
                        foreach (object item in collection)
                        {
                            if (item is Entity childEntity)
                            {
                                childEntity.EntityStore = _entityStore;
                                if (setIsClone)
                                    childEntity.IsClone = true;
                            }
                        }
                    }
                }
            }

            return copiedEntity;
        }

        private static ForeignKeyAttribute? GetForeignKeyAttrib(PropertyInfo propInfo)
        {
            object[] attrs = propInfo.GetCustomAttributes(true);

            for (int i = 0; i < attrs.Length; i++)
            {
                if (attrs[i] is ForeignKeyAttribute attr)
                    return attr;
            }

            return null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0051")]
        private static EntityColumnAttribute? GetEntityColumnAttribute(PropertyInfo propInfo)
        {
            object[] attrs = propInfo.GetCustomAttributes(true);

            for (int i = 0; i < attrs.Length; i++)
            {
                if (attrs[i] is EntityColumnAttribute attr)
                    return attr;
            }

            return null;
        }

        //private object CloneObject(object obj, Type type)
        //{
        //    try
        //    {
        //        if (type == typeof(string))
        //            return obj;

        //        var members = FormatterServices.GetSerializableMembers(type);
        //        var data = FormatterServices.GetObjectData(obj, members);
        //        var cloned = Activator.CreateInstance(type);
        //        //var cloned = FormatterServices.GetSafeUninitializedObject(type);
        //        FormatterServices.PopulateObjectMembers
        //            (cloned, members, data);
        //        return cloned;
        //    }
        //    catch (Exception ex)
        //    {
        //    }
        //    return null;
        //}
        protected static T? DeepCopy<T>(T obj) where T : IEntity
        {

            // Serialize the object to JSON
            string json = JsonSerialize.ToJsonString(obj);

            // Deserialize the JSON back to a new object
            T? newObj = JsonSerialize.Deserialize<T>(json);

            return newObj;
        }

        private static T? GetAttrib<T>(PropertyInfo propInfo) where T : class
        {
            object[] attrs = propInfo.GetCustomAttributes(true);

            for (int i = 0; i < attrs.Length; i++)
            {
                if (attrs[i] is T attr)
                    return attr;
            }

            return null;
        }

        private List<ColumnSchema> InitializeColumnSchemas()
        {
            var schemas = new List<ColumnSchema>();

            // 병렬로 처리
            Parallel.ForEach(Properties, propInfo =>
            {
                EntityColumnAttribute? attrib = GetAttrib<EntityColumnAttribute>(propInfo);
                if (attrib != null)
                {
                    // 캐싱
                    lock (schemas)
                    {
                        schemas.Add(new ColumnSchema(propInfo, attrib));
                    }
                }
            });

            // 정렬
            schemas.Sort(); // sorts by column index

            return schemas;
        }
        private void UpdateChangedFields(EntityChanges fieldChanges)
        {
            foreach (FieldValueChange fvc in fieldChanges.Values)
            {
                PropertyInfo? propertyInfo = this.properties?.FirstOrDefault(x => x.Name == fvc.Field.FieldName);
                propertyInfo?.SetValue(this, fvc.NewValue);
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;

            // ID가 동일한지 확인
            if (obj is Entity entity)
                return ID == entity.ID;

            return false;
        }

        public override int GetHashCode()
        {
            // ID를 기반으로 해시코드 생성
            return ID.GetHashCode();
        }

        #endregion

        #region Public Methods
        public Dictionary<FieldKey, object> GetFieldValues()
        {
            Dictionary<FieldKey, object> fieldValues = [];
            // get column values 
            foreach (ColumnSchema col in ColumnSchemas)
            {
                object? fieldValue = col.Property.GetValue(this, null);
                if (fieldValue is null)
                    continue;
                fieldValues.Add(new FieldKey(col.ColumnName, false, col.Attributes.ForeignType != null), fieldValue);
            }
            return fieldValues;
        }
        #endregion
    }
}

