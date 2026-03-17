using OPNX.Lib.Streaming.RTSP.Onvif;
using System.Buffers;

namespace OPNX.Lib.Streaming.RTSP.RTP
{
    /// <summary>
    /// This class handles the AMR Payload
    /// </summary>
    public class AMRPayload(MemoryPool<byte>? memoryPool = null) : IPayloadProcessor
    {
        private readonly MemoryPool<byte> _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
        private bool _disposed = false;

        public RawMediaFrame ProcessPacket(RTPPacket packet)
        {
            // TODO check the RFC to handle the different modes

            // Octet-Aligned Mode (RFC 4867 Section 4.4.1)
            // First byte is the Payload Header
            if (packet.PayloadSize < 1)
            {
                return RawMediaFrame.Empty;
            }
            // byte payloadHeader = payload[0];

            int lenght = packet.PayloadSize - 1;
            IMemoryOwner<byte> owner = _memoryPool.Rent(lenght);
            // The rest of the RTP packet is the AMR data
            packet.Payload[1..].CopyTo(owner.Memory);

            return new([owner.Memory[..lenght]], [owner])
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

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing) { }
                _disposed = true;
            }
        }
    }
}
