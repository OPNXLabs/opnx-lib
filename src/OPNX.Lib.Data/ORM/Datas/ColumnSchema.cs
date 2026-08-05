using OPNX.Lib.Data.ORM.Datas.Attributes;
using System.ComponentModel;
using System.Data;
using System.Reflection;

namespace OPNX.Lib.Data.ORM.Datas
{
    /// <summary>
    /// Provides simplified access to data column properties for use with SundanceSchema.
    /// </summary>
    public class ColumnSchema(PropertyInfo prop, EntityColumnAttribute attribs) : IComparable
    {
        private readonly PropertyInfo _prop = prop;
        private readonly EntityColumnAttribute _attribs = attribs;
        private readonly DefaultValueAttribute? _defaultValueAttrib = GetAttrib<DefaultValueAttribute>(prop);
        //SoftwareIdentityAttribute identityAttrib;
        private int _lastId;

        /// <summary>
        /// Constructor requires the PropertyInfo and EntityColumnAttribute.
        /// </summary>
        /// <param name="columnProp"></param>
        /// <param name="_attribs"></param>
        //public ColumnSchema
        //{
        //    prop = columnProp;
        //    attribs = _attribs;
        //    defaultValueAttrib = GetAttrib<DefaultValueAttribute>(columnProp);
        //}

        //public ColumnSchema(PropertyInfo columnProp, EntityColumnAttribute _attribs, SoftwareIdentityAttribute _identityAttrib)
        //{
        //    prop = columnProp;
        //    attribs = _attribs;
        //    identityAttrib = _identityAttrib;
        //    defaultValueAttrib = GetAttrib<DefaultValueAttribute>(columnProp);
        //}

        /// <summary>
        /// Returns the PropertyInfo object for the column-property.
        /// </summary>
        public PropertyInfo Property
        {
            get { return _prop; }
        }

        /// <summary>
        /// 
        /// </summary>
        public bool IsUnsupported
        {
            get { return isUnsupported; }
            set { isUnsupported = value; }
        }
        bool isUnsupported;

        /// <summary>
        /// Returns the column/property name.
        /// </summary>
        public string ColumnName
        {
            get { return Property.Name; }
        }

        /// <summary>
        /// Returns the .Net type of the property.
        /// </summary>
        public Type ColumnType
        {
            get { return Property.PropertyType; }
        }

        /// <summary>
        /// Returns the SQL DB Type of the property. NOTE: Specific to MS SQL-Server.
        /// </summary>
        public SqlDbType SqlDataType
        {
            get
            {
                return Attributes.SqlDataType;
            }
        }

        /// <summary>
        /// Returns the attributes provided in the constructor, which were set as attributes of column properties in auto-generated entity classes.
        /// </summary>
        public EntityColumnAttribute Attributes
        {
            get { return _attribs; }
        }

        //public SoftwareIdentityAttribute SoftwareIdentity
        //{
        //    get { return this.identityAttrib; }
        //    set { this.identityAttrib = value; }
        //}

        /// <summary>
        /// Returns true if the .Net data type of the column is a number.
        /// </summary>
        public bool IsNumber =>
            Property.PropertyType.Name switch
            {
                "Int16" => true,
                "Int32" => true,
                "Int64" => true,
                "Decimal" => true,
                "Single" => true,
                "Double" => true,
                _ => false
            };
        //public bool IsNumber
        //{
        //    get
        //    {
        //        switch (prop.PropertyType.Name)
        //        {
        //            case "Int16":
        //            case "Int32":
        //            case "Int64":
        //            case "Decimal":
        //            case "Single":
        //            case "Double":
        //                return true;
        //        }
        //        return false;
        //    }
        //}

        /// <summary>
        /// Returns true if the .Net data type of the column is DateTime.
        /// </summary>
        public bool IsDateTime
        {
            get
            {
                return Property.PropertyType.Equals(typeof(DateTime));
            }
        }

        /// <summary>
        /// Returns true if the .Net data type of the column is String.
        /// </summary>
        public bool IsString
        {
            get
            {
                return Property.PropertyType.Equals(typeof(String));
            }
        }

        public bool IsBoolean
        {
            get
            {
                return Property.PropertyType.Equals(typeof(Boolean));
            }
        }

        /// <summary>
        /// Returns the key used for tracking data changes.
        /// </summary>
        public FieldKey FieldKey
        {
            get { return new FieldKey(ColumnName, false, Attributes.ForeignType != null); }
        }

        /// <summary>
        /// Is identity column?
        /// </summary>
        public bool IsIdentity
        {
            get { return Attributes.IsIdentity; }
        }

        /// <summary>
        /// Is software identity column?
        /// </summary>
        //public bool IsSWIdentity
        //{
        //    get
        //    {
        //        return (SoftwareIdentity != null);
        //    }
        //}

        /// <summary>
        /// Gets or sets last number for software identity.
        /// </summary>
        public int LastId
        {
            get { return _lastId; }
            set { _lastId = value; }
        }

        public object? DefaultValue
        {
            get
            {
                if (_defaultValueAttrib == null)
                {
                    if (IsNumber)
                        return 0;
                    else if (IsDateTime)
                        return DateTime.MinValue;
                    else if (IsString)
                        return string.Empty;
                    else if (IsBoolean)
                        return false;
                    else if (Property.PropertyType.IsEnum)
                        return Enum.GetValues(Property.PropertyType).GetValue(0);
                    else
                        return null;
                }

                return _defaultValueAttrib.Value;
            }
        }

        #region IComparable Members

        /// <summary>
        /// Sort by column index.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public int CompareTo(object? obj)
        {
            if (obj == null)
                return 1;

            ColumnSchema toCompare = (ColumnSchema)obj;

            return this.Attributes.ColIndex.CompareTo(toCompare.Attributes.ColIndex);
        }

        #endregion

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
    }
}
