using OPNX.Lib.SystemMonitoring.Models;

namespace OPNX.Lib.SystemMonitoring.Platform;

internal interface IOperatingSystemMetricsProvider
{
    CpuSnapshot GetCpuSnapshot();
    MemorySnapshot GetMemorySnapshot();
    IReadOnlyList<GpuSnapshot> GetGpuSnapshots();
}
