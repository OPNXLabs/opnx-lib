namespace OPNX.Lib.Network.Abstractions.Events
{
    public sealed class DisconnectedEventArgs(Guid sessionID, DisconnectReason reason) : EventArgs
    {
        public Guid SessionID { get; } = sessionID;
        public DisconnectReason Reason { get; } = reason;
    }
}
