using OPNX.Lib.Streaming.RTSP.Onvif;
using System.Buffers;

namespace OPNX.Lib.Streaming.RTSP.RTP
{
    public class RawPayload(MemoryPool<byte>? memoryPool = null) : IPayloadProcessor
    {
        private readonly MemoryPool<byte> _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;

        private bool _disposed = false;


        public RawMediaFrame ProcessPacket(RTPPacket packet)
        {
            var owner = _memoryPool.Rent(packet.PayloadSize);
            var memory = owner.Memory[..packet.PayloadSize];
            packet.Payload.CopyTo(memory);
            return new RawMediaFrame([memory], [owner])
            {
                ClockTimestamp = RtpPacketOnvifUtils.ProcessRTPTimestampExtension(packet.Extension, headerPosition: out _),
                RtpTimestamp = packet.TimeStamp,
            };
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing) { }
                _disposed = true;
            }
        }
    }
}
