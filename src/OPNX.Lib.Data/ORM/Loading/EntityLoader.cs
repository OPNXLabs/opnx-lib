using OPNX.Lib.Data.ORM.Interfaces;
using OPNX.Lib.Data.ORM.Mapping;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace OPNX.Lib.Data.ORM.Loading;

public sealed class EntityLoader(DataRowMapper? dataRowMapper = null)
{
    private readonly DataRowMapper _dataRowMapper = dataRowMapper ?? new DataRowMapper();
    private static readonly ConcurrentDictionary<Type, MethodInfo> _insertMethods = new();

    public int Load(Type entityType, DataTable? table, IEntityStore entityStore)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(entityStore);
        Type keyType = GetEntityKeyType(entityType);

        IReadOnlyList<object> entities = _dataRowMapper.Map(entityType, table, entityStore);
        MethodInfo insertMethod = _insertMethods.GetOrAdd(entityType, type => typeof(IEntityStore).GetMethod(nameof(IEntityStore.InsertEntity))!.MakeGenericMethod(type, keyType));
        int loadedCount = 0;

        foreach (object item in entities)
        {
            if (item is not IDatabaseEntity || item is IAuditableEntity { IsAuditable: true } && item is ISoftDeletableEntity { IsDeleted: true })
                continue;

            try
            {
                if (insertMethod.Invoke(entityStore, [item]) is true)
                    loadedCount++;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        return loadedCount;
    }

    private static Type GetEntityKeyType(Type entityType)
    {
        Type[] keyTypes = [.. entityType.GetInterfaces().Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEntity<>)).Select(type => type.GetGenericArguments()[0]).Distinct()];
        return keyTypes.Length switch
        {
            1 => keyTypes[0],
            0 => throw new ArgumentException($"{entityType.FullName} does not implement {typeof(IEntity<>).Name}.", nameof(entityType)),
            _ => throw new ArgumentException($"{entityType.FullName} implements {typeof(IEntity<>).Name} with multiple key types.", nameof(entityType))
        };
    }
}
