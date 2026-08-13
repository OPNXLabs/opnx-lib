using OPNX.Lib.SystemMonitoring.Models;

namespace OPNX.Lib.SystemMonitoring;

public interface ISystemResourceCollector
{
    ValueTask<SystemResourceSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}
