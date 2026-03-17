using OPNX.Lib.Common.Primitives.Media;
using OPNX.Lib.Streaming.RTSP.RTP;
using System.Diagnostics;

namespace OPNX.Lib.Streaming.RTSP.Generic
{
    public class RTPPacketMaker(int payloadType, uint syncSource)
    {
        #region Fields
        private const int RTP_HEADER_SIZE = 12;
        private const int FU_A_HEADER_SIZE = 2;
        private const int MAX_RTP_PKT_LENGTH = 1400; //일반적은 MTU값. 환경에 따라 조정 필요

        private ushort sequenceNumber = 0;

        private readonly int payloadType = payloadType;
        private readonly uint syncSource = syncSource;
        #endregion

        #region Constructors
        public RTPPacketMaker()
            : this(96)
        {
        }

        public RTPPacketMaker(int payloadType)
            : this(payloadType, 3113216219)
        {

        }
        #endregion

        #region Private / Protected Methods        

        private RTPHeader GetRTPHeader(int payloadType, int payloadSize, uint timeStamp, int markerBit)
        {
            if (sequenceNumber >= ushort.MaxValue)
                sequenceNumber = 0;
            else
                sequenceNumber += 1;

            return new RTPHeader(payloadType, payloadSize, timeStamp, markerBit, syncSource, sequenceNumber);
        }

        private ReadOnlyMemory<byte> CreateSingleRtpPacket(ReadOnlyMemory<byte> payload, uint timeStamp, int markerBit = 0)
        {
            // RTP 헤더 생성
            byte[] rtpHeaderBytes = GetRTPHeader(payloadType, payload.Length, timeStamp, markerBit).GetBytes();

            // RTP 패킷 크기 계산
            int headerLength = rtpHeaderBytes.Length;
            int packetLength = headerLength + payload.Length;

            // 새로운 배열로 RTP 패킷을 구성
            byte[] packet = new byte[packetLength];

            // Span을 사용하여 복사 성능 최적화
            rtpHeaderBytes.CopyTo(packet.AsSpan(0, headerLength));
            payload.Span.CopyTo(packet.AsSpan(headerLength));

            // ReadOnlyMemory로 감싸서 반환
            return packet;
        }

        private List<ReadOnlyMemory<byte>> CreateMjpegRtpPackets(ReadOnlyMemory<byte> jpegFrame, uint timeStamp, int frameWidth, int frameHeight)
        {
            const int jpegHeaderSize = 8; // RTP JPEG 헤더 최소 크기
            int maxPayload = MAX_RTP_PKT_LENGTH - RTP_HEADER_SIZE - jpegHeaderSize;

            List<ReadOnlyMemory<byte>> packets = new((jpegFrame.Length + maxPayload - 1) / maxPayload);
            int offset = 0;

            int width8 = (frameWidth + 7) / 8;   // 8로 나눈 값, 올림 처리
            int height8 = (frameHeight + 7) / 8; // 8로 나눈 값, 올림 처리

            while (offset < jpegFrame.Length)
            {
                int chunkSize = Math.Min(maxPayload, jpegFrame.Length - offset);
                bool isLastPacket = (offset + chunkSize >= jpegFrame.Length);

                // RTP 헤더 생성
                byte[] rtpHeader = GetRTPHeader(payloadType, chunkSize, timeStamp, isLastPacket ? 1 : 0).GetBytes();

                // RTP 패킷 생성
                byte[] packet = new byte[rtpHeader.Length + jpegHeaderSize + chunkSize];

                // RTP 헤더 복사
                rtpHeader.CopyTo(packet.AsSpan(0, rtpHeader.Length));

                // JPEG RTP 헤더 (RFC 2435)
                packet[rtpHeader.Length + 0] = 0; // Type-specific
                packet[rtpHeader.Length + 1] = (byte)((offset >> 16) & 0xFF); // Fragment offset high
                packet[rtpHeader.Length + 2] = (byte)((offset >> 8) & 0xFF);  // Fragment offset mid
                packet[rtpHeader.Length + 3] = (byte)(offset & 0xFF);         // Fragment offset low
                packet[rtpHeader.Length + 4] = 0;   // Type (0 = default JPEG)
                packet[rtpHeader.Length + 5] = 255; // Q factor (255 = default)
                packet[rtpHeader.Length + 6] = (byte)width8;
                packet[rtpHeader.Length + 7] = (byte)height8;

                // JPEG 데이터 복사
                jpegFrame.Slice(offset, chunkSize).Span.CopyTo(packet.AsSpan(rtpHeader.Length + jpegHeaderSize));

                packets.Add(packet);
                offset += chunkSize;
            }

            return packets;
        }

        private List<ReadOnlyMemory<byte>> CreateH265FragmentedRtpPackets(ReadOnlyMemory<byte> nalu, uint timeStamp)
        {
            List<ReadOnlyMemory<byte>> packets = [];

            // NAL 헤더 2바이트
            byte naluHeader0 = nalu.Span[0];
            byte naluHeader1 = nalu.Span[1];

            int F = (naluHeader0 >> 7) & 0x01;
            int nalType = (naluHeader0 >> 1) & 0x3F;
            int layerId = ((naluHeader0 & 0x01) << 5) | ((naluHeader1 >> 3) & 0x1F);
            int tid = naluHeader1 & 0x07;

            const int FU_TYPE = 49; // Fragmentation Unit
            int maxFragment = MAX_RTP_PKT_LENGTH - RTP_HEADER_SIZE - 3;

            int offset = 2;
            bool firstFragment = true;

            while (offset < nalu.Length)
            {
                int fragmentSize = Math.Min(nalu.Length - offset, maxFragment);
                bool isLast = (offset + fragmentSize) >= nalu.Length;

                byte[] rtpHeader = GetRTPHeader(payloadType, fragmentSize, timeStamp, isLast ? 1 : 0).GetBytes();
                byte[] packet = new byte[rtpHeader.Length + 3 + fragmentSize];

                Buffer.BlockCopy(rtpHeader, 0, packet, 0, rtpHeader.Length);

                int idx = rtpHeader.Length;

                // FU Indicator
                packet[idx++] = (byte)((F << 7) | (FU_TYPE << 1) | ((layerId >> 5) & 0x01));
                packet[idx++] = (byte)(((layerId & 0x1F) << 3) | (tid & 0x07));

                // FU Header (S, E, FuType)
                byte fuHeader = (byte)(nalType & 0x3F);
                if (firstFragment) fuHeader |= 0x80;
                if (isLast) fuHeader |= 0x40;
                packet[idx++] = fuHeader;

                nalu.Slice(offset, fragmentSize).CopyTo(packet.AsMemory(idx));

                packets.Add(packet);
                offset += fragmentSize;
                firstFragment = false;
            }

            return packets;
        }

        private List<ReadOnlyMemory<byte>> CreateH264FragmentedRtpPackets(ReadOnlyMemory<byte> nalu, uint timeStamp)
        {
            List<ReadOnlyMemory<byte>> packets = new(nalu.Length / (MAX_RTP_PKT_LENGTH - RTP_HEADER_SIZE - FU_A_HEADER_SIZE) + 1);

            int offset = 1; // NALU 헤더 건너뛰기
            byte naluHeader = nalu.Span[0];
            int maxSize = MAX_RTP_PKT_LENGTH - RTP_HEADER_SIZE - FU_A_HEADER_SIZE;
            bool firstFragment = true;

            while (offset < nalu.Length - 1)
            {
                int fragmentSize = Math.Min(nalu.Length - offset, maxSize);
                bool isLastFragment = (offset + fragmentSize >= nalu.Length);

                // RTP 헤더 생성 및 바이트 변환
                byte[] rtpHeaderBytes = GetRTPHeader(payloadType, fragmentSize, timeStamp, isLastFragment ? 1 : 0).GetBytes();
                int headerLength = rtpHeaderBytes.Length;

                // 패킷 크기 계산
                byte[] packet = new byte[headerLength + FU_A_HEADER_SIZE + fragmentSize];

                // Span을 활용한 복사 성능 최적화
                rtpHeaderBytes.CopyTo(packet.AsSpan(0, headerLength));

                // FU-A 헤더 설정
                packet[headerLength] = (byte)(naluHeader & 0xE0); // F 및 NRI 비트 복사
                packet[headerLength] |= 28; // FU-A 타입 (28)

                // FU-A 조각 유형 설정
                if (firstFragment)
                {
                    packet[headerLength + 1] = (byte)(0x80 | (naluHeader & 0x1F)); // 첫 번째 조각 (Start bit = 1)
                    firstFragment = false;
                }
                else if (isLastFragment)
                {
                    packet[headerLength + 1] = (byte)(0x40 | (naluHeader & 0x1F)); // 마지막 조각 (End bit = 1)
                }
                else
                {
                    packet[headerLength + 1] = (byte)(naluHeader & 0x1F); // 중간 조각
                }

                // NALU 데이터를 패킷에 복사
                nalu.Slice(offset, fragmentSize).Span.CopyTo(packet.AsSpan(headerLength + FU_A_HEADER_SIZE));

                // 패킷을 ReadOnlyMemory로 추가
                packets.Add(packet);

                offset += fragmentSize;
            }

            return packets;
        }


        private static byte[] PrepareAacPayload(ReadOnlyMemory<byte> aacData)
        {
            // AAC RTP 페이로드 형식 (RFC 3640)
            // AU-Header-Length (16bits) + AU-Header + AAC data
            var data = aacData.ToArray();
            byte[] payload = new byte[4 + data.Length];

            // AU-Header-Length = 16 bits (2 bytes)
            payload[0] = 0x00;
            payload[1] = 0x10;

            // AU-Header: AU-size (13bits) + AU-Index (3bits)
            ushort auSize = (ushort)(data.Length << 3);
            payload[2] = (byte)(auSize >> 8);
            payload[3] = (byte)(auSize & 0xFF);

            // AAC 데이터 복사
            Array.Copy(data, 0, payload, 4, data.Length);

            return payload;
        }

        private List<ReadOnlyMemory<byte>> ProcessVideoStream(CodecId codec, uint timeStamp, ReadOnlyMemory<byte> videoStream, int width = 0, int height = 0)
        {
            var rtpPackets = new List<ReadOnlyMemory<byte>>();
            List<ReadOnlyMemory<byte>> units = StreamExtractor.StreamSplit(codec, videoStream);

            for (int i = 0; i < units.Count; i++)
            {
                ReadOnlyMemory<byte> unit = units[i];
                bool isLastUnit = (i == units.Count - 1);

                switch (codec)
                {
                    case CodecId.H264:
                        {
                            int maxSingleSize = MAX_RTP_PKT_LENGTH - RTP_HEADER_SIZE - FU_A_HEADER_SIZE;

                            if (unit.Length <= maxSingleSize)
                            {
                                // 단일 패킷: 0
                                var rtpPacket = CreateSingleRtpPacket(unit, timeStamp, 0);
                                if (!rtpPacket.IsEmpty)
                                {
                                    rtpPackets.Add(rtpPacket);
                                }
                            }
                            else
                            {
                                // Fragmentation: 내부에서 마커 비트 처리
                                var fuRTPPackets = CreateH264FragmentedRtpPackets(unit, timeStamp);
                                if (fuRTPPackets != null)
                                {
                                    rtpPackets.AddRange(fuRTPPackets);
                                }
                            }
                        }
                        break;

                    case CodecId.H265:
                        {
                            byte naluHeader0 = unit.Span[0];
                            int nalType = (naluHeader0 >> 1) & 0x3F;
                            int maxSingleSize = MAX_RTP_PKT_LENGTH - RTP_HEADER_SIZE;

                            // VPS/SPS/PPS는 무조건 단일 패킷 (마커 비트 0)
                            if (nalType >= 32 && nalType <= 34)
                            {
                                var rtpPacket = CreateSingleRtpPacket(unit, timeStamp, 0);
                                if (!rtpPacket.IsEmpty)
                                {
                                    rtpPackets.Add(rtpPacket);
                                }
                            }
                            else if (unit.Length <= maxSingleSize)
                            {
                                // 일반 NALU 단일 패킷: 마지막만 마커 비트 1
                                var rtpPacket = CreateSingleRtpPacket(unit, timeStamp, isLastUnit ? 1 : 0);
                                if (!rtpPacket.IsEmpty)
                                {
                                    rtpPackets.Add(rtpPacket);
                                }
                            }
                            else
                            {
                                // Fragmentation: 내부에서 마커 비트 처리
                                var fuRTPPackets = CreateH265FragmentedRtpPackets(unit, timeStamp);
                                if (fuRTPPackets != null)
                                {
                                    rtpPackets.AddRange(fuRTPPackets);
                                }
                            }
                        }
                        break;

                    case CodecId.MJPEG:
                        {
                            var fuRTPPackets = CreateMjpegRtpPackets(unit, timeStamp, width, height);
                            if (fuRTPPackets != null)
                            {
                                rtpPackets.AddRange(fuRTPPackets);
                            }
                        }
                        break;
                    case CodecId.AV1:
                        {
                            var obuList = StreamExtractor.StreamSplit(codec, videoStream); // OBU 단위로 분리됨 가정
                            int maxPayloadSize = MAX_RTP_PKT_LENGTH - RTP_HEADER_SIZE - 1; // Aggregation Header(1바이트)

                            foreach (var obu in obuList)
                            {
                                if (obu.Length <= maxPayloadSize)
                                {
                                    // 단일 RTP 패킷
                                    byte[] aggregationHeader = [0];// new byte[1];
                                    aggregationHeader[0] = 0x00; // Z=0, N=0 (Aggregation start)

                                    byte[] rtpHeader = GetRTPHeader(payloadType, obu.Length, timeStamp, 1).GetBytes();
                                    byte[] packet = new byte[rtpHeader.Length + aggregationHeader.Length + obu.Length];

                                    rtpHeader.CopyTo(packet.AsSpan(0, rtpHeader.Length));
                                    aggregationHeader.CopyTo(packet.AsSpan(rtpHeader.Length));
                                    obu.Span.CopyTo(packet.AsSpan(rtpHeader.Length + aggregationHeader.Length));

                                    rtpPackets.Add(packet);
                                }
                                else
                                {
                                    // Fragmentation: 큰 OBU는 여러 RTP 패킷으로 분할
                                    int offset = 0;
                                    bool first = true;

                                    while (offset < obu.Length)
                                    {
                                        int chunkSize = Math.Min(maxPayloadSize, obu.Length - offset);
                                        bool last = (offset + chunkSize) >= obu.Length;

                                        byte[] aggregationHeader = new byte[1];
                                        byte Z = (byte)(first ? 0 : 1); // Z=1이면 이전 OBU fragment 존재
                                        byte N = (byte)(last ? 1 : 0);  // N=1이면 마지막 fragment
                                        aggregationHeader[0] = (byte)((Z << 1) | N);

                                        byte[] rtpHeader = GetRTPHeader(payloadType, chunkSize, timeStamp, last ? 1 : 0).GetBytes();
                                        byte[] packet = new byte[rtpHeader.Length + aggregationHeader.Length + chunkSize];

                                        rtpHeader.CopyTo(packet.AsSpan(0, rtpHeader.Length));
                                        aggregationHeader.CopyTo(packet.AsSpan(rtpHeader.Length));
                                        obu.Slice(offset, chunkSize).Span.CopyTo(packet.AsSpan(rtpHeader.Length + 1));

                                        rtpPackets.Add(packet);

                                        offset += chunkSize;
                                        first = false;
                                    }
                                }
                            }
                        }
                        break;
                }
            }

            return rtpPackets;
        }

        private List<ReadOnlyMemory<byte>> ProcessAudioStream(CodecId codec, uint timeStamp, ReadOnlyMemory<byte> audioStream)
        {
            var rtpPackets = new List<ReadOnlyMemory<byte>>();

            ReadOnlyMemory<byte> payload = codec switch
            {
                CodecId.AAC => PrepareAacPayload(audioStream),
                _ => audioStream
            };
            // 오디오는 일반적으로 작은 크기이므로 분할하지 않음
            if (payload.Length <= MAX_RTP_PKT_LENGTH - RTP_HEADER_SIZE)
            {
                var rtpPacket = CreateSingleRtpPacket(payload, timeStamp, 0); // 오디오도 마커 비트 설정
                if (!rtpPacket.IsEmpty)
                {
                    rtpPackets.Add(rtpPacket);
                }
            }
            else
            {
                // 오디오 데이터가 너무 큰 경우 청크로 분할
                int offset = 0;
                int maxPayloadSize = MAX_RTP_PKT_LENGTH - RTP_HEADER_SIZE;

                while (offset < payload.Length)
                {
                    int chunkSize = Math.Min(payload.Length - offset, maxPayloadSize);
                    bool isLastChunk = (offset + chunkSize >= payload.Length);

                    var chunk = payload.Slice(offset, chunkSize);
                    var rtpPacket = CreateSingleRtpPacket(chunk, timeStamp, isLastChunk ? 1 : 0);

                    if (!rtpPacket.IsEmpty)
                    {
                        rtpPackets.Add(rtpPacket);
                    }

                    offset += chunkSize;
                }
            }

            return rtpPackets;
        }

        //private List<ReadOnlyMemory<byte>> CreateFragmentedRtpPackets(ReadOnlyMemory<byte> nalu, uint timeStamp)
        //{
        //    List<ReadOnlyMemory<byte>> packets = new List<ReadOnlyMemory<byte>>();

        //    int offset = 1; // NALU 헤더 건너뛰기
        //    byte naluHeader = nalu.Span[0];
        //    int maxSize = MAX_RTP_PKT_LENGTH - RTP_HEADER_SIZE - FU_A_HEADER_SIZE;
        //    bool firstFragment = true;

        //    while (offset < nalu.Length)
        //    {
        //        int fragmentSize = Math.Min(nalu.Length - offset, maxSize);

        //        // RTP 헤더 생성
        //        RTPHeader rtpHeader = GetRTPHeader(payloadType, fragmentSize, timeStamp, offset + fragmentSize >= nalu.Length ? 1 : 0);

        //        byte[] header = rtpHeader.GetBytes();
        //        byte[] packet = new byte[header.Length + FU_A_HEADER_SIZE + fragmentSize];

        //        // FU-A 헤더와 FU-A 인디케이터 추가
        //        packet[RTP_HEADER_SIZE] = (byte)(naluHeader & 0xE0); // F 및 NRI 비트 복사
        //        packet[RTP_HEADER_SIZE] |= 28; // FU-A 타입은 28

        //        if (firstFragment)
        //        {
        //            packet[RTP_HEADER_SIZE + 1] = (byte)(0x80 | (naluHeader & 0x1F)); // 첫 번째 조각
        //            firstFragment = false;
        //        }
        //        else if (offset + fragmentSize >= nalu.Length)
        //        {
        //            packet[RTP_HEADER_SIZE + 1] = (byte)(0x40 | (naluHeader & 0x1F)); // 마지막 조각
        //        }
        //        else
        //        {
        //            packet[RTP_HEADER_SIZE + 1] = (byte)(naluHeader & 0x1F); // 중간 조각
        //        }

        //        // RTP 헤더를 패킷에 복사
        //        Array.Copy(header, 0, packet, 0, header.Length);
        //        // NALU 데이터를 패킷에 복사
        //        nalu.Slice(offset, fragmentSize).Span.CopyTo(packet.AsSpan().Slice(header.Length + FU_A_HEADER_SIZE));

        //        // 패킷을 ReadOnlyMemory로 추가
        //        packets.Add(new ReadOnlyMemory<byte>(packet));

        //        offset += fragmentSize;
        //    }

        //    return packets;
        //}
        #endregion

        #region Public Methods
        public List<ReadOnlyMemory<byte>> GetRTPPackets(CodecId codec, uint timeStamp, ReadOnlyMemory<byte> mediaStream, int width = 0, int height = 0)
        {
            var rtpPackets = new List<ReadOnlyMemory<byte>>();
            try
            {
                if (CodecIdExtensions.IsVideo(codec))
                {
                    rtpPackets = ProcessVideoStream(codec, timeStamp, mediaStream, width, height);
                }
                else if (CodecIdExtensions.IsAudio(codec))
                {
                    rtpPackets = ProcessAudioStream(codec, timeStamp, mediaStream);
                }
                else
                {
                    Debug.WriteLine($"Unsupported codec: {codec}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            return rtpPackets;
        }
        #endregion
    }
}
