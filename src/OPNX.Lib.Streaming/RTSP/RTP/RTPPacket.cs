namespace OPNX.Lib.Streaming.RTSP.RTP
{
    public readonly struct RTPPacket
    {
        #region Fields
        private readonly ReadOnlyMemory<byte> rawData;
        private readonly RTPHeader header;

        private readonly int headerSize;
        private readonly int extensionSize;
        private readonly int paddingSize;
        #endregion

        #region Constructors
        public RTPPacket(ReadOnlyMemory<byte> rawData)
        {
            this.rawData = rawData;
            var span = rawData.Span;

            header = new RTPHeader(span);
            headerSize = RTPHeader.MIN_HEADER_LEN + (header.CSRCCount * 4);

            if (header.HasExtension)
            {
                int extensionLength = (span[headerSize + 2] << 8) + span[headerSize + 3] + 1;
                extensionSize = extensionLength * 4;
            }
            else
            {
                extensionSize = 0;
            }

            paddingSize = header.HasPadding ? span[^1] : 0;
        }

        public RTPPacket(ReadOnlySpan<byte> span)
        {
            // Span을 배열로 복사하여 저장
            byte[] owned = span.ToArray();
            this.rawData = owned;

            header = new RTPHeader(span);
            headerSize = RTPHeader.MIN_HEADER_LEN + (header.CSRCCount * 4);

            if (header.HasExtension)
            {
                int extensionLength = (span[headerSize + 2] << 8) + span[headerSize + 3] + 1;
                extensionSize = extensionLength * 4;
            }
            else
            {
                extensionSize = 0;
            }

            paddingSize = header.HasPadding ? span[^1] : 0;
        }
        #endregion

        #region Properties
        public RTPHeader Header => header;

        public bool HasExtension => header.HasExtension;
        public bool HasPadding => header.HasPadding;
        public int HeaderSize => headerSize;
        public int ExtensionSize => extensionSize;
        public int PaddingSize => paddingSize;
        public int PayloadType => header.PayloadType;
        public int PayloadSize => header.PayloadSize;
        public uint TimeStamp => header.TimeStamp;
        public int MarkerBit => header.MarkerBit;
        public bool IsMarker => MarkerBit > 0;

        public ReadOnlyMemory<byte> RawData => rawData;
        public ReadOnlyMemory<byte> Payload
        {
            get
            {
                int start = HeaderSize + ExtensionSize;
                int length = rawData.Length - start - PaddingSize;
                return rawData.Slice(start, length);
            }
        }
        public ReadOnlyMemory<byte> Extension
        {
            get
            {
                if (!HasExtension) return ReadOnlyMemory<byte>.Empty;
                return rawData.Slice(HeaderSize, ExtensionSize);
            }
        }
        #endregion

        //public byte[] GetBytes()
        //{
        //    byte[] header = Header.GetBytes();
        //    byte[] packet = new byte[header.Length + Payload.Length];

        //    Buffer.BlockCopy(header, 0, packet, 0, header.Length);
        //    Buffer.BlockCopy(Payload, 0, packet, header.Length, Payload.Length);

        //    //Array.Copy(header, packet, header.Length);
        //    //Array.Copy(Payload, 0, packet, header.Length, Payload.Length);

        //    return packet;
        //}

        //private byte[] GetNullPayload(int numBytes)
        //{
        //    byte[] payload = new byte[numBytes];

        //    for (int byteCount = 0; byteCount < numBytes; byteCount++)
        //    {
        //        payload[byteCount] = 0xff;
        //    }

        //    return payload;
        //}

        //public static bool TryParse(
        //    ReadOnlySpan<byte> buffer,
        //    ref RTPPacket packet,
        //    out int consumed)
        //{
        //    consumed = 0;
        //    if (RTPHeader.TryParse(buffer, out var header, out var headerConsumed))
        //    {
        //        packet.Header = header;
        //        consumed += headerConsumed;
        //        packet.Payload = buffer.Slice(headerConsumed, header.PayloadSize).ToArray();
        //        consumed += header.PayloadSize;
        //        return true;
        //    }

        //    return false;
        //}

        //public static bool TryParse(
        //    ReadOnlySpan<byte> buffer,
        //    out RTPPacket packet,
        //    out int consumed)
        //{
        //    packet = new RTPPacket();
        //    return TryParse(buffer, packet, out consumed);
        //}
    }
}
