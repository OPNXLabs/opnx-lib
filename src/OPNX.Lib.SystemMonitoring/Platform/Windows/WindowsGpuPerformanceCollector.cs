using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace OPNX.Lib.SystemMonitoring.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsGpuPerformanceCollector : IDisposable
{
    private const uint ErrorSuccess = 0;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtLarge = 0x00000400;
    private const string EnginePath = @"\GPU Engine(*)\Utilization Percentage";
    private const string MemoryPath = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, GpuCounter> _engineCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GpuCounter> _memoryCounters = new(StringComparer.OrdinalIgnoreCase);
    private IntPtr _query;
    private bool _hasPreviousSample;
    private bool _disposed;

    public IReadOnlyDictionary<string, WindowsGpuPerformanceSample> Collect()
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_query == IntPtr.Zero && PdhOpenQuery(null, UIntPtr.Zero, out _query) != ErrorSuccess)
            {
                _query = IntPtr.Zero;
                return new Dictionary<string, WindowsGpuPerformanceSample>();
            }

            // GPU engine instances change as processes start and exit. Keep existing
            // handles for rate sampling and discover new instances on each collection.
            RefreshCounters(EnginePath, _engineCounters);
            RefreshCounters(MemoryPath, _memoryCounters);
            if (PdhCollectQueryData(_query) != ErrorSuccess)
                return new Dictionary<string, WindowsGpuPerformanceSample>();

            Dictionary<string, MutableGpuSample> samples = new(StringComparer.OrdinalIgnoreCase);
            if (_hasPreviousSample)
            {
                Dictionary<(string Luid, string Engine), double> engineUsage = [];
                foreach (GpuCounter counter in _engineCounters.Values)
                {
                    if (!TryReadDouble(counter.Handle, out double value))
                        continue;

                    var key = (counter.Luid, counter.Engine);
                    engineUsage[key] = engineUsage.GetValueOrDefault(key) + Math.Max(0, value);
                }

                // Sum processes using the same physical engine, then use the busiest
                // engine as the GPU's utilization rather than the busiest single process.
                foreach (var engine in engineUsage)
                {
                    MutableGpuSample sample = GetOrAdd(samples, engine.Key.Luid);
                    sample.UsagePercent = Math.Max(sample.UsagePercent ?? 0, Math.Clamp(engine.Value, 0, 100));
                }
            }

            foreach (GpuCounter counter in _memoryCounters.Values)
            {
                if (!TryReadInt64(counter.Handle, out long value))
                    continue;

                MutableGpuSample sample = GetOrAdd(samples, counter.Luid);
                sample.DedicatedMemoryUsedBytes = (sample.DedicatedMemoryUsedBytes ?? 0) + Math.Max(0, value);
            }

            _hasPreviousSample = true;
            return samples.ToDictionary(
                static pair => pair.Key,
                static pair => new WindowsGpuPerformanceSample(pair.Value.UsagePercent, pair.Value.DedicatedMemoryUsedBytes),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_query != IntPtr.Zero)
            {
                PdhCloseQuery(_query);
                _query = IntPtr.Zero;
            }
            _engineCounters.Clear();
            _memoryCounters.Clear();
        }
    }

    private void RefreshCounters(string wildcardPath, Dictionary<string, GpuCounter> counters)
    {
        HashSet<string> paths = new(ExpandWildcardPath(wildcardPath), StringComparer.OrdinalIgnoreCase);
        foreach (string path in counters.Keys.ToArray())
        {
            if (paths.Contains(path))
                continue;

            PdhRemoveCounter(counters[path].Handle);
            counters.Remove(path);
        }

        foreach (string path in paths)
        {
            if (counters.ContainsKey(path))
                continue;

            Match luid = LuidRegex().Match(path);
            if (!luid.Success || PdhAddEnglishCounter(_query, path, UIntPtr.Zero, out IntPtr handle) != ErrorSuccess)
                continue;

            Match engine = EngineRegex().Match(path);
            counters.Add(path, new GpuCounter(luid.Value.ToLowerInvariant(),
                engine.Success ? engine.Value.ToLowerInvariant() : path, handle));
        }
    }

    private static string[] ExpandWildcardPath(string wildcardPath)
    {
        uint size = 0;
        uint status = PdhExpandWildCardPath(null, wildcardPath, IntPtr.Zero, ref size, 0);
        for (int attempt = 0; status == PdhMoreData && size > 0 && attempt < 3; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)size * sizeof(char)));
            try
            {
                status = PdhExpandWildCardPath(null, wildcardPath, buffer, ref size, 0);
                if (status == ErrorSuccess)
                {
                    // PDH returns MULTI_SZ. StringBuilder marshalling truncates at the
                    // first null and silently loses the remaining GPU/process counters.
                    char[] characters = new char[checked((int)size)];
                    Marshal.Copy(buffer, characters, 0, characters.Length);
                    return new string(characters).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return [];
    }

    private static bool TryReadDouble(IntPtr counter, out double value)
    {
        uint status = PdhGetFormattedCounterValue(counter, PdhFmtDouble, out _, out PdhFormattedCounterValue result);
        value = result.DoubleValue;
        return status == ErrorSuccess && result.Status <= 1 && double.IsFinite(value);
    }

    private static bool TryReadInt64(IntPtr counter, out long value)
    {
        uint status = PdhGetFormattedCounterValue(counter, PdhFmtLarge, out _, out PdhFormattedCounterValue result);
        value = result.LargeValue;
        return status == ErrorSuccess && result.Status <= 1;
    }

    private static MutableGpuSample GetOrAdd(Dictionary<string, MutableGpuSample> samples, string luid)
    {
        if (!samples.TryGetValue(luid, out MutableGpuSample? sample))
            samples.Add(luid, sample = new MutableGpuSample());
        return sample;
    }

    [GeneratedRegex(@"luid_0x[0-9a-f]+_0x[0-9a-f]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LuidRegex();

    [GeneratedRegex(@"phys_\d+_eng_\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EngineRegex();

    private sealed record GpuCounter(string Luid, string Engine, IntPtr Handle);
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

    [DllImport("pdh.dll")]
    private static extern uint PdhRemoveCounter(IntPtr counter);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, UIntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFormattedCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhExpandWildCardPath(string? dataSource, string wildcardPath, IntPtr expandedPathList, ref uint pathListLength, uint flags);
}

internal sealed record WindowsGpuPerformanceSample(double? UsagePercent, long? DedicatedMemoryUsedBytes);
