using OPNX.Lib.SystemMonitoring.Models;
using OPNX.Lib.SystemMonitoring.Platform;
using OPNX.Lib.SystemMonitoring.Platform.Linux;
using OPNX.Lib.SystemMonitoring.Platform.Windows;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace OPNX.Lib.SystemMonitoring;

public sealed class SystemResourceCollector : ISystemResourceCollector, IDisposable
{
    private readonly IOperatingSystemMetricsProvider _operatingSystemMetricsProvider;
    private readonly object _syncRoot = new();
    private readonly Process _process;
    private readonly Dictionary<string, NetworkCounterSample> _previousNetworkSamples = new(StringComparer.Ordinal);
    private TimeSpan _previousProcessCpuTime;
    private long _previousProcessTimestamp;
    private bool _hasPreviousProcessSample;
    private bool _disposed;

    public SystemResourceCollector()
        : this(CreateOperatingSystemMetricsProvider(), Process.GetCurrentProcess())
    {
    }

    internal SystemResourceCollector(IOperatingSystemMetricsProvider operatingSystemMetricsProvider, Process process)
    {
        _operatingSystemMetricsProvider = operatingSystemMetricsProvider ?? throw new ArgumentNullException(nameof(operatingSystemMetricsProvider));
        _process = process ?? throw new ArgumentNullException(nameof(process));
    }

    public ValueTask<SystemResourceSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            long sampleTimestamp = Stopwatch.GetTimestamp();

            return ValueTask.FromResult(new SystemResourceSnapshot
            {
                Timestamp = timestamp,
                MachineName = Environment.MachineName,
                OperatingSystem = Environment.OSVersion.VersionString,
                SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
                Cpu = _operatingSystemMetricsProvider.GetCpuSnapshot(),
                Memory = _operatingSystemMetricsProvider.GetMemorySnapshot(),
                Process = GetProcessSnapshot(timestamp, sampleTimestamp),
                Disks = GetDiskSnapshots(),
                NetworkInterfaces = GetNetworkSnapshots(sampleTimestamp),
                Gpus = GetGpuSnapshots()
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _process.Dispose();
    }

    private ProcessSnapshot GetProcessSnapshot(DateTimeOffset timestamp, long sampleTimestamp)
    {
        _process.Refresh();
        TimeSpan totalCpuTime = _process.TotalProcessorTime;
        double cpuUsage = 0;

        if (_hasPreviousProcessSample)
        {
            double elapsedSeconds = Stopwatch.GetElapsedTime(_previousProcessTimestamp, sampleTimestamp).TotalSeconds;
            if (elapsedSeconds > 0)
            {
                cpuUsage = (totalCpuTime - _previousProcessCpuTime).TotalSeconds * 100d /
                           (elapsedSeconds * Environment.ProcessorCount);
                cpuUsage = Math.Clamp(cpuUsage, 0d, 100d);
            }
        }

        _previousProcessCpuTime = totalCpuTime;
        _previousProcessTimestamp = sampleTimestamp;
        _hasPreviousProcessSample = true;

        DateTimeOffset startTime = _process.StartTime.ToUniversalTime();
        return new ProcessSnapshot
        {
            ProcessId = _process.Id,
            ProcessName = _process.ProcessName,
            CpuUsagePercent = cpuUsage,
            WorkingSetBytes = _process.WorkingSet64,
            PrivateMemoryBytes = _process.PrivateMemorySize64,
            ThreadCount = _process.Threads.Count,
            HandleCount = GetHandleCount(_process),
            Uptime = timestamp > startTime ? timestamp - startTime : TimeSpan.Zero
        };
    }

    private static IReadOnlyList<DiskVolumeSnapshot> GetDiskSnapshots()
    {
        List<DiskVolumeSnapshot> snapshots = [];
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                    continue;

                snapshots.Add(new DiskVolumeSnapshot
                {
                    Name = drive.Name,
                    MountPoint = drive.RootDirectory.FullName,
                    DriveType = drive.DriveType,
                    FileSystem = drive.DriveFormat,
                    TotalBytes = drive.TotalSize,
                    AvailableBytes = drive.AvailableFreeSpace
                });
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return snapshots;
    }

    private IReadOnlyList<NetworkInterfaceSnapshot> GetNetworkSnapshots(long sampleTimestamp)
    {
        List<NetworkInterfaceSnapshot> snapshots = [];
        HashSet<string> currentIds = new(StringComparer.Ordinal);

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                IPv4InterfaceStatistics statistics = networkInterface.GetIPv4Statistics();
                string id = networkInterface.Id;
                currentIds.Add(id);
                double sentRate = 0;
                double receivedRate = 0;

                if (_previousNetworkSamples.TryGetValue(id, out NetworkCounterSample previous))
                {
                    double elapsedSeconds = Stopwatch.GetElapsedTime(previous.Timestamp, sampleTimestamp).TotalSeconds;
                    if (elapsedSeconds > 0)
                    {
                        sentRate = CalculateRate(previous.BytesSent, statistics.BytesSent, elapsedSeconds);
                        receivedRate = CalculateRate(previous.BytesReceived, statistics.BytesReceived, elapsedSeconds);
                    }
                }

                _previousNetworkSamples[id] = new NetworkCounterSample(statistics.BytesSent, statistics.BytesReceived, sampleTimestamp);
                snapshots.Add(new NetworkInterfaceSnapshot
                {
                    Id = id,
                    Name = networkInterface.Name,
                    Description = networkInterface.Description,
                    InterfaceType = networkInterface.NetworkInterfaceType,
                    Status = networkInterface.OperationalStatus,
                    BytesSent = statistics.BytesSent,
                    BytesReceived = statistics.BytesReceived,
                    SentBytesPerSecond = sentRate,
                    ReceivedBytesPerSecond = receivedRate,
                    LinkSpeedBitsPerSecond = networkInterface.Speed
                });
            }
            catch (NetworkInformationException)
            {
            }
        }

        foreach (string removedId in _previousNetworkSamples.Keys.Where(id => !currentIds.Contains(id)).ToArray())
            _previousNetworkSamples.Remove(removedId);

        return snapshots;
    }

    private static IOperatingSystemMetricsProvider CreateOperatingSystemMetricsProvider()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsSystemMetricsProvider();
        if (OperatingSystem.IsLinux())
            return new LinuxSystemMetricsProvider();

        throw new PlatformNotSupportedException($"System monitoring is not supported on {Environment.OSVersion.Platform}.");
    }

    private IReadOnlyList<GpuSnapshot> GetGpuSnapshots()
    {
        try
        {
            return _operatingSystemMetricsProvider.GetGpuSnapshots();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (System.Security.SecurityException)
        {
            return [];
        }
        catch (PlatformNotSupportedException)
        {
            return [];
        }
    }

    private static double CalculateRate(long previous, long current, double elapsedSeconds) =>
        current >= previous ? (current - previous) / elapsedSeconds : 0d;

    private static int GetHandleCount(Process process)
    {
        try
        {
            return process.HandleCount;
        }
        catch (PlatformNotSupportedException)
        {
            return 0;
        }
    }

    private readonly record struct NetworkCounterSample(long BytesSent, long BytesReceived, long Timestamp);
}
