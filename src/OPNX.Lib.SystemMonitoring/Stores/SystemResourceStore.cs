using OPNX.Lib.SystemMonitoring.Models;
using System.Collections.Concurrent;

namespace OPNX.Lib.SystemMonitoring.Stores;

public sealed class SystemResourceStore<TKey> : ISystemResourceStore<TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, SystemResourceState<TKey>> _states = new();

    public int Count => _states.Count;

    public void SetLatest(TKey key, SystemResourceSnapshot snapshot, DateTimeOffset receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _states[key] = new SystemResourceState<TKey>
        {
            Key = key,
            Snapshot = snapshot,
            ReceivedAtUtc = receivedAtUtc
        };
    }

    public bool TryGetLatest(TKey key, out SystemResourceState<TKey> state)
    {
        return _states.TryGetValue(key, out state!);
    }

    public bool Remove(TKey key)
    {
        return _states.TryRemove(key, out _);
    }

    public IReadOnlyList<SystemResourceState<TKey>> GetAll()
    {
        return [.. _states.Values];
    }
}
