namespace OPNX.Lib.Data.ORM.Datas.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true)]
    public class CustomEntityPropertyAttribute : Attribute
    {
        public CustomEntityPropertyAttribute(string _fieldName, Type _entityType)
        {
            fieldName = _fieldName;
            entityType = _entityType;
        }

        private string fieldName;
        /// <summary>
        /// Read-only property which returns the name of the Foreign Key field.
        /// </summary>
        public string FieldName
        {
            get { return fieldName; }
            set { fieldName = value; }
        }

        private Type entityType;
        /// <summary>
        /// Named parameter used to identify the type of the related item or list.
        /// </summary>
        public Type EntityType
        {
            get { return this.entityType; }
            set { this.entityType = value; }
        }
    }
}
