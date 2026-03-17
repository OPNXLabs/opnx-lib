using System.Buffers.Binary;

namespace OPNX.Lib.Streaming.RTSP.RTP
{
    public static class RtpPacketUtil
    {
        public const int RTP_VERSION = 2;

        public static void WriteHeader(Span<byte> packet, int version, bool padding, bool hasExtension,
            int csrcCount, bool marker, int payloadType)
        {
            packet[0] = (byte)((version << 6) | ((padding ? 1 : 0) << 5) | ((hasExtension ? 1 : 0) << 4) | csrcCount);
            packet[1] = (byte)(((marker ? 1 : 0) << 7) | (payloadType & 0x7F));
        }

        public static int DataOffset(int csrcCount, int? extensionDataSizeInWord)
            => 12 + (csrcCount * 4) + ((extensionDataSizeInWord + 1) * 4 ?? 0);

        public static void WriteSequenceNumber(Span<byte> packet, ushort sequenceNumber)
            => BinaryPrimitives.WriteUInt16BigEndian(packet[2..], sequenceNumber);

        public static void WriteTimestamp(Span<byte> packet, uint timestamp)
            => BinaryPrimitives.WriteUInt32BigEndian(packet[4..], timestamp);

        public static void WriteSSRC(Span<byte> packet, uint ssrc)
            => BinaryPrimitives.WriteUInt32BigEndian(packet[8..], ssrc);
    }
}
