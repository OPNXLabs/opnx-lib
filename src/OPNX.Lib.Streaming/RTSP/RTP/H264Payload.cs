using OPNX.Lib.Common.Logging;
using OPNX.Lib.Streaming.RTSP.Onvif;
using System.Buffers;
using System.Buffers.Binary;

namespace OPNX.Lib.Streaming.RTSP.RTP
{
    //public class H264Payload(ILogger<H264Payload> logger = null, MemoryPool<byte> memoryPool = null) : IPayloadProcessor
    public class H264Payload(MemoryPool<byte>? memoryPool = null) : IPayloadProcessor
    {
        //private readonly ILogger _logger = logger as ILogger ?? NullLogger.Instance;

        private int norm, fu_a, fu_b, stap_a, stap_b, mtap16, mtap24; // used for diagnostics stats

        // Stores the NAL units for a Video Frame. May be more than one NAL unit in a video frame.
        private readonly List<ReadOnlyMemory<byte>> nalUnits = [];
        private readonly List<IMemoryOwner<byte>> owners = [];
        // used to concatenate fragmented H264 NALs where NALs are split over RTP packets
        private readonly MemoryStream fragmentedNal = new();
        private readonly MemoryPool<byte> _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;

        private DateTime _timestamp;
        private bool _isIFrame = false;
        private bool _disposed = false;

        // Process a RTP Packet.
        // Returns a list of NAL Units (with no Size header)
        private void ProcessH264RTPFrame(ReadOnlySpan<byte> payload)
        {
            int nal_header_f_bit = payload[0] >> 7 & 0x01;
            int nal_header_nri = payload[0] >> 5 & 0x03;
            int nal_header_type = payload[0] >> 0 & 0x1F;

            // 일반 NAL (1-23)
            if (nal_header_type >= 1 && nal_header_type <= 23)
            {
                LogManager.Debug("Normal NAL");
                norm++;

                // ✅ 여기서도 I-Frame 판단 추가
                if (nal_header_type == 5)  // IDR (I-Frame)
                {
                    _isIFrame = true;
                    LogManager.Debug("IDR Frame (I-Frame) detected in normal NAL");
                }
                else if (nal_header_type == 1)  // Non-IDR (P/B-Frame)
                {
                    _isIFrame = false;
                }
                // SPS(7), PPS(8)는 _isIFrame 상태 유지

                var nalSpan = PrepareNewNal(payload.Length);
                payload.CopyTo(nalSpan);
            }
            // STAP-A (여러 NAL 집합)
            else if (nal_header_type == 24)
            {
                LogManager.Debug("Agg STAP-A");
                stap_a++;

                try
                {
                    int ptr = 1;
                    while (ptr + 2 < payload.Length - 1)
                    {
                        int size = BinaryPrimitives.ReadUInt16BigEndian(payload[ptr..]);
                        ptr += 2;

                        // ✅ STAP-A 내부 NAL도 타입 체크
                        int inner_nal_type = payload[ptr] & 0x1F;
                        if (inner_nal_type == 5)
                        {
                            _isIFrame = true;
                            LogManager.Debug("IDR Frame detected in STAP-A");
                        }
                        else if (inner_nal_type == 1)
                        {
                            _isIFrame = false;
                        }

                        var nalSpan = PrepareNewNal(size);
                        payload[ptr..(ptr + size)].CopyTo(nalSpan);
                        ptr += size;
                    }
                }
                catch (Exception err)
                {
                    LogManager.Warning(err, "H264 Aggregate Packet processing error");
                }
            }
            else if (nal_header_type == 25)
            {
                LogManager.Debug("Agg STAP-B not supported");
                stap_b++;
            }
            else if (nal_header_type == 26)
            {
                LogManager.Debug("Agg MTAP16 not supported");
                mtap16++;
            }
            else if (nal_header_type == 27)
            {
                LogManager.Debug("Agg MTAP24 not supported");
                mtap24++;
            }
            // FU-A (분할 패킷)
            else if (nal_header_type == 28)
            {
                LogManager.Debug("Frag FU-A");
                fu_a++;

                bool startMarker = (payload[1] >> 7 & 0x01) == 1;
                bool endMarker = (payload[1] >> 6 & 0x01) == 1;
                int fu_header_type = payload[1] >> 0 & 0x1F;

                if (startMarker)
                {
                    // ✅ 시작 패킷에서만 I-Frame 판단
                    if (fu_header_type == 5)
                    {
                        _isIFrame = true;
                        LogManager.Debug("IDR Frame (I-Frame) detected in FU-A");
                    }
                    else if (fu_header_type == 1)
                    {
                        _isIFrame = false;
                    }

                    byte reconstructed_nal_type = (byte)((nal_header_f_bit << 7) + (nal_header_nri << 5) + fu_header_type);
                    fragmentedNal.SetLength(0);
                    fragmentedNal.WriteByte(reconstructed_nal_type);
                }

                fragmentedNal.Write(payload[2..]);

                if (endMarker)
                {
                    var length = (int)fragmentedNal.Length;
                    var nalSpan = PrepareNewNal(length);
                    fragmentedNal.GetBuffer().AsSpan()[..length].CopyTo(nalSpan);
                }
            }
            else if (nal_header_type == 29)
            {
                LogManager.Debug("Frag FU-B not supported");
                fu_b++;
            }
            else
            {
                LogManager.Debug("Unknown NAL header {nalHeaderType} not supported", nal_header_type);

                //if (_logger.IsEnabled(LogLevel.Debug))
                //    _logger.LogDebug
            }
        }

        private Span<byte> PrepareNewNal(int sizeWitoutHeader)
        {
            var owner = _memoryPool.Rent(sizeWitoutHeader + 4);
            owners.Add(owner);
            var memory = owner.Memory[..(sizeWitoutHeader + 4)];
            nalUnits.Add(memory);
            //// Add the NAL start code 00 00 00 01
            //memory.Span[0] = 0;
            //memory.Span[1] = 0;
            //memory.Span[2] = 0;
            //memory.Span[3] = 1;
            //return memory[4..].Span;
            return memory.Span;
        }

        public RawMediaFrame ProcessPacket(ReadOnlySpan<byte> payload)
        {
            ProcessH264RTPFrame(payload);

            // Output some statistics
            //if (_logger.IsEnabled(LogLevel.Debug))
            //    _logger.LogDebug("Norm={norm} ST-A={stapA} ST-B={stapB} M16={mtap16} M24={mtap24} FU-A={fuA} FU-B={fuB}", norm, stap_a, stap_b, mtap16, mtap24, fu_a, fu_b);
            LogManager.Debug("Norm={norm} ST-A={stapA} ST-B={stapB} M16={mtap16} M24={mtap24} FU-A={fuA} FU-B={fuB}", norm, stap_a, stap_b, mtap16, mtap24, fu_a, fu_b);

            // End Marker is set return the list of NALs
            // clone list of nalUnits and owners
            var result = new RawMediaFrame(nalUnits, owners)
            {
                IsKeyFrame = _isIFrame
            };
            nalUnits.Clear();
            owners.Clear();
            return result;
        }

        public RawMediaFrame ProcessPacket(RTPPacket packet)
        {
            if (packet.Extension.Length > 0)
            {
                _timestamp = RtpPacketOnvifUtils.ProcessRTPTimestampExtension(packet.Extension, headerPosition: out _);
            }

            ProcessH264RTPFrame(packet.Payload.Span);

            if (!packet.IsMarker)
            {
                // we don't have a frame yet. Keep accumulating RTP packets
                return RawMediaFrame.Empty;
            }
            // Output some statistics
            //if (_logger.IsEnabled(LogLevel.Debug))
            //    _logger.LogDebug("Norm={norm} ST-A={stapA} ST-B={stapB} M16={mtap16} M24={mtap24} FU-A={fuA} FU-B={fuB}", norm, stap_a, stap_b, mtap16, mtap24, fu_a, fu_b);
            LogManager.Debug("Norm={norm} ST-A={stapA} ST-B={stapB} M16={mtap16} M24={mtap24} FU-A={fuA} FU-B={fuB}", norm, stap_a, stap_b, mtap16, mtap24, fu_a, fu_b);

            // End Marker is set return the list of NALs
            // clone list of nalUnits and owners
            var result = new RawMediaFrame([.. nalUnits], [.. owners])
            {
                RtpTimestamp = packet.TimeStamp,
                ClockTimestamp = _timestamp,
                IsKeyFrame = _isIFrame
            };

            nalUnits.Clear();
            owners.Clear();

            return result;
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
                if (disposing)
                {
                    foreach (var owner in owners)
                    {
                        owner.Dispose(); // 풀로 버퍼 반환
                    }
                    owners.Clear();
                    nalUnits.Clear(); // ReadOnlyMemory 뷰 클리어 (원본 버퍼는 owner.Dispose()로 해제)

                    fragmentedNal.Dispose(); // MemoryStream의 내부 버퍼 해제를 위해 Dispose 호출
                }

                _disposed = true;
            }
        }
    }
}
