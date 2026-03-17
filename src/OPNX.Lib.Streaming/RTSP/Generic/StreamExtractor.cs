using OPNX.Lib.Common.Primitives.Media;

namespace OPNX.Lib.Streaming.RTSP.Generic
{
    public readonly struct OBUHeader(byte headerByte)
    {
        private readonly byte headerByte = headerByte;

        /// <summary>OBU Forbidden Bit (bit 7)</summary>
        public bool ObuForbiddenBit => (headerByte & 0b1000_0000) != 0;

        /// <summary>OBU Type (bits 6~3)</summary>
        public byte ObuType => (byte)((headerByte >> 3) & 0b0000_1111);

        /// <summary>OBU Extension Flag (bit 2)</summary>
        public bool ObuExtensionFlag => (headerByte & 0b0000_0100) != 0;

        /// <summary>OBU Has Size Field (bit 1)</summary>
        public bool HasSizeField => (headerByte & 0b0000_0010) != 0;

        /// <summary>OBU Reserved 1 Bit (bit 0)</summary>
        public bool ObuReserved1Bit => (headerByte & 0b0000_0001) != 0;

        public override string ToString()
            => $"OBU(type={ObuType}, hasSize={HasSizeField}, ext={ObuExtensionFlag})";
    }


    public class StreamExtractor
    {
        public static List<ReadOnlyMemory<byte>> StreamSplit(
            CodecId codec,
            ReadOnlyMemory<byte> stream)
            => codec switch
            {
                CodecId.H264 or
                CodecId.H265 => SplitNalUnits(stream),

                CodecId.AV1 => SplitObuUnits(stream),

                CodecId.MJPEG => [stream],

                _ => throw new NotSupportedException($"Unsupported codec: {codec}")
            };

        public static List<ReadOnlyMemory<byte>> SplitObuUnits(ReadOnlyMemory<byte> stream, bool enableLogging = false)
        {
            var obuUnits = new List<ReadOnlyMemory<byte>>();
            int pos = 0;
            var span = stream.Span;

            if (enableLogging)
            {
                Console.WriteLine($"=== AV1 OBU Parsing Started (Stream size: {span.Length} bytes) ===");
                Console.WriteLine($"First 16 bytes: {BitConverter.ToString(span[..Math.Min(16, span.Length)].ToArray())}");
            }

            while (pos < span.Length)
            {
                if (pos + 1 > span.Length)
                {
                    if (enableLogging)
                        Console.WriteLine($"[{pos}] End of stream reached");
                    break;
                }

                int obuStartPos = pos;

                byte obuHeaderByte = span[pos++];
                OBUHeader obuHeader = new(obuHeaderByte);

                if (enableLogging)
                    Console.WriteLine($"\n[Offset {obuStartPos}] {obuHeader}");

                if (obuHeader.ObuForbiddenBit)
                {
                    if (enableLogging)
                        Console.WriteLine($"  ERROR: Forbidden bit is set! Invalid OBU.");
                    throw new ArgumentException($"Invalid OBU at position {obuStartPos}: Forbidden bit is set");
                }

                if (obuHeader.ObuExtensionFlag)
                {
                    if (pos >= span.Length)
                    {
                        if (enableLogging)
                            Console.WriteLine($"  ERROR: Extension flag set but no extension header data");
                        break;
                    }
                    byte extensionHeader = span[pos++];
                    if (enableLogging)
                        Console.WriteLine($"  Extension header: 0x{extensionHeader:X2}");
                }

                //int obuPayloadSize = 0;
                //int sizeLengthBytes = 0;
                int obuPayloadSize;
                int sizeLengthBytes;

                if (obuHeader.HasSizeField)
                {
                    if (pos >= span.Length)
                    {
                        if (enableLogging)
                            Console.WriteLine($"  ERROR: HasSizeField=true but no size data available");
                        break;
                    }

                    try
                    {
                        //obuPayloadSize = ReadLeb128(span.Slice(pos), out sizeLengthBytes);
                        obuPayloadSize = ReadLeb128(span[..pos], out sizeLengthBytes);

                        if (enableLogging)
                            Console.WriteLine($"  Payload size: {obuPayloadSize} bytes (LEB128: {sizeLengthBytes} bytes)");
                    }
                    catch (Exception ex)
                    {
                        if (enableLogging)
                            Console.WriteLine($"  ERROR: LEB128 parsing failed - {ex.Message}");
                        break;
                    }

                    if (obuPayloadSize == 0 && obuHeader.ObuType != 2)
                    {
                        if (enableLogging)
                            Console.WriteLine($"  WARNING: Payload size is 0 for OBU type {obuHeader.ObuType}");
                    }
                }
                else
                {
                    obuPayloadSize = span.Length - pos;
                    sizeLengthBytes = 0;

                    if (enableLogging)
                        Console.WriteLine($"  No size field - using remaining stream: {obuPayloadSize} bytes");
                }

                if (pos + sizeLengthBytes + obuPayloadSize > span.Length)
                {
                    if (enableLogging)
                    {
                        Console.WriteLine($"  ERROR: OBU extends beyond stream boundary");
                        Console.WriteLine($"    Required: {pos + sizeLengthBytes + obuPayloadSize} bytes");
                        Console.WriteLine($"    Available: {span.Length} bytes");
                    }
                    break;
                }

                bool shouldSave = obuHeader.ObuType switch
                {
                    1 => true,  // Sequence Header
                    2 => true,  // Temporal Delimiter
                    3 => true,  // Frame Header
                    6 => true,  // Frame
                    5 => true,  // Metadata (선택적)
                    _ => false
                };

                if (shouldSave)
                {
                    int totalObuLength = (pos - obuStartPos) + sizeLengthBytes + obuPayloadSize;
                    var obuComplete = stream.Slice(obuStartPos, totalObuLength);
                    obuUnits.Add(obuComplete);

                    if (enableLogging)
                    {
                        string typeName = GetObuTypeName(obuHeader.ObuType);
                        Console.WriteLine($"  ✓ Saved {typeName} (total: {totalObuLength} bytes)");
                    }
                }
                else
                {
                    if (enableLogging)
                    {
                        string typeName = GetObuTypeName(obuHeader.ObuType);
                        Console.WriteLine($"  - Skipped {typeName}");
                    }
                }

                pos += sizeLengthBytes + obuPayloadSize;
            }

            if (enableLogging)
            {
                Console.WriteLine($"\n=== Parsing Complete: {obuUnits.Count} OBU(s) extracted ===");
            }

            return obuUnits;
        }

        public static int ReadLeb128(ReadOnlySpan<byte> buffer, out int bytesRead)
        {
            long value = 0;
            int shift = 0;
            bytesRead = 0;

            while (true)
            {
                if (bytesRead >= buffer.Length)
                    throw new ArgumentException($"LEB128 extends beyond buffer (read {bytesRead} bytes, buffer size: {buffer.Length})");

                byte leb128Byte = buffer[bytesRead];
                bytesRead++;


                value |= (long)(leb128Byte & 0x7F) << shift;

                if ((leb128Byte & 0x80) == 0)
                    break;

                shift += 7;

                if (shift >= 63)
                    throw new OverflowException($"LEB128 value too large (shift: {shift})");
            }

            if (value > int.MaxValue)
                throw new OverflowException($"LEB128 value {value} exceeds Int32.MaxValue");

            return (int)value;
        }

        private static string GetObuTypeName(byte obuType)
        {
            return obuType switch
            {
                1 => "Sequence Header",
                2 => "Temporal Delimiter",
                3 => "Frame Header",
                4 => "Tile Group",
                5 => "Metadata",
                6 => "Frame",
                7 => "Redundant Frame Header",
                8 => "Tile List",
                15 => "Padding",
                _ => $"Unknown Type {obuType}"
            };
        }


        /// <summary>
        /// 스트림을 NAL Unit 단위로 분리
        /// H.264/H.265 모두 지원, 3바이트와 4바이트 start code 처리
        /// </summary>
        public static List<ReadOnlyMemory<byte>> SplitNalUnits(ReadOnlyMemory<byte> stream)
        {
            var nalus = new List<ReadOnlyMemory<byte>>();
            ReadOnlySpan<byte> data = stream.Span;

            ReadOnlySpan<byte> startCode3 = [0x00, 0x00, 0x01];
            ReadOnlySpan<byte> startCode4 = [0x00, 0x00, 0x00, 0x01];

            int pos = 0;

            while (pos < data.Length)
            {
                // 현재 위치의 start code 건너뛰기
                if (pos + 4 <= data.Length && data.Slice(pos, 4).SequenceEqual(startCode4))
                {
                    pos += 4;
                }
                else if (pos + 3 <= data.Length && data.Slice(pos, 3).SequenceEqual(startCode3))
                {
                    pos += 3;
                }

                if (pos >= data.Length)
                    break;

                // 다음 start code 찾기
                int nextStart = FindNextStartCode(data, pos, startCode3, startCode4);

                if (nextStart == -1)
                {
                    nalus.Add(stream[..pos]);
                    break;
                }

                //nalus.Add(stream.Slice(pos, nextStart - pos));
                nalus.Add(stream[pos..nextStart]);
                pos = nextStart;
            }

            return nalus;
        }

        /// <summary>
        /// 3바이트/4바이트 start code 다음 위치 찾기
        /// </summary>
        private static int FindNextStartCode(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> startCode3, ReadOnlySpan<byte> startCode4)
        {
            int maxIndex = data.Length - 3; // 최소 3바이트 체크 가능

            for (int i = offset; i <= maxIndex; i++)
            {
                // 먼저 4바이트 체크
                if (i <= data.Length - 4 && data.Slice(i, 4).SequenceEqual(startCode4))
                    return i;

                // 3바이트 체크
                if (data.Slice(i, 3).SequenceEqual(startCode3))
                    return i;
            }

            return -1;
        }

        private static bool CompareByteArrays(byte[] array1, int startIndex, byte[] array2)
        {

            if (startIndex + array2.Length > array1.Length)
                return false;

            for (int i = 0; i < array2.Length; i++)
            {
                if (array1[startIndex + i] != array2[i])
                    return false;
            }

            return true;
        }
        //public static List<byte[]> SplitNALUs(Codec codec, byte[] stream)
        //{
        //    List<byte[]> nalus = new List<byte[]>();
        //    int pos = 0;


        //    switch (codec)
        //    {
        //        case Codec.H265:
        //            {
        //                while (pos < stream.Length)
        //                {
        //                    int nextNaluStart = FindStartCode(stream, codec, pos);

        //                    if (nextNaluStart == -1)
        //                    {
        //                        nalus.Add(SubArray(stream, pos, stream.Length - pos));
        //                        break;
        //                    }
        //                    else if (nextNaluStart > pos)
        //                    {
        //                        nalus.Add(SubArray(stream, pos, nextNaluStart - pos));
        //                        pos = nextNaluStart;
        //                    }
        //                    else
        //                    {
        //                        pos = nextNaluStart + 4; // H.265 NAL Unit은 4바이트 start code를 가집니다.

        //                        if (pos < stream.Length && stream[nextNaluStart] == 0x00 && stream[nextNaluStart + 1] == 0x00 &&
        //                            stream[nextNaluStart + 2] == 0x00 && stream[nextNaluStart + 3] == 0x01)
        //                        {
        //                            pos++; // Skip the additional 0x00 byte if present
        //                        }
        //                    }
        //                }
        //            }
        //            break;
        //        case Codec.H264:                
        //            {
        //                while (pos < stream.Length)
        //                {
        //                    int nextNaluStart = FindStartCode(stream, codec, pos);

        //                    if (nextNaluStart == -1)
        //                    {
        //                        nalus.Add(SubArray(stream, pos, stream.Length - pos));
        //                        break;
        //                    }
        //                    else if (nextNaluStart > pos)
        //                    {
        //                        nalus.Add(SubArray(stream, pos, nextNaluStart - pos));
        //                        pos = nextNaluStart;
        //                    }
        //                    else
        //                    {
        //                        pos = nextNaluStart + 3;

        //                        if (stream[nextNaluStart] == 0x00 && stream[nextNaluStart + 1] == 0x00 &&
        //                            stream[nextNaluStart + 2] == 0x00 && stream[nextNaluStart + 3] == 0x01)
        //                        {
        //                            pos++; // Skip the 4-byte start code
        //                        }
        //                    }
        //                }
        //            }
        //            break;       
        //    }



        //    return nalus;
        //}



        //private static int FindStartCode(Codec codec, byte[] buffer, int start = 0)
        //{
        //    for (int i = start; i < buffer.Length - 3; i++)
        //    {
        //        switch (codec)
        //        {
        //            case Codec.H264:
        //                {
        //                    if (buffer[i] == 0x00 && buffer[i + 1] == 0x00)
        //                    {
        //                        if (buffer[i + 2] == 0x01 || (buffer[i + 2] == 0x00 && buffer[i + 3] == 0x01))
        //                            return i;
        //                    }
        //                }
        //                break;
        //            case Codec.H265:
        //                {
        //                    if (buffer[i] == 0x00 && buffer[i + 1] == 0x00 && buffer[i + 2] == 0x00)
        //                    {
        //                        if (buffer[i + 3] == 0x01)
        //                            return i;
        //                    }
        //                }
        //                break;                
        //        }                
        //    }
        //    return -1;
        //}

        //private static int FindStartCode(byte[] buffer, int offset)
        //{
        //    int end = data.Length - 3;

        //    for (int i = offset; i < end; i++)
        //    {
        //        if (data[i] == 0x00 && data[i + 1] == 0x00)
        //        {
        //            if (data[i + 2] == 0x01)
        //            {
        //                return i;
        //            }
        //            else if (i < end - 1 && data[i + 2] == 0x00 && data[i + 3] == 0x01)
        //            {
        //                return i;
        //            }
        //        }
        //    }

        //    return -1;
        //}

        private static byte[] SubArray(byte[] data, int index, int length)
        {
            byte[] result = new byte[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }
    }
}
