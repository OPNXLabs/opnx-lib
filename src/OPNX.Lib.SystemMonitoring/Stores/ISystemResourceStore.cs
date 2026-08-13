using OPNX.Lib.SystemMonitoring.Models;

namespace OPNX.Lib.SystemMonitoring.Stores;

public interface ISystemResourceStore<TKey>
    where TKey : notnull
{
    int Count { get; }

    void SetLatest(TKey key, SystemResourceSnapshot snapshot, DateTimeOffset receivedAtUtc);

    bool TryGetLatest(TKey key, out SystemResourceState<TKey> state);

    bool Remove(TKey key);

    IReadOnlyList<SystemResourceState<TKey>> GetAll();
}
