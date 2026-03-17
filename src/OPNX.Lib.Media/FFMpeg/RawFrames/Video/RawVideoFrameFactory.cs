using System.Buffers;

namespace OPNX.Lib.Media.FFMpeg.RawFrames.Video
{
    public class RawVideoFrameFactory : IDisposable
    {
        #region Fields
        private bool disposed = false;

        private ReadOnlyMemory<byte> cachedSPS = ReadOnlyMemory<byte>.Empty;
        private ReadOnlyMemory<byte> cachedPPS = ReadOnlyMemory<byte>.Empty;
        private ReadOnlyMemory<byte> cachedVPS = ReadOnlyMemory<byte>.Empty;

        private static readonly byte[] StartMarkerArray = RawVideoFrame.StartMarkerArray;
        private static readonly int StartMarkerLength = StartMarkerArray.Length;
        #endregion

        #region Constructors
        public RawVideoFrameFactory()
        {

        }
        #endregion

        #region Private / Protected Methods
        // NAL 유닛 결합 로직을 공통 메서드로 분리
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]        
        //private static void CombineNalUnits(IReadOnlyList<ReadOnlyMemory<byte>> nalUnits, byte[] buffer)
        //{
        //    int offset = 0;

        //    for (int i = 0; i < nalUnits.Count; i++)
        //    {
        //        var nal = nalUnits[i];

        //        // StartMarker 복사
        //        StartMarkerArray.AsSpan().CopyTo(buffer.AsSpan(offset));
        //        offset += StartMarkerLength;

        //        // NAL 데이터 복사 - MemoryMarshal 최적화
        //        if (MemoryMarshal.TryGetArray(nal, out ArraySegment<byte> segment))
        //        {
        //            // 배열 세그먼트인 경우 Buffer.BlockCopy 사용 (더 빠름)
        //            Buffer.BlockCopy(segment.Array!, segment.Offset, buffer, offset, nal.Length);
        //        }
        //        else
        //        {
        //            // 일반적인 경우
        //            //nal.Span.CopyTo(buffer.AsSpan(offset));
        //            Unsafe.CopyBlockUnaligned(ref buffer[offset], ref MemoryMarshal.GetReference(nal.Span), (uint)nal.Length);
        //        }
        //        offset += nal.Length;
        //    }
        //}

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //private static unsafe void CombineNalUnits(IReadOnlyList<ReadOnlyMemory<byte>> nalUnits, byte[] buffer)
        //{
        //    int offset = 0;

        //    fixed (byte* bufferPtr = buffer)
        //    fixed (byte* startMarkerPtr = StartMarkerArray)
        //    {
        //        for (int i = 0; i < nalUnits.Count; i++)
        //        {
        //            var nal = nalUnits[i];

        //            // StartMarker 복사 (보통 4바이트: 00 00 00 01)
        //            Unsafe.CopyBlockUnaligned(bufferPtr + offset, startMarkerPtr, (uint)StartMarkerLength);
        //            offset += StartMarkerLength;

        //            // NAL Unit 복사
        //            var span = nal.Span;
        //            fixed (byte* srcPtr = &MemoryMarshal.GetReference(span))
        //            {
        //                Unsafe.CopyBlockUnaligned(bufferPtr + offset, srcPtr, (uint)span.Length);
        //            }
        //            offset += span.Length;
        //        }
        //    }
        //}

        private static void CombineNalUnits(List<ReadOnlyMemory<byte>> nalUnits, byte[] buffer)
        {
            int offset = 0;

            for (int i = 0; i < nalUnits.Count; i++)
            {
                // StartMarker 복사
                StartMarkerArray.CopyTo(buffer, offset);
                offset += StartMarkerLength;

                // NAL Unit 복사
                nalUnits[i].Span.CopyTo(buffer.AsSpan(offset));
                offset += nalUnits[i].Length;
            }
        }

        // H264 처리 로직 분리
        private RawVideoFrame? ProcessH264Frame(IEnumerable<ReadOnlyMemory<byte>> nalUnits, long timeStamp)
        {
            ReadOnlyMemory<byte> spsNal = default;
            ReadOnlyMemory<byte> ppsNal = default;
            var frameNals = new List<ReadOnlyMemory<byte>>(2);

            int totalFrameLength = 0;
            bool isKeyFrame = false;
            bool hasParameterSets = false;

            foreach (var nal in nalUnits)
            {
                if (nal.IsEmpty) continue;

                int nalType = nal.Span[0] & 0x1F;
                int nalLength = StartMarkerLength + nal.Length;

                switch (nalType)
                {
                    case 7: // SPS
                        spsNal = nal;
                        hasParameterSets = true;
                        break;
                    case 8: // PPS
                        ppsNal = nal;
                        hasParameterSets = true;
                        break;
                    case 5: // IDR
                        isKeyFrame = true;
                        frameNals.Add(nal);
                        totalFrameLength += nalLength;
                        break;
                    case 1: // P-frame or others
                    default:
                        frameNals.Add(nal);
                        totalFrameLength += nalLength;
                        break;
                }
            }

            // 캐시만 하고 종료 (프레임은 생성하지 않음)
            if (frameNals.Count == 0 && hasParameterSets)
            {
                if (spsNal.Length > 0) cachedSPS = spsNal;
                if (ppsNal.Length > 0) cachedPPS = ppsNal;
                return null;
            }

            // 프레임 데이터 결합
            ReadOnlyMemory<byte> combinedFrameData = ReadOnlyMemory<byte>.Empty;
            byte[]? rentedFrameBuffer = null;
            if (totalFrameLength > 0)
            {
                //var buffer = new byte[totalFrameLength];
                rentedFrameBuffer = ArrayPool<byte>.Shared.Rent(totalFrameLength);
                CombineNalUnits(frameNals, rentedFrameBuffer);
                combinedFrameData = new ReadOnlyMemory<byte>(rentedFrameBuffer, 0, totalFrameLength);
            }

            H264ParameterSets parameterSets;
            ReadOnlyMemory<byte> combinedParameterSets;
            byte[] rentedParamSetBuffer;

            // SPS/PPS 세트 만들기
            if (spsNal.Length > 0) cachedSPS = spsNal;
            if (ppsNal.Length > 0) cachedPPS = ppsNal;

            if (isKeyFrame && !cachedSPS.IsEmpty && !cachedPPS.IsEmpty)
            {
                var nalList = new List<ReadOnlyMemory<byte>> { cachedSPS, cachedPPS };
                int paramSetLength = StartMarkerLength * 2 + cachedSPS.Length + cachedPPS.Length;

                //var paramSetBuffer = new byte[paramSetLength];
                rentedParamSetBuffer = ArrayPool<byte>.Shared.Rent(paramSetLength);
                CombineNalUnits(nalList, rentedParamSetBuffer);
                combinedParameterSets = new ReadOnlyMemory<byte>(rentedParamSetBuffer, 0, paramSetLength);

                parameterSets = new H264ParameterSets
                {
                    SPS = cachedSPS,
                    PPS = cachedPPS,
                    Combined = combinedParameterSets
                };

                return new RawH264IFrame(timeStamp, combinedFrameData, parameterSets, rentedFrameBuffer, rentedParamSetBuffer) { IsKeyFrame = true };
            }

            return new RawH264PFrame(timeStamp, combinedFrameData, rentedFrameBuffer);
        }

        // H265 처리 로직 분리        
        private RawVideoFrame? ProcessH265Frame(IEnumerable<ReadOnlyMemory<byte>> nalUnits, long timeStamp)
        {
            ReadOnlyMemory<byte> vpsNal = default;
            ReadOnlyMemory<byte> spsNal = default;
            ReadOnlyMemory<byte> ppsNal = default;
            var frameNals = new List<ReadOnlyMemory<byte>>(2);

            int totalFrameLength = 0;
            bool isKeyFrame = false;
            bool hasParameterSets = false;

            foreach (var nal in nalUnits)
            {
                if (nal.IsEmpty) continue;

                int nalType = (nal.Span[0] >> 1) & 0x3F;
                int nalLength = StartMarkerLength + nal.Length;

                switch (nalType)
                {
                    case 32: // VPS
                        vpsNal = nal;
                        hasParameterSets = true;
                        break;
                    case 33: // SPS
                        spsNal = nal;
                        hasParameterSets = true;
                        break;
                    case 34: // PPS
                        ppsNal = nal;
                        hasParameterSets = true;
                        break;
                    case 19:
                    case 20: // IDR
                        isKeyFrame = true;
                        frameNals.Add(nal);
                        totalFrameLength += nalLength;
                        break;
                    default:
                        frameNals.Add(nal);
                        totalFrameLength += nalLength;
                        break;
                }
            }

            // 캐시만 하고 종료 (프레임은 생성하지 않음)
            if (frameNals.Count == 0 && hasParameterSets)
            {
                if (vpsNal.Length > 0) cachedVPS = vpsNal;
                if (spsNal.Length > 0) cachedSPS = spsNal;
                if (ppsNal.Length > 0) cachedPPS = ppsNal;
                return null;
            }

            // 프레임 데이터 결합
            ReadOnlyMemory<byte> combinedFrameData = ReadOnlyMemory<byte>.Empty;
            byte[]? rentedFrameBuffer = null;
            if (totalFrameLength > 0)
            {
                //var frameBuffer = new byte[totalFrameLength]; 
                rentedFrameBuffer = ArrayPool<byte>.Shared.Rent(totalFrameLength);
                CombineNalUnits(frameNals, rentedFrameBuffer);
                combinedFrameData = new ReadOnlyMemory<byte>(rentedFrameBuffer, 0, totalFrameLength);
            }

            // 파라미터 세트 결합
            H265ParameterSets parameterSets;
            ReadOnlyMemory<byte> combinedParameterSets;
            byte[] rentedParamSetBuffer;

            // VPS/SPS/PPS 세트 만들기
            if (vpsNal.Length > 0) cachedVPS = vpsNal;
            if (spsNal.Length > 0) cachedSPS = spsNal;
            if (ppsNal.Length > 0) cachedPPS = ppsNal;

            if (isKeyFrame && cachedVPS.Length > 0 && cachedSPS.Length > 0 && cachedPPS.Length > 0)
            {
                var paramNals = new List<ReadOnlyMemory<byte>> { cachedVPS, cachedSPS, cachedPPS };
                int totalParamSetLength = StartMarkerLength * 3 + cachedVPS.Length + cachedSPS.Length + cachedPPS.Length;

                //var paramBuffer = new byte[totalParamSetLength];
                rentedParamSetBuffer = ArrayPool<byte>.Shared.Rent(totalParamSetLength);
                CombineNalUnits(paramNals, rentedParamSetBuffer);
                combinedParameterSets = new ReadOnlyMemory<byte>(rentedParamSetBuffer, 0, totalParamSetLength);

                parameterSets = new H265ParameterSets
                {
                    VPS = cachedVPS,
                    SPS = cachedSPS,
                    PPS = cachedPPS,
                    Combined = combinedParameterSets
                };

                return new RawH265IFrame(timeStamp, combinedFrameData, parameterSets, rentedFrameBuffer, rentedParamSetBuffer) { IsKeyFrame = true };
            }

            return new RawH265PFrame(timeStamp, combinedFrameData, rentedFrameBuffer);
        }
        #endregion

        #region Public Methods
        public RawVideoFrame? CreateVideoFrame(string codec, long timeStamp, double fps, IEnumerable<ReadOnlyMemory<byte>> nalUnits)
        {
            if (string.IsNullOrWhiteSpace(codec))
                throw new ArgumentException("Codec cannot be null or empty.", nameof(codec));
            if (nalUnits == null || !nalUnits.Any())
                throw new ArgumentException("NAL units cannot be null or empty.", nameof(nalUnits));

            var nalUnitsList = nalUnits as IReadOnlyList<ReadOnlyMemory<byte>> ?? [.. nalUnits];
            if (nalUnitsList.Count == 0)
                throw new ArgumentException("NAL units cannot be empty.", nameof(nalUnits));

            RawVideoFrame? result = null;

            // 문자열 비교 최적화: ReadOnlySpan<char> 사용
            ReadOnlySpan<char> codecSpan = codec.AsSpan();

            if (codecSpan.Equals("H264", StringComparison.OrdinalIgnoreCase))
            {
                result = ProcessH264Frame(nalUnitsList, timeStamp);
            }
            else if (codecSpan.Equals("H265", StringComparison.OrdinalIgnoreCase))
            {
                result = ProcessH265Frame(nalUnitsList, timeStamp);
            }
            else if (codecSpan.Equals("JPEG", StringComparison.OrdinalIgnoreCase)
                  || codecSpan.Equals("MJPEG", StringComparison.OrdinalIgnoreCase))
            {
                var firstNalUnit = nalUnitsList[0]; // First() 대신 인덱서 사용
                if (!firstNalUnit.IsEmpty)
                {
                    result = new RawJpegFrame(timeStamp, firstNalUnit);
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported codec: {codec}");
            }

            if (result != null)
            {
                result.Codec = codec;
                result.FPS = fps;
            }

            return result;
        }

        public void Dispose()
        {
            if (!disposed)
            {
                // 성능 개선: 캐시된 메모리 정리
                cachedSPS = ReadOnlyMemory<byte>.Empty;
                cachedPPS = ReadOnlyMemory<byte>.Empty;
                cachedVPS = ReadOnlyMemory<byte>.Empty;

                disposed = true;
            }

            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
