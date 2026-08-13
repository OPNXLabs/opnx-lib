using System.Net.NetworkInformation;

namespace OPNX.Lib.SystemMonitoring.Models;

public sealed record SystemResourceSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string MachineName { get; init; }
    public required string OperatingSystem { get; init; }
    public required TimeSpan SystemUptime { get; init; }
    public required CpuSnapshot Cpu { get; init; }
    public required MemorySnapshot Memory { get; init; }
    public required ProcessSnapshot Process { get; init; }
    public IReadOnlyList<DiskVolumeSnapshot> Disks { get; init; } = [];
    public IReadOnlyList<NetworkInterfaceSnapshot> NetworkInterfaces { get; init; } = [];
    public IReadOnlyList<GpuSnapshot> Gpus { get; init; } = [];
}

public sealed record CpuSnapshot
{
    public string? Name { get; init; }
    public required double UsagePercent { get; init; }
    public required int LogicalProcessorCount { get; init; }
}

public sealed record MemorySnapshot
{
    public required long TotalBytes { get; init; }
    public required long AvailableBytes { get; init; }
    public long UsedBytes => Math.Max(0, TotalBytes - AvailableBytes);
    public double UsagePercent => TotalBytes > 0 ? UsedBytes * 100d / TotalBytes : 0d;
}

public sealed record ProcessSnapshot
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required double CpuUsagePercent { get; init; }
    public required long WorkingSetBytes { get; init; }
    public required long PrivateMemoryBytes { get; init; }
    public required int ThreadCount { get; init; }
    public required int HandleCount { get; init; }
    public required TimeSpan Uptime { get; init; }
}

public sealed record DiskVolumeSnapshot
{
    public required string Name { get; init; }
    public required string MountPoint { get; init; }
    public required DriveType DriveType { get; init; }
    public string? FileSystem { get; init; }
    public required long TotalBytes { get; init; }
    public required long AvailableBytes { get; init; }
    public long UsedBytes => Math.Max(0, TotalBytes - AvailableBytes);
    public double UsagePercent => TotalBytes > 0 ? UsedBytes * 100d / TotalBytes : 0d;

    public double TotalGb => TotalBytes / 1_000_000_000d;
    public double AvailableGb => AvailableBytes / 1_000_000_000d;
    public double UsedGb => UsedBytes / 1_000_000_000d;
}

public sealed record NetworkInterfaceSnapshot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required NetworkInterfaceType InterfaceType { get; init; }
    public required OperationalStatus Status { get; init; }
    public required long BytesSent { get; init; }
    public required long BytesReceived { get; init; }
    public required double SentBytesPerSecond { get; init; }
    public required double ReceivedBytesPerSecond { get; init; }
    public long LinkSpeedBitsPerSecond { get; init; }
    public double? UsagePercent => LinkSpeedBitsPerSecond > 0
        ? Math.Clamp((SentBytesPerSecond + ReceivedBytesPerSecond) * 8d * 100d / LinkSpeedBitsPerSecond, 0d, 100d)
        : null;
}

public sealed record GpuSnapshot
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public string? Vendor { get; init; }
    public string? Driver { get; init; }
    public string? DriverVersion { get; init; }
    public double? UsagePercent { get; init; }
    public long? DedicatedMemoryTotalBytes { get; init; }
    public long? DedicatedMemoryUsedBytes { get; init; }
    public double? TemperatureCelsius { get; init; }
    public double? PowerUsageWatts { get; init; }
}
