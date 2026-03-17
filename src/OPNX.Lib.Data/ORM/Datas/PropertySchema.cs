using OPNX.Lib.Data.ORM;
using OPNX.Lib.Data.ORM.Datas.Attributes;
using System.Reflection;

namespace OnEyes.DataBase.Datas
{
    public class PropertySchema
    {
        Entity parentEntity;
        PropertyInfo prop;
        ForeignKeyAttribute attrib;

        /// <summary>
        /// Constructor requires the PropertyInfo object and ForeignKeyAttribute.
        /// </summary>
        /// <param name="_prop"></param>
        /// <param name="_attrib"></param>
        public PropertySchema(Entity _parentEntity, PropertyInfo _prop, ForeignKeyAttribute _attrib)
        {
            parentEntity = _parentEntity;
            prop = _prop;
            attrib = _attrib;
        }

        /// <summary>
        /// Returns the EntitySchema to which this PropertySchema belongs.
        /// </summary>
        public Entity ParentEntity
        {
            get { return parentEntity; }
        }

        /// <summary>
        /// Returns the PropertyInfo object for the column-property.
        /// </summary>
        public PropertyInfo Property
        {
            get { return prop; }
        }


        ///// <summary>
        ///// Returns the ForeignKeyAttrib object.
        ///// </summary>
        public ForeignKeyAttribute ForeignKeyAttribs
        {
            get { return attrib; }
        }
    }
}
