namespace OPNX.Lib.Data.ORM.Enums;

[Flags]
public enum CascadeType
{
    None = 0,
    Insert = 1 << 0,
    Update = 1 << 1,
    SoftDelete = 1 << 2,
    Delete = 1 << 3,
    All = Insert | Update | SoftDelete | Delete
}
