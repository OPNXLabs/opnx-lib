namespace OPNX.Lib.Network.Protocol.Framing
{
    public readonly struct PacketHeader(
        PacketFlags flags,
        PacketType packetType,
        byte payloadType,
        uint payloadLength,
        byte version = PacketHeader.CurrentVersion,
        byte reserved = 0)
    {
        public const byte Magic0 = 0xAE;
        public const byte Magic1 = 0xAE;
        public const byte Magic2 = 0xAE;

        public const byte CurrentVersion = 1;
        public const int Size = 12;

        public readonly byte Version = version;
        public readonly PacketFlags Flags = flags;
        public readonly PacketType PacketType = packetType;
        public readonly byte PayloadType = payloadType;
        public readonly uint PayloadLength = payloadLength;
        public readonly byte Reserved = reserved;

        public bool IsCompressed
            => (Flags & PacketFlags.Compressed) != 0;

        public bool IsEncrypted
            => (Flags & PacketFlags.Encrypted) != 0;
    }
}
