using OPNX.Lib.SystemMonitoring.Models;
using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OPNX.Lib.SystemMonitoring.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSystemMetricsProvider : IOperatingSystemMetricsProvider
{
    private const string ProcessorRegistryPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string DisplayAdapterRegistryPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private readonly object _syncRoot = new();
    private readonly string? _cpuName = ReadCpuName();
    private readonly WindowsGpuPerformanceCollector _gpuPerformanceCollector = new();
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasPreviousCpuSample;

    public CpuSnapshot GetCpuSnapshot()
    {
        if (!GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        ulong idle = idleTime.ToUInt64();
        ulong kernel = kernelTime.ToUInt64();
        ulong user = userTime.ToUInt64();
        double usage = 0;

        lock (_syncRoot)
        {
            if (_hasPreviousCpuSample)
            {
                ulong idleDelta = idle - _previousIdle;
                ulong kernelDelta = kernel - _previousKernel;
                ulong userDelta = user - _previousUser;
                ulong totalDelta = kernelDelta + userDelta;
                usage = totalDelta > 0
                    ? Math.Clamp((totalDelta - Math.Min(idleDelta, totalDelta)) * 100d / totalDelta, 0d, 100d)
                    : 0d;
            }

            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            _hasPreviousCpuSample = true;
        }

        return new CpuSnapshot
        {
            Name = _cpuName,
            UsagePercent = usage,
            LogicalProcessorCount = Environment.ProcessorCount
        };
    }

    public MemorySnapshot GetMemorySnapshot()
    {
        MemoryStatusEx status = new();
        if (!GlobalMemoryStatusEx(status))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new MemorySnapshot
        {
            TotalBytes = ToInt64(status.TotalPhysical),
            AvailableBytes = ToInt64(status.AvailablePhysical)
        };
    }

    public IReadOnlyList<GpuSnapshot> GetGpuSnapshots()
    {
        IReadOnlyDictionary<string, WindowsGpuPerformanceSample> performanceSamples = _gpuPerformanceCollector.Collect();
        IReadOnlyList<WindowsGpuAdapter> dxgiAdapters = WindowsGpuAdapterEnumerator.GetAdapters();
        if (dxgiAdapters.Count > 0)
        {
            return dxgiAdapters.Select(adapter =>
            {
                performanceSamples.TryGetValue(adapter.Luid, out WindowsGpuPerformanceSample? performance);
                return new GpuSnapshot
                {
                    Id = adapter.Luid,
                    Name = adapter.Name,
                    Vendor = GetVendorName(adapter.VendorId),
                    UsagePercent = performance?.UsagePercent,
                    DedicatedMemoryTotalBytes = adapter.DedicatedMemoryBytes,
                    DedicatedMemoryUsedBytes = performance?.DedicatedMemoryUsedBytes
                };
            }).ToArray();
        }

        List<GpuSnapshot> snapshots = [];
        using RegistryKey? displayAdapters = Registry.LocalMachine.OpenSubKey(DisplayAdapterRegistryPath);
        if (displayAdapters is null)
            return snapshots;

        foreach (string subKeyName in displayAdapters.GetSubKeyNames())
        {
            using RegistryKey? adapter = displayAdapters.OpenSubKey(subKeyName);
            if (adapter is null)
                continue;

            string? name = GetRegistryString(adapter, "DriverDesc") ?? GetRegistryString(adapter, "HardwareInformation.AdapterString");
            string? matchingDeviceId = GetRegistryString(adapter, "MatchingDeviceId");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(matchingDeviceId))
                continue;

            string id = GetRegistryString(adapter, "NetCfgInstanceId") ?? matchingDeviceId;
            if (snapshots.Any(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
                continue;

            string? luid = GetAdapterLuid(adapter);
            performanceSamples.TryGetValue(luid ?? string.Empty, out WindowsGpuPerformanceSample? performance);
            snapshots.Add(new GpuSnapshot
            {
                Id = luid ?? id,
                Name = name.Trim(),
                Vendor = GetRegistryString(adapter, "ProviderName") ?? GetVendorName(matchingDeviceId),
                Driver = GetRegistryString(adapter, "InstalledDisplayDrivers"),
                DriverVersion = GetRegistryString(adapter, "DriverVersion"),
                UsagePercent = performance?.UsagePercent,
                DedicatedMemoryTotalBytes = GetRegistryInt64(adapter, "HardwareInformation.qwMemorySize"),
                DedicatedMemoryUsedBytes = performance?.DedicatedMemoryUsedBytes
            });
        }

        foreach ((string luid, WindowsGpuPerformanceSample performance) in performanceSamples)
        {
            if (snapshots.Any(x => string.Equals(x.Id, luid, StringComparison.OrdinalIgnoreCase)))
                continue;

            snapshots.Add(new GpuSnapshot
            {
                Id = luid,
                Name = $"GPU ({luid})",
                UsagePercent = performance.UsagePercent,
                DedicatedMemoryUsedBytes = performance.DedicatedMemoryUsedBytes
            });
        }

        return snapshots;
    }

    private static string? ReadCpuName()
    {
        try
        {
            using RegistryKey? processor = Registry.LocalMachine.OpenSubKey(ProcessorRegistryPath);
            return GetRegistryString(processor, "ProcessorNameString")?.Trim();
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? GetRegistryString(RegistryKey? key, string valueName)
    {
        object? value = key?.GetValue(valueName);
        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            string[] values when values.Length > 0 => string.Join(", ", values.Where(static item => !string.IsNullOrWhiteSpace(item))),
            _ => null
        };
    }

    private static long? GetRegistryInt64(RegistryKey key, string valueName)
    {
        object? value = key.GetValue(valueName);
        return value switch
        {
            long number when number >= 0 => number,
            int number when number >= 0 => number,
            byte[] bytes when bytes.Length >= sizeof(long) => BitConverter.ToInt64(bytes, 0),
            _ => null
        };
    }

    private static string? GetAdapterLuid(RegistryKey key)
    {
        object? value = key.GetValue("AdapterLuid");
        ulong luid = value switch
        {
            long number => unchecked((ulong)number),
            byte[] bytes when bytes.Length >= sizeof(ulong) => BitConverter.ToUInt64(bytes, 0),
            _ => 0
        };

        return luid == 0
            ? null
            : $"luid_0x{luid >> 32:x8}_0x{luid & uint.MaxValue:x8}";
    }

    private static string? GetVendorName(string deviceId)
    {
        if (deviceId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase)) return "NVIDIA";
        if (deviceId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase)) return "AMD";
        if (deviceId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase)) return "Intel";
        if (deviceId.Contains("VEN_1414", StringComparison.OrdinalIgnoreCase)) return "Microsoft";
        return null;
    }

    private static string? GetVendorName(uint vendorId) => vendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 => "AMD",
        0x8086 => "Intel",
        0x1414 => "Microsoft",
        _ => null
    };

    private static long ToInt64(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
