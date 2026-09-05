namespace OPNX.Lib.Data.ORM.EventHandlers;

/// <summary>
/// Describes an EntityStore mutation that failed after its database operation
/// had already succeeded. The database result is authoritative; subscribers
/// may use this notification to schedule an EntityStore reload.
/// </summary>
public sealed class EntityStoreSynchronizationFailedEventArgs(
    string operation,
    Type entityType,
    Exception exception) : EventArgs
{
    #region Properties
    public string Operation { get; } = operation;
    public Type EntityType { get; } = entityType;
    public Exception Exception { get; } = exception;
    #endregion
}
