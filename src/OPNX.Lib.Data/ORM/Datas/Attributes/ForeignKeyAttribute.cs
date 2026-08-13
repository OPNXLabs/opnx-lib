namespace OPNX.Lib.Data.ORM.Datas.Attributes
{
    /// <summary>
    /// Property to identify the property name of the foreign key field. This property will
    /// be in the current entity for individual entity properties, and in the foreign entity
    /// for lists. For example:
    /// <code>
    /// In the first case, the foreign key specifies the name of the property in the current
    /// entity class (in this case Camera) which contains the value of the primary key of the
    /// entity the property will be set to (in this case also Device).
    /// 
    /// [ForeignKey("DeviceID")]
    /// public Device Device { get { ...
    /// 
    /// If no attribute is defined, the application will default to [PropertyName]ID.
    /// 
    /// In the second case, the foreign key specifies the name of the property in the target
    /// entity class (DeviceConnection) which contains the value of the primary key of the 
    /// current entity (StorageServer).
    /// 
    /// [ForeignKey("StorageServerID")]
    /// public SortedBindingList{DeviceConnection} DeviceConnections { get { ...
    /// 
    /// If no attribute is defined, the application will default to [CurrentEntityType]ID.
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class ForeignKeyAttribute : Attribute
    {
        /// <summary>
        /// Identifies the name of the foreign key property used to establish relationships. This
        /// is the property in the foreign entity class if property references a single object, and
        /// is the property in the same entity class if referencing a collection of related objects.
        /// </summary>
        /// <param name="foreignKeyFieldName"></param>
        public ForeignKeyAttribute(string _foreignKeyField, Type _relatedType)
        {
            foreignKeyField = _foreignKeyField;
            relatedType = _relatedType;
        }

        private string foreignKeyField;
        /// <summary>
        /// Read-only property which returns the name of the Foreign Key field.
        /// </summary>
        public string ForeignKeyField
        {
            get { return foreignKeyField; }
            set { foreignKeyField = value; }
        }

        private Type relatedType;
        /// <summary>
        /// Named parameter used to identify the type of the related item or list.
        /// </summary>
        public Type RelatedType
        {
            get { return this.relatedType; }
            set { this.relatedType = value; }
        }

        /// <summary>
        /// Controls which persistence operations are propagated to the related collection.
        /// Destructive operations must be explicitly enabled for owned relationships.
        /// </summary>
        public OPNX.Lib.Data.ORM.Enums.CascadeType Cascade { get; set; } =
            OPNX.Lib.Data.ORM.Enums.CascadeType.Insert | OPNX.Lib.Data.ORM.Enums.CascadeType.Update;


    }
}
