using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.Enums;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OPNX.Lib.Data.ORM.Datas;

public static partial class DatabaseNaming
{
    public static DatabaseNamingStyle Style { get; set; } = DatabaseNamingStyle.AsDeclared;

    public static string GetTableName(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        EntityTableAttribute? attribute = entityType.GetCustomAttribute<EntityTableAttribute>();
        string name = string.IsNullOrWhiteSpace(attribute?.Name) ? entityType.Name : attribute.Name;
        return attribute?.UseNamingConvention == false ? name : ApplyConvention(name);
    }

    public static string GetColumnName(PropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);
        EntityColumnAttribute? attribute = property.GetCustomAttribute<EntityColumnAttribute>();
        string name = string.IsNullOrWhiteSpace(attribute?.Name) ? property.Name : attribute.Name;
        return attribute?.UseNamingConvention == false ? name : ApplyConvention(name);
    }

    private static string ApplyConvention(string name)
    {
        return Style switch
        {
            DatabaseNamingStyle.LowerCase => name.ToLowerInvariant(),
            DatabaseNamingStyle.SnakeCase => ToSnakeCase(name),
            _ => name
        };
    }

    private static string ToSnakeCase(string name)
    {
        string acronymSeparated = AcronymBoundaryRegex().Replace(name, "$1_$2");
        return WordBoundaryRegex().Replace(acronymSeparated, "$1_$2").ToLowerInvariant();
    }

    [GeneratedRegex("([A-Z]+)([A-Z][a-z])")]
    private static partial Regex AcronymBoundaryRegex();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundaryRegex();
}
