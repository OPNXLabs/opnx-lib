using OPNX.Lib.Network.Protocol.Framing;

namespace OPNX.Lib.Network.Abstractions.Events
{
    public sealed class PacketReceivedEventArgs(Guid sessionID, PacketHeader header, ReadOnlyMemory<byte> payload) : EventArgs
    {
        public Guid SessionID { get; } = sessionID;
        public PacketHeader Heeader { get; } = header;
        public ReadOnlyMemory<byte> Payload { get; } = payload;
    }
}
