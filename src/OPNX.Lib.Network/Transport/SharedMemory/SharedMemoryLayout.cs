namespace OPNX.Lib.Network.Transport.SharedMemory
{
    public readonly record struct SharedMemoryLayout(
        int MessageHeaderSize,
        int HeadOffset,
        int TailOffset,
        int HeaderSize)
    {
        public const int DefaultMaxMessageLength = 1024 * 1024 * 2; // 2MB
        public const int DefaultBufferCapacity = 1024 * 1024 * 64; // 64MB

        public const int DefaultMessageHeaderSize = sizeof(int);

        public const int DefaultHeadOffset = 0;  // long (8 bytes)
        public const int DefaultTailOffset = 8;  // long (8 bytes)
        public const int DefaultHeaderSize = 16; // head+tail = 16 bytes

        public static SharedMemoryLayout Default => new(
            MessageHeaderSize: DefaultMessageHeaderSize,
            HeadOffset: DefaultHeadOffset,
            TailOffset: DefaultTailOffset,
            HeaderSize: DefaultHeaderSize);
    }
}
