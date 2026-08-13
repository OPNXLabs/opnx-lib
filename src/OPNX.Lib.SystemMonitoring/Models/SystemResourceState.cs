namespace OPNX.Lib.SystemMonitoring.Models;

public sealed record SystemResourceState<TKey>
    where TKey : notnull
{
    public required TKey Key { get; init; }
    public required SystemResourceSnapshot Snapshot { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
}
