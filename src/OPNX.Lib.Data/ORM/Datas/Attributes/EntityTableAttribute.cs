namespace OPNX.Lib.Data.ORM.Datas.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class EntityTableAttribute : Attribute
{
    public EntityTableAttribute() { }

    public EntityTableAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; set; } = string.Empty;
    public bool UseNamingConvention { get; set; } = true;
}
