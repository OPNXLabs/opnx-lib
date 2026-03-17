using System.Buffers.Binary;

namespace OPNX.Lib.Streaming.RTSP.RTP
{
    public struct RTPHeader
    {
        public const int MIN_HEADER_LEN = 12;
        public const int RTP_VERSION = 2;

        //public RTPHeader()
        //{
        //    Version = RTP_VERSION;
        //    SequenceNumber = Crypto.GetRandomUInt16();
        //    SyncSource = Crypto.GetRandomUInt();
        //    Timestamp = Crypto.GetRandomUInt();
        //}

        public RTPHeader(int payloadType, int payloadSize, uint timeStamp, int markerBit, uint syncSource, ushort sequenceNumber)
        {
            Version = RTP_VERSION;
            PayloadType = payloadType;
            PayloadSize = payloadSize;
            TimeStamp = timeStamp;
            MarkerBit = markerBit;
            SyncSource = syncSource;
            CSRCList = null;
            SequenceNumber = sequenceNumber;

            HasPadding = false;
            PaddingCount = 0;
            HasExtension = false;
            ExtensionLength = 0;
            ExtensionProfile = 0;
            ExtensionPayload = null;
            CSRCCount = 0;
        }

        public RTPHeader(ReadOnlySpan<byte> rawData)
        {
            if (rawData.Length < MIN_HEADER_LEN)
            {
                throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTP header packet.");
            }

            Version = rawData[0] >> 6;
            HasPadding = ((rawData[0] >> 5) & 0x01) > 0;
            HasExtension = ((rawData[0] >> 4) & 0x01) > 0;
            CSRCCount = rawData[0] & 0x0F;
            MarkerBit = (rawData[1] >> 7) & 0x01;
            PayloadType = rawData[1] & 0x7F;
            SequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(rawData.Slice(2, 2));
            TimeStamp = BinaryPrimitives.ReadUInt32BigEndian(rawData.Slice(4, 4));
            SyncSource = BinaryPrimitives.ReadUInt32BigEndian(rawData.Slice(8, 4));
            CSRCList = null;

            ExtensionLength = 0;
            ExtensionPayload = null;
            ExtensionProfile = 0;
            PaddingCount = 0;

            int headerLength = MIN_HEADER_LEN + (CSRCCount * 4);

            if (HasExtension && rawData.Length >= headerLength + 4)
            {
                ExtensionProfile = BinaryPrimitives.ReadUInt16BigEndian(rawData.Slice(headerLength, 2));
                ExtensionLength = BinaryPrimitives.ReadUInt16BigEndian(rawData.Slice(headerLength + 2, 2));

                int extensionPayloadStart = headerLength + 4;
                int extensionPayloadLength = ExtensionLength * 4;
                if (rawData.Length >= extensionPayloadStart + extensionPayloadLength)
                {
                    var payload = new byte[extensionPayloadLength];
                    rawData.Slice(extensionPayloadStart, extensionPayloadLength).CopyTo(payload);
                    ExtensionPayload = payload;
                }
                else
                {
                    throw new ApplicationException("Invalid extension payload length.");
                }
            }

            PayloadSize = rawData.Length - headerLength - (HasExtension ? 4 + (ExtensionLength * 4) : 0);

            if (HasPadding)
            {
                PaddingCount = rawData[^1];
                PayloadSize -= PaddingCount;

                if (rawData.Length < headerLength + PayloadSize + PaddingCount)
                {
                    throw new ApplicationException("Invalid padding count.");
                }
                //PaddingCount = packet[packet.Length - 1];
                //if (PaddingCount < PayloadSize)
                //{
                //    PayloadSize -= PaddingCount;
                //}
            }
        }

        public int Version { get; private set; }                 // 2 bits
        public bool HasPadding { get; private set; }             // 1 bit
        public bool HasExtension { get; private set; }     // 1 bit
        public int CSRCCount { get; private set; }               // 4 bits
        public int MarkerBit { get; private set; }               // 1 bit        
        public int PayloadType { get; private set; }             // 7 bits
        public ushort SequenceNumber { get; private set; }       // 16 bits
        public uint TimeStamp { get; private set; }              // 32 bits
        public uint SyncSource { get; private set; }             // 32 bits
        public int[]? CSRCList { get; private set; }              // 32 bits
        public ushort ExtensionProfile { get; private set; }     // 16 bits
        public ushort ExtensionLength { get; private set; }      // 16 bits (length of the header extensions in 32 bit words)
        public ReadOnlyMemory<byte> ExtensionPayload { get; private set; } // byte[]에서 변경

        public int PayloadSize { get; private set; }
        public byte PaddingCount { get; private set; }
        //public DateTime ReceivedTime { get; private set; }

        public readonly int Length => MIN_HEADER_LEN + (CSRCCount * 4) + (HasExtension == false ? 0 : 4 + (ExtensionLength * 4));

        public byte[] GetHeader(uint sequenceNumber, uint timeStamp, uint syncSource)
        {
            SequenceNumber = (ushort)sequenceNumber;
            TimeStamp = timeStamp;
            SyncSource = syncSource;
            return GetBytes();
        }

        public readonly byte[] GetBytes()
        {
            byte[] header = new byte[Length];
            Span<byte> span = header;

            span[0] = (byte)((Version << 6) | ((HasPadding ? 1 : 0) << 5) | ((HasExtension ? 1 : 0) << 4) | (CSRCCount & 0x0F));
            span[1] = (byte)((MarkerBit << 7) | (PayloadType & 0x7F));
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), SequenceNumber);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), TimeStamp);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), SyncSource);

            if (HasExtension)
            {
                int offset = 12 + 4 * CSRCCount;
                BinaryPrimitives.WriteUInt16BigEndian(span[offset..], ExtensionProfile);
                BinaryPrimitives.WriteUInt16BigEndian(span[(offset + 2)..], ExtensionLength);
            }

            if (!ExtensionPayload.IsEmpty)
            {
                ExtensionPayload.Span.CopyTo(span[(16 + 4 * CSRCCount)..]);
            }

            return header;
        }

        private readonly RTPHeaderExtensionData? GetExtensionAtPosition(ref int position, int id, int len, RTPHeaderExtensionType type, out bool invalid)
        {
            RTPHeaderExtensionData? ext = null;
            if (!ExtensionPayload.IsEmpty)
            {
                if (id != 0)
                {
                    if (position + len > ExtensionPayload.Length)
                    {
                        invalid = true;
                        return null;
                    }

                    // Skip().Take() 대신 Slice 사용
                    ext = new RTPHeaderExtensionData(id, ExtensionPayload.Slice(position, len).ToArray(), type);
                    position += len;
                }
                else
                {
                    position++;
                }

                var span = ExtensionPayload.Span;
                while (position < span.Length && span[position] == 0)
                {
                    position++;
                }
            }
            invalid = false;
            return ext;
        }

        public readonly List<RTPHeaderExtensionData> GetHeaderExtensions()
        {
            var extensions = new List<RTPHeaderExtensionData>();

            if (ExtensionPayload.IsEmpty)
            {
                return extensions;
            }

            var span = ExtensionPayload.Span;
            var i = 0;

            while (i + 1 < span.Length)
            {
                RTPHeaderExtensionData? extension;
                bool invalid;

                if (HasOneByteExtension())
                {
                    var id = (span[i] & 0xF0) >> 4;
                    var len = (span[i] & 0x0F) + 1;
                    i++;
                    extension = GetExtensionAtPosition(ref i, id, len, RTPHeaderExtensionType.OneByte, out invalid);
                }
                else if (HasTwoByteExtension())
                {
                    var id = span[i++];
                    var len = span[i++] + 1;
                    extension = GetExtensionAtPosition(ref i, id, len, RTPHeaderExtensionType.TwoByte, out invalid);
                }
                else
                {
                    // We don't recognize this extension, ignore it
                    break;
                }

                if (!invalid && extension != null)
                {
                    extensions.Add(extension);
                }
            }

            return extensions;
        }

        private readonly bool HasOneByteExtension()
        {
            return ExtensionProfile == 0xBEDE;
        }

        private readonly bool HasTwoByteExtension()
        {
            return (ExtensionProfile & 0b1111111111110000) == 0b0001000000000000;
        }

        public static bool TryParse(
            ReadOnlySpan<byte> buffer,
            out RTPHeader header,
            out int consumed)
        {
            header = new RTPHeader();
            consumed = 0;
            int offset = 0;
            if (buffer.Length < MIN_HEADER_LEN)
            {
                return false;
            }

            var firstWord = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2));
            offset += 2;

            header.SequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2));
            offset += 2;
            header.TimeStamp = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(offset, 4));
            offset += 4;
            header.SyncSource = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(offset, 4));
            offset += 4;

            header.Version = firstWord >> 14;
            header.HasPadding = ((firstWord >> 13) & 0x1) > 0;
            header.HasExtension = ((firstWord >> 12) & 0x1) > 0;
            header.CSRCCount = (firstWord >> 8) & 0xf;

            header.MarkerBit = (firstWord >> 7) & 0x1;
            header.PayloadType = firstWord & 0x7f;

            int headerAndCSRCLength = offset + 4 * header.CSRCCount;

            if (header.HasExtension && (buffer.Length >= (headerAndCSRCLength + 4)))
            {
                header.ExtensionProfile = BinaryPrimitives.ReadUInt16BigEndian(buffer[offset..]);
                offset += 2;
                header.ExtensionLength = BinaryPrimitives.ReadUInt16BigEndian(buffer[offset..]);
                offset += 2 + header.ExtensionLength * 4;

                var extensionPayloadLength = header.ExtensionLength * 4;
                if (header.ExtensionLength > 0 && buffer.Length >= headerAndCSRCLength + 4 + extensionPayloadLength)
                {
                    var payload = new byte[extensionPayloadLength];
                    buffer.Slice(headerAndCSRCLength + 4, extensionPayloadLength).CopyTo(payload);
                    header.ExtensionPayload = payload;
                }
            }

            header.PayloadSize = buffer.Length - offset;
            if (header.HasPadding)
            {
                // ReSharper disable once UseIndexFromEndExpression
                header.PaddingCount = buffer[^1];
                if (header.PaddingCount < header.PayloadSize)//Prevent some protocol attacks 
                {
                    header.PayloadSize -= header.PaddingCount;
                }
            }

            consumed = offset;
            return header.PayloadSize >= 0;
        }
    }
}
