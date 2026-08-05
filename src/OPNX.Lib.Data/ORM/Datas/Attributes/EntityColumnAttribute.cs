using System.Data;

namespace OPNX.Lib.Data.ORM.Datas.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class EntityColumnAttribute : Attribute
{
    public string Name { get; set; } = string.Empty;
    public bool UseNamingConvention { get; set; } = true;
    public SqlDbType SqlDataType { get; set; }
    public bool AllowNull { get; set; }
    public Type? ForeignType { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsTimeOnly { get; set; }
    public bool IsReadOnly { get; set; }
    public int ColIndex { get; set; }
    public int FieldLength { get; set; }
}
