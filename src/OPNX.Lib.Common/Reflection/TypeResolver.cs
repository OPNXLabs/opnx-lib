namespace OPNX.Lib.Common.Reflection
{
    public static class TypeResolver
    {
        public static Type? GetTypeByName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Reverse())
            {
                var type = assembly.GetType(name);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
