using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace OPNX.Lib.Network.Protocol.Framing
{
    public static class PacketHeaderExtensions
    {
        // PacketHeader.Size를 그대로 사용
        public const int HeaderSize = PacketHeader.Size;

        // 오프셋(문서화 + 유지보수용) - "와이어 포맷" 기준
        // [0..2] Magic, [3] Version, [4] PacketType, [5] PayloadType, [6] Flags, [7..10] Length, [11] Reserved
        private const int OffMagic0 = 0;
        private const int OffMagic1 = 1;
        private const int OffMagic2 = 2;
        private const int OffVersion = 3;
        private const int OffPacketType = 4;
        private const int OffPayloadType = 5;
        private const int OffFlags = 6;
        private const int OffLength = 7;   // 4 bytes: 7..10
        private const int OffReserved = 11;

        // 안전장치(원하는 값으로 조정)
        public const uint MaxPayloadLength = 64u * 1024 * 1024; // 64MB

        /// <summary>헤더의 Magic(3바이트)만 빠르게 검사</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasValidMagic(this ReadOnlySpan<byte> src)
            => src.Length >= 3
            && src[OffMagic0] == PacketHeader.Magic0
            && src[OffMagic1] == PacketHeader.Magic1
            && src[OffMagic2] == PacketHeader.Magic2;

        /// <summary>헤더(12바이트) 기준 Magic 검사</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasValidHeaderMagic(this ReadOnlySpan<byte> src)
            => src.Length >= HeaderSize
            && src[OffMagic0] == PacketHeader.Magic0
            && src[OffMagic1] == PacketHeader.Magic1
            && src[OffMagic2] == PacketHeader.Magic2;

        /// <summary>PacketHeader를 Span에 기록</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteTo(this in PacketHeader h, Span<byte> dest)
        {
            if (dest.Length < HeaderSize)
                throw new ArgumentException("Destination buffer too small.", nameof(dest));

            dest[OffMagic0] = PacketHeader.Magic0;
            dest[OffMagic1] = PacketHeader.Magic1;
            dest[OffMagic2] = PacketHeader.Magic2;

            dest[OffVersion] = h.Version;
            dest[OffPacketType] = (byte)h.PacketType;
            dest[OffPayloadType] = h.PayloadType;
            dest[OffFlags] = (byte)h.Flags;

            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(OffLength, 4), h.PayloadLength);

            dest[OffReserved] = h.Reserved;
        }

        /// <summary>Span에서 PacketHeader 파싱</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadPacketHeader(this ReadOnlySpan<byte> src, out PacketHeader h)
        {
            h = default;

            if (src.Length < HeaderSize)
                return false;

            if (src[OffMagic0] != PacketHeader.Magic0 ||
                src[OffMagic1] != PacketHeader.Magic1 ||
                src[OffMagic2] != PacketHeader.Magic2)
                return false;

            byte version = src[OffVersion];

            // 버전 정책: 보수적으로 "현재 버전만 허용"
            // 호환 버전 허용하려면 (version == 0 || version > CurrentVersion) 형태로 바꿔도 됨
            //if (version != PacketHeader.CurrentVersion)
            //    return false;

            var packetType = (PacketType)src[OffPacketType];
            byte payloadType = src[OffPayloadType];
            var flags = (PacketFlags)src[OffFlags];
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(OffLength, 4));
            byte reserved = src[OffReserved];

            if (length > MaxPayloadLength)
                return false;

            h = new PacketHeader(flags, packetType, payloadType, length, version, reserved);
            return true;
        }

        /// <summary>PayloadLength만 빠르게 Peek</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryPeekPayloadLength(this ReadOnlySpan<byte> src, out uint length)
        {
            length = 0;

            if (src.Length < HeaderSize)
                return false;

            if (src[OffMagic0] != PacketHeader.Magic0 ||
                src[OffMagic1] != PacketHeader.Magic1 ||
                src[OffMagic2] != PacketHeader.Magic2)
                return false;

            length = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(OffLength, 4));
            if (length > MaxPayloadLength)
            {
                length = 0;
                return false;
            }

            return true;
        }

        /// <summary>HeaderSize + payloadLength를 안전하게 int로 계산</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFrameSize(uint payloadLength, out int frameSize)
        {
            if (payloadLength > (uint)(int.MaxValue - HeaderSize))
            {
                frameSize = 0;
                return false;
            }

            frameSize = HeaderSize + (int)payloadLength;
            return true;
        }

        /// <summary>PipeWriter에 헤더를 바로 기록</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteHeaderTo(this PipeWriter writer, PacketHeader h)
        {
            var span = writer.GetSpan(HeaderSize);
            h.WriteTo(span);
            writer.Advance(HeaderSize);
        }
    }
}
