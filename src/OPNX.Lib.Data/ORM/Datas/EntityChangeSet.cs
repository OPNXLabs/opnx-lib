using System.Runtime.Serialization;

namespace OPNX.Lib.Data.ORM.Datas
{
    ///// <summary>
    ///// Represents update changes for a list of entities of a single type. The 
    ///// structure is a dictionary of EntityChanges (field changes for a single 
    ///// entity), keyed by entity ID.
    ///// </summary>
    //[Serializable]
    //public class EntityChangeSet : Dictionary<int, EntityChanges>, ISerializable
    //{
    //    public EntityChangeSet() { }

    //    /// <summary>
    //    /// Serialization constructor
    //    /// </summary>
    //    /// <param name="info"></param>
    //    /// <param name="context"></param>
    //    protected EntityChangeSet(SerializationInfo info, StreamingContext context)
    //        : base(info, context)
    //    {
    //        this.EntityType = (EntityTypes)info.GetValue("EntityType", typeof(EntityTypes));
    //    }

    //    /// <summary>
    //    /// Constructor to create a change set for a single entity.
    //    /// </summary>
    //    /// <param name="entityID"></param>
    //    /// <param name="fieldChanges"></param>
    //    public EntityChangeSet(EntityTypes entityType, int entityID, EntityChanges fieldChanges)
    //    {
    //        this.EntityType = entityType;
    //        this.Add(entityID, fieldChanges);
    //    }

    //    /// <summary>
    //    /// Constructor which requires a dictionary of field changes, keyed by entity ID.
    //    /// </summary>
    //    /// <param name="changedEntities"></param>
    //    public EntityChangeSet(EntityTypes entityType, Dictionary<int, EntityChanges> changedEntities)
    //    {
    //        this.EntityType = entityType;

    //        foreach (KeyValuePair<int, EntityChanges> kvp in changedEntities)
    //        {
    //            this.Add(kvp.Key, kvp.Value);
    //        }
    //    }

    //    /// <summary>
    //    /// The type of entity this change set applies to.
    //    /// </summary>
    //    public EntityTypes EntityType
    //    {
    //        get { return _EntityType; }
    //        set
    //        {
    //            _EntityType = value;
    //            _EntityTypeName = value.ToString();
    //        }
    //    }

    //    private EntityTypes _EntityType = EntityTypes.None;

    //    public string EntityTypeName
    //    {
    //        get { return _EntityTypeName; }
    //        set
    //        {
    //            _EntityTypeName = value;
    //            if (Enum.IsDefined(typeof(EntityTypes), value))
    //                _EntityType = (EntityTypes)Enum.Parse(typeof(EntityTypes), value);
    //            else
    //                _EntityType = EntityTypes.None;
    //        }
    //    }

    //    private string _EntityTypeName;

    //    /// <summary>
    //    /// True if only custom properties used for state were updated.
    //    /// </summary>
    //    public bool IsStateChangeOnly
    //    {
    //        get
    //        {
    //            bool isStateChangeOnly = false;

    //            foreach (EntityChanges entityChanges in this.Values)
    //            {
    //                foreach (FieldValueChange fieldChange in entityChanges.Values)
    //                {
    //                    if (fieldChange.Field.IsCustomProp)
    //                        isStateChangeOnly = true;
    //                    else
    //                        return false;
    //                }
    //            }
    //            return isStateChangeOnly;
    //        }
    //    }

    //    /// <summary>
    //    /// True if a foreign key field was changed, indicating 
    //    /// a change to the relationship between entities.
    //    /// </summary>
    //    public bool IsForeignKeyChange
    //    {
    //        get
    //        {
    //            foreach (EntityChanges entityChanges in this.Values)
    //            {
    //                foreach (FieldValueChange fieldChange in entityChanges.Values)
    //                {
    //                    if (fieldChange.Field.IsForeignKey)
    //                        return true;
    //                }
    //            }
    //            return false;
    //        }
    //    }

    //    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    //    {
    //        base.GetObjectData(info, context);

    //        info.AddValue("EntityType", this.EntityType);
    //    }
    //}

    /// <summary>
    /// Represents the changes made to all fields for a single entity.
    /// This class is only used to improve readability in code. It is 
    /// simply a dictionary of FieldValueChanges keyed by field name.
    /// </summary>
    [Serializable]
    public class EntityChanges : Dictionary<string, FieldValueChange>, ISerializable
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public EntityChanges() { }

        /// <summary>
        /// Serialization constructor
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        //protected EntityChanges(SerializationInfo info, StreamingContext context)
        //    : base(info, context) { }

        /// <summary>
        /// Returns true if a field that has changed is a custom prop.
        /// </summary>
        public bool IsCustomFieldChange
        {
            get
            {
                foreach (FieldValueChange fieldChange in this.Values)
                {
                    if (fieldChange.Field.IsCustomProp) return true;
                }

                return false;
            }
        }

        /// <summary>
        ///  Returns true if a field that has changes only a custom prop.
        /// </summary>
        public bool IsCustomFieldChangeOnly
        {
            get
            {
                foreach (FieldValueChange fieldChange in this.Values)
                {
                    if (!fieldChange.Field.IsCustomProp) return false;
                }

                return true;
            }
        }

        public bool IsForignKeyChange
        {
            get
            {
                foreach (FieldValueChange fieldChange in this.Values)
                {
                    if (fieldChange.Field.IsForeignKey)
                        return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Encapsulating class used as a key in a field value dictionary.
    /// </summary>
    [Serializable]
    public class FieldKey(string name, bool isCustomProp, bool isForeignKey)
    {
        //public FieldKey() { }

        //public FieldKey
        //{
        //    this.FieldName = name;
        //    this.IsCustomProp = isCustomProp;
        //    this.IsForeignKey = isForeignKey;
        //}

        public string FieldName { get; set; } = name;
        public bool IsCustomProp { get; set; } = isCustomProp;
        public bool IsForeignKey { get; set; } = isForeignKey;
    }

    [Serializable]
    public class FieldValueChange(FieldKey field, object oldValue, object newValue)
    {
        //public FieldValueChange() { }

        //public FieldValueChange
        //{
        //    this.Field = field;
        //    this.OldValue = oldValue;
        //    this.NewValue = newValue;
        //}

        public FieldKey Field { get; set; } = field;
        public object OldValue { get; set; } = oldValue;
        public object NewValue { get; set; } = newValue;
    }
}
