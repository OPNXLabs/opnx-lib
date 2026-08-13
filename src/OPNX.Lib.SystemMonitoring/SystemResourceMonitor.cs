using OPNX.Lib.SystemMonitoring.Models;

namespace OPNX.Lib.SystemMonitoring;

public sealed class SystemResourceMonitor : IDisposable, IAsyncDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private readonly ISystemResourceCollector _collector;
    private readonly TimeSpan _interval;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _monitorTask;
    private bool _disposed;

    public SystemResourceMonitor(ISystemResourceCollector? collector = null, TimeSpan? interval = null)
    {
        _collector = collector ?? new SystemResourceCollector();
        _interval = interval ?? DefaultInterval;
        if (_interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
    }

    public event EventHandler<SystemResourceSnapshot>? SnapshotCollected;
    public event EventHandler<Exception>? CollectionFailed;

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
                return _monitorTask is { IsCompleted: false };
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_syncRoot)
        {
            if (_monitorTask is { IsCompleted: false })
                return;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            _monitorTask = RunAsync(_cancellationTokenSource.Token);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Start();
        return Task.CompletedTask;
    }

    public void Stop()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    public async Task StopAsync()
    {
        Task? monitorTask;
        lock (_syncRoot)
        {
            _cancellationTokenSource?.Cancel();
            monitorTask = _monitorTask;
        }

        if (monitorTask != null)
            await monitorTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        lock (_syncRoot)
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _monitorTask = null;
        }

        if (_collector is IDisposable disposable)
            disposable.Dispose();
        else if (_collector is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await CollectOnceAsync(cancellationToken).ConfigureAwait(false);
        using PeriodicTimer timer = new(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await CollectOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask CollectOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            SystemResourceSnapshot snapshot = await _collector.CollectAsync(cancellationToken).ConfigureAwait(false);
            SnapshotCollected?.Invoke(this, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CollectionFailed?.Invoke(this, ex);
        }
    }
}
