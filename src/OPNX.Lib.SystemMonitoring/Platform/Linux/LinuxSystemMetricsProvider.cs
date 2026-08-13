using OPNX.Lib.SystemMonitoring.Models;
using System.Globalization;
using System.Runtime.Versioning;

namespace OPNX.Lib.SystemMonitoring.Platform.Linux;

[SupportedOSPlatform("linux")]
internal sealed class LinuxSystemMetricsProvider : IOperatingSystemMetricsProvider
{
    private readonly object _syncRoot = new();
    private readonly string? _cpuName = ReadCpuName();
    private ulong _previousIdle;
    private ulong _previousTotal;
    private bool _hasPreviousCpuSample;

    public CpuSnapshot GetCpuSnapshot()
    {
        string? cpuLine = File.ReadLines("/proc/stat").FirstOrDefault(static line => line.StartsWith("cpu ", StringComparison.Ordinal));
        if (cpuLine is null)
            throw new InvalidDataException("The aggregate CPU line is missing from /proc/stat.");

        string[] fields = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 5)
            throw new InvalidDataException("The aggregate CPU line in /proc/stat is invalid.");

        ulong[] values = fields.Skip(1)
            .Select(static value => ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture))
            .ToArray();
        ulong idle = values[3] + (values.Length > 4 ? values[4] : 0);
        ulong total = values.Aggregate(0UL, static (sum, value) => sum + value);
        double usage = 0;

        lock (_syncRoot)
        {
            if (_hasPreviousCpuSample)
            {
                ulong totalDelta = total - _previousTotal;
                ulong idleDelta = idle - _previousIdle;
                usage = totalDelta > 0
                    ? Math.Clamp((totalDelta - Math.Min(idleDelta, totalDelta)) * 100d / totalDelta, 0d, 100d)
                    : 0d;
            }

            _previousIdle = idle;
            _previousTotal = total;
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
        Dictionary<string, long> values = [];
        foreach (string line in File.ReadLines("/proc/meminfo"))
        {
            int separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
                continue;

            string key = line[..separatorIndex];
            string rawValue = line[(separatorIndex + 1)..].Trim();
            string numericValue = rawValue.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (long.TryParse(numericValue, NumberStyles.None, CultureInfo.InvariantCulture, out long kilobytes))
                values[key] = checked(kilobytes * 1024);
        }

        if (!values.TryGetValue("MemTotal", out long totalBytes))
            throw new InvalidDataException("MemTotal is missing from /proc/meminfo.");

        long availableBytes = values.TryGetValue("MemAvailable", out long available)
            ? available
            : values.GetValueOrDefault("MemFree") + values.GetValueOrDefault("Buffers") + values.GetValueOrDefault("Cached");

        return new MemorySnapshot
        {
            TotalBytes = totalBytes,
            AvailableBytes = Math.Clamp(availableBytes, 0, totalBytes)
        };
    }

    public IReadOnlyList<GpuSnapshot> GetGpuSnapshots()
    {
        const string drmRoot = "/sys/class/drm";
        if (!Directory.Exists(drmRoot))
            return [];

        List<GpuSnapshot> snapshots = [];
        foreach (string cardPath in Directory.EnumerateDirectories(drmRoot, "card*"))
        {
            string cardName = Path.GetFileName(cardPath);
            if (cardName.Length <= 4 || !cardName.AsSpan(4).ToString().All(char.IsDigit))
                continue;

            string devicePath = Path.Combine(cardPath, "device");
            if (!Directory.Exists(devicePath))
                continue;

            Dictionary<string, string> uevent = ReadKeyValueFile(Path.Combine(devicePath, "uevent"), '=');
            string? pciId = uevent.GetValueOrDefault("PCI_ID");
            string? driver = ReadLinkName(Path.Combine(devicePath, "driver"));
            string? vendor = GetVendorName(pciId, ReadText(Path.Combine(devicePath, "vendor")));
            string? deviceId = ReadText(Path.Combine(devicePath, "device"));
            string name = ReadText(Path.Combine(devicePath, "product_name"))
                          ?? BuildGpuName(vendor, deviceId)
                          ?? cardName;

            snapshots.Add(new GpuSnapshot
            {
                Id = uevent.GetValueOrDefault("PCI_SLOT_NAME") ?? cardName,
                Name = name,
                Vendor = vendor,
                Driver = driver,
                DriverVersion = ReadDriverVersion(driver),
                UsagePercent = ReadDouble(Path.Combine(devicePath, "gpu_busy_percent")),
                DedicatedMemoryTotalBytes = ReadLong(Path.Combine(devicePath, "mem_info_vram_total")),
                DedicatedMemoryUsedBytes = ReadLong(Path.Combine(devicePath, "mem_info_vram_used")),
                TemperatureCelsius = ReadHwmonValue(devicePath, "temp1_input", 1000d),
                PowerUsageWatts = ReadHwmonValue(devicePath, "power1_average", 1_000_000d)
            });
        }

        return snapshots;
    }

    private static string? ReadCpuName()
    {
        try
        {
            if (!File.Exists("/proc/cpuinfo"))
                return null;

            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                int separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                    continue;

                string key = line[..separatorIndex].Trim();
                if (key is not ("model name" or "Processor" or "Hardware" or "Model"))
                    continue;

                string value = line[(separatorIndex + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static Dictionary<string, string> ReadKeyValueFile(string path, char separator)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return values;

        foreach (string line in File.ReadLines(path))
        {
            int separatorIndex = line.IndexOf(separator);
            if (separatorIndex > 0)
                values[line[..separatorIndex]] = line[(separatorIndex + 1)..].Trim();
        }

        return values;
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() is string value && value.Length > 0 ? value : null : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static long? ReadLong(string path) =>
        long.TryParse(ReadText(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

    private static double? ReadDouble(string path) =>
        double.TryParse(ReadText(path), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

    private static double? ReadHwmonValue(string devicePath, string fileName, double divisor)
    {
        string hwmonRoot = Path.Combine(devicePath, "hwmon");
        if (!Directory.Exists(hwmonRoot))
            return null;

        foreach (string hwmonPath in Directory.EnumerateDirectories(hwmonRoot))
        {
            double? value = ReadDouble(Path.Combine(hwmonPath, fileName));
            if (value.HasValue)
                return value.Value / divisor;
        }

        return null;
    }

    private static string? ReadLinkName(string path)
    {
        try
        {
            FileSystemInfo? target = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
            return target?.Name;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? ReadDriverVersion(string? driver)
    {
        if (string.IsNullOrWhiteSpace(driver))
            return null;

        string moduleVersionPath = Path.Combine("/sys/module", driver.Replace('-', '_'), "version");
        return ReadText(moduleVersionPath);
    }

    private static string? GetVendorName(string? pciId, string? vendorId)
    {
        string value = pciId?.Split(':')[0] ?? vendorId ?? string.Empty;
        value = value.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        return value.ToUpperInvariant() switch
        {
            "10DE" => "NVIDIA",
            "1002" => "AMD",
            "8086" => "Intel",
            "1AF4" => "Red Hat",
            "15AD" => "VMware",
            _ => string.IsNullOrWhiteSpace(value) ? null : $"PCI {value}"
        };
    }

    private static string? BuildGpuName(string? vendor, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(vendor) && string.IsNullOrWhiteSpace(deviceId))
            return null;
        return string.IsNullOrWhiteSpace(deviceId) ? $"{vendor} GPU" : $"{vendor} GPU ({deviceId})";
    }
}
