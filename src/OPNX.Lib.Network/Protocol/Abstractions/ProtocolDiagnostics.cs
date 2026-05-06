namespace OPNX.Lib.Network.Protocol.Abstractions
{
    public sealed class ProtocolDiagnostics
    {
        private long _connectCount;
        private long _disconnectCount;
        private long _outboundQueuedCount;
        private long _outboundSentCount;
        private long _inboundProcessedCount;
        private long _droppedPacketCount;
        private long _sentBytes;
        private long _receivedBytes;

        public long ConnectCount => Interlocked.Read(ref _connectCount);
        public long DisconnectCount => Interlocked.Read(ref _disconnectCount);
        public long OutboundQueuedCount => Interlocked.Read(ref _outboundQueuedCount);
        public long OutboundSentCount => Interlocked.Read(ref _outboundSentCount);
        public long InboundProcessedCount => Interlocked.Read(ref _inboundProcessedCount);
        public long DroppedPacketCount => Interlocked.Read(ref _droppedPacketCount);
        public long SentBytes => Interlocked.Read(ref _sentBytes);
        public long ReceivedBytes => Interlocked.Read(ref _receivedBytes);

        internal void MarkConnected() => Interlocked.Increment(ref _connectCount);
        internal void MarkDisconnected() => Interlocked.Increment(ref _disconnectCount);
        internal void MarkOutboundQueued(int bytes)
        {
            Interlocked.Increment(ref _outboundQueuedCount);
            Interlocked.Add(ref _sentBytes, bytes);
        }

        internal void MarkOutboundSent() => Interlocked.Increment(ref _outboundSentCount);

        internal void MarkInboundProcessed(int bytes)
        {
            Interlocked.Increment(ref _inboundProcessedCount);
            Interlocked.Add(ref _receivedBytes, bytes);
        }

        internal void MarkDroppedPacket() => Interlocked.Increment(ref _droppedPacketCount);
    }
}
