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
        if (!typeof(IEntity).IsAssignableFrom(entityType))
            throw new ArgumentException($"{entityType.FullName} does not implement {nameof(IEntity)}.", nameof(entityType));

        IReadOnlyList<object> entities = _dataRowMapper.Map(entityType, table, entityStore);
        MethodInfo insertMethod = _insertMethods.GetOrAdd(entityType, static type => typeof(IEntityStore).GetMethod(nameof(IEntityStore.InsertEntity))!.MakeGenericMethod(type));
        int loadedCount = 0;

        foreach (object item in entities)
        {
            if (item is not IEntity entity || entity.IsAuditable && entity.IsDeleted)
                continue;

            try
            {
                insertMethod.Invoke(entityStore, [entity]);
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
}
