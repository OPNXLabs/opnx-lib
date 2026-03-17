namespace OPNX.Lib.Network.Abstractions.Events
{
    public sealed class ConnectedEventArgs(Guid sessionID) : EventArgs
    {
        public Guid SessionID { get; } = sessionID;
    }
}
