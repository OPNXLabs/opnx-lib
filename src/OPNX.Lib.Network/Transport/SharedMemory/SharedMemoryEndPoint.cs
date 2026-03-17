namespace OPNX.Lib.Network.Transport.SharedMemory
{
    public sealed record SharedMemoryEndPoint(
        string MapName,
        int MaxMessageLength = SharedMemoryLayout.DefaultMaxMessageLength,
        int BufferCapacity = SharedMemoryLayout.DefaultBufferCapacity)
    {
        public SharedMemoryLayout Layout { get; init; } = SharedMemoryLayout.Default;

        public int MinimumCapacityBytes => checked(Layout.HeaderSize + BufferCapacity);

        public int MinimumFreeBytesForOneMessage(int payloadLen)
            => checked(Layout.MessageHeaderSize + payloadLen);
    }
}
