using System.Data;

namespace OPNX.Lib.Data.ORM.Datas.Attributes
{
    /// <summary>
    /// Attribute used to identify properties which directly correspond to fields in the DB table.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class DataColumnAttribute : Attribute
    {
        /// <summary>
        /// Named parameter identifying the SQL Data Type of the column. NOTE: Specific to MS SQL-Server.
        /// </summary>
        public SqlDbType SqlDataType { get; set; }

        /// <summary>
        /// Named parameter specifying if the column accepts null values. Default value is false. If true, the entity property should accept null values.
        /// </summary>
        public bool AllowNull { get; set; }

        /// <summary>
        /// Named parameter specifying the type of the entity to which this column is a foreign key.
        /// </summary>
        public Type? ForeignType { get; set; }

        /// <summary>
        /// Named parameter specifying if the column is an identify column. Since the value of the column is set automatically by the DB, the column value will not be set on inserts and updates. 
        /// </summary>
        public bool IsIdentity { get; set; }

        /// <summary>
        /// Named parameter specifying if the column is an identify column. Since the value of the column is set automatically by the DB, the column value will not be set on inserts and updates. 
        /// </summary>
        public bool IsPrimaryKey { get; set; }

        /// <summary>
        /// Named parameter specifying that the datetime column should only be used to store Time. The date portion should be ignored if present.
        /// </summary>
        public bool IsTimeOnly { get; set; }

        /// <summary>
        /// Named parameter specifying that the column is read only, meaning that it should be ignored for insert and update statements.
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// Named parameter identifying the column index, primarily for sorting purposes.
        /// </summary>
        public int ColIndex { get; set; }

        /// <summary>
        /// Optional named paramter specifying the maximum field length for string values.
        /// </summary>
        public int FieldLength { get; set; }

        ///// <summary>
        ///// Demand action on update for foreign key
        ///// </summary>
        //public RelationActions OnUpdate { get; set; }

        ///// <summary>
        ///// Demand action on delete for foreign key
        ///// </summary>
        //public RelationActions OnDelete { get; set; }
    }
}
