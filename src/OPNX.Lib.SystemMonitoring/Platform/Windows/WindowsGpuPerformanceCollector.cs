using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace OPNX.Lib.SystemMonitoring.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsGpuPerformanceCollector
{
    private const uint ErrorSuccess = 0;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtLarge = 0x00000400;
    private const string EnginePath = @"\GPU Engine(*)\% Utilization";
    private const string MemoryPath = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private readonly object _syncRoot = new();
    private IntPtr _query;
    private List<GpuCounter> _engineCounters = [];
    private List<GpuCounter> _memoryCounters = [];
    private bool _initialized;
    private bool _hasPreviousSample;

    public IReadOnlyDictionary<string, WindowsGpuPerformanceSample> Collect()
    {
        lock (_syncRoot)
        {
            EnsureInitialized();
            if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != ErrorSuccess)
                return new Dictionary<string, WindowsGpuPerformanceSample>();

            Dictionary<string, MutableGpuSample> samples = new(StringComparer.OrdinalIgnoreCase);
            if (_hasPreviousSample)
            {
                foreach (GpuCounter counter in _engineCounters)
                {
                    if (!TryReadDouble(counter.Handle, out double value))
                        continue;

                    MutableGpuSample sample = GetOrAdd(samples, counter.Luid);
                    sample.UsagePercent = Math.Max(sample.UsagePercent ?? 0, Math.Clamp(value, 0, 100));
                }
            }

            foreach (GpuCounter counter in _memoryCounters)
            {
                if (!TryReadInt64(counter.Handle, out long value))
                    continue;

                MutableGpuSample sample = GetOrAdd(samples, counter.Luid);
                sample.DedicatedMemoryUsedBytes = Math.Max(0, value);
            }

            _hasPreviousSample = true;
            return samples.ToDictionary(
                static pair => pair.Key,
                static pair => new WindowsGpuPerformanceSample(pair.Value.UsagePercent, pair.Value.DedicatedMemoryUsedBytes),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;
        if (PdhOpenQuery(null, UIntPtr.Zero, out _query) != ErrorSuccess)
        {
            _query = IntPtr.Zero;
            return;
        }

        _engineCounters = AddCounters(EnginePath);
        _memoryCounters = AddCounters(MemoryPath);
        if (_engineCounters.Count == 0 && _memoryCounters.Count == 0)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    private List<GpuCounter> AddCounters(string wildcardPath)
    {
        List<GpuCounter> counters = [];
        foreach (string path in ExpandWildcardPath(wildcardPath))
        {
            Match match = LuidRegex().Match(path);
            if (!match.Success || PdhAddEnglishCounter(_query, path, UIntPtr.Zero, out IntPtr handle) != ErrorSuccess)
                continue;

            counters.Add(new GpuCounter(match.Value.ToLowerInvariant(), handle));
        }

        return counters;
    }

    private static IEnumerable<string> ExpandWildcardPath(string wildcardPath)
    {
        uint size = 0;
        uint status = PdhExpandWildCardPath(null, wildcardPath, null, ref size, 0);
        if (status != PdhMoreData || size == 0)
            return [];

        StringBuilder buffer = new((int)size);
        status = PdhExpandWildCardPath(null, wildcardPath, buffer, ref size, 0);
        return status == ErrorSuccess
            ? buffer.ToString().Split('\0', StringSplitOptions.RemoveEmptyEntries)
            : [];
    }

    private static bool TryReadDouble(IntPtr counter, out double value)
    {
        uint status = PdhGetFormattedCounterValue(counter, PdhFmtDouble, out _, out PdhFormattedCounterValue result);
        value = result.DoubleValue;
        return status == ErrorSuccess && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool TryReadInt64(IntPtr counter, out long value)
    {
        uint status = PdhGetFormattedCounterValue(counter, PdhFmtLarge, out _, out PdhFormattedCounterValue result);
        value = result.LargeValue;
        return status == ErrorSuccess;
    }

    private static MutableGpuSample GetOrAdd(Dictionary<string, MutableGpuSample> samples, string luid)
    {
        if (!samples.TryGetValue(luid, out MutableGpuSample? sample))
            samples.Add(luid, sample = new MutableGpuSample());
        return sample;
    }

    [GeneratedRegex(@"luid_0x[0-9a-f]+_0x[0-9a-f]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LuidRegex();

    private sealed record GpuCounter(string Luid, IntPtr Handle);
    private sealed class MutableGpuSample
    {
        public double? UsagePercent { get; set; }
        public long? DedicatedMemoryUsedBytes { get; set; }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFormattedCounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double DoubleValue;
        [FieldOffset(8)] public long LargeValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, UIntPtr userData, out IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, UIntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFormattedCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhExpandWildCardPath(string? dataSource, string wildcardPath, StringBuilder? expandedPathList, ref uint pathListLength, uint flags);
}

internal sealed record WindowsGpuPerformanceSample(double? UsagePercent, long? DedicatedMemoryUsedBytes);
