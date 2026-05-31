using DataChannelDotnet;
using DataChannelDotnet.Bindings;
using DataChannelDotnet.Data;
using DataChannelDotnet.Impl;
using System.Buffers.Binary;

namespace OPNX.Lib.Streaming.WebRTC.DataChannel
{
    public sealed class DataChannelPeerConnection : IDisposable
    {
        private const int H264PayloadType = 100;
        private const int PcmuPayloadType = 0;

        private readonly IRtcPeerConnection peerConnection;
        private readonly IRtcTrack videoTrack;
        private readonly IRtcTrack audioTrack;
        private readonly uint videoSsrc = CreateSsrc();
        private readonly uint audioSsrc = CreateSsrc();

        private ushort videoSequenceNumber;
        private ushort audioSequenceNumber;

        public int VideoSourceID = int.MinValue;

        public Guid SessionID { get; } = Guid.NewGuid();
        public rtcState ConnectionState => peerConnection.ConnectionState;
        public bool IsConnected => ConnectionState == rtcState.RTC_CONNECTED;
        public string? RemoteDescription => peerConnection.RemoteDescription;

        public event Action<DataChannelPeerConnection, RtcDescription>? LocalDescriptionCreated;
        public event Action<DataChannelPeerConnection, RtcCandidate>? LocalCandidateCreated;
        public event Action<DataChannelPeerConnection, rtcState>? ConnectionStateChanged;

        public DataChannelPeerConnection(RtcPeerConfiguration? configuration = null)
        {
            peerConnection = new RtcPeerConnection(configuration ?? CreateDefaultConfiguration());

            peerConnection.OnLocalDescriptionSafe += (_, description) =>
                LocalDescriptionCreated?.Invoke(this, description);

            peerConnection.OnCandidateSafe += (_, candidate) =>
                LocalCandidateCreated?.Invoke(this, candidate);

            peerConnection.OnConnectionStateChange += (_, state) =>
                ConnectionStateChanged?.Invoke(this, state);

            videoTrack = CreateVideoTrack();
            audioTrack = CreateAudioTrack();

            videoTrack.AddRtcpSrReporter();
            videoTrack.AddRtcpNackResponder(128);
            audioTrack.AddRtcpSrReporter();
        }

        public async Task<RtcDescription> CreateOfferAsync(TimeSpan? timeout = null)
        {
            TaskCompletionSource<RtcDescription> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(DataChannelPeerConnection _, RtcDescription description)
            {
                if (description.Type == RtcDescriptionType.Offer)
                    completion.TrySetResult(description);
            }

            LocalDescriptionCreated += Handler;

            try
            {
                peerConnection.SetLocalDescription(RtcDescriptionType.Offer);

                Task delay = Task.Delay(timeout ?? TimeSpan.FromSeconds(5));
                Task completed = await Task.WhenAny(completion.Task, delay).ConfigureAwait(false);

                if (completed != completion.Task)
                    throw new TimeoutException("Timed out while waiting for a local WebRTC offer.");

                return await completion.Task.ConfigureAwait(false);
            }
            finally
            {
                LocalDescriptionCreated -= Handler;
            }
        }

        public void SetRemoteAnswer(string sdp)
        {
            peerConnection.SetRemoteDescription(new RtcDescription
            {
                Type = RtcDescriptionType.Answer,
                Sdp = sdp
            });
        }

        public void AddRemoteCandidate(string candidate, string mid)
        {
            peerConnection.AddRemoteCandidate(new RtcCandidate
            {
                Content = candidate,
                Mid = mid
            });
        }

        public void SendRtpVideoData(int payloadType, ReadOnlySpan<byte> payload, uint timestamp, int markerBit)
        {
            SendRtpPacket(videoTrack, payloadType, payload, timestamp, markerBit != 0, videoSsrc, ref videoSequenceNumber);
        }

        public void SendRtpAudioData(int payloadType, ReadOnlySpan<byte> payload, uint timestamp, int markerBit)
        {
            SendRtpPacket(audioTrack, payloadType, payload, timestamp, markerBit != 0, audioSsrc, ref audioSequenceNumber);
        }

        private IRtcTrack CreateVideoTrack()
        {
            return peerConnection.CreateTrack(new RtcCreateTrackArgs
            {
                Direction = rtcDirection.RTC_DIRECTION_SENDONLY,
                Codec = rtcCodec.RTC_CODEC_H264,
                PayloadType = H264PayloadType,
                Ssrc = videoSsrc,
                Mid = "video",
                Name = "video",
                Msid = SessionID.ToString("N"),
                TrackId = "video0",
                Profile = "42e01f"
            });
        }

        private IRtcTrack CreateAudioTrack()
        {
            return peerConnection.CreateTrack(new RtcCreateTrackArgs
            {
                Direction = rtcDirection.RTC_DIRECTION_SENDONLY,
                Codec = rtcCodec.RTC_CODEC_PCMU,
                PayloadType = PcmuPayloadType,
                Ssrc = audioSsrc,
                Mid = "audio",
                Name = "audio",
                Msid = SessionID.ToString("N"),
                TrackId = "audio0"
            });
        }

        private static void SendRtpPacket(IRtcTrack track, int payloadType, ReadOnlySpan<byte> payload, uint timestamp, bool marker, uint ssrc, ref ushort sequenceNumber)
        {
            byte[] packet = new byte[12 + payload.Length];
            Span<byte> header = packet.AsSpan(0, 12);

            header[0] = 0x80;
            header[1] = (byte)(payloadType & 0x7F);

            if (marker)
                header[1] |= 0x80;

            BinaryPrimitives.WriteUInt16BigEndian(header[2..4], sequenceNumber++);
            BinaryPrimitives.WriteUInt32BigEndian(header[4..8], timestamp);
            BinaryPrimitives.WriteUInt32BigEndian(header[8..12], ssrc);

            payload.CopyTo(packet.AsSpan(12));
            track.Write(packet);
        }

        private static RtcPeerConfiguration CreateDefaultConfiguration()
        {
            return new RtcPeerConfiguration
            {
                IceServers = ["stun:stun.l.google.com:19302"]
            };
        }

        private static uint CreateSsrc()
        {
            Span<byte> bytes = stackalloc byte[4];
            Random.Shared.NextBytes(bytes);
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        public void Dispose()
        {
            audioTrack.Dispose();
            videoTrack.Dispose();
            peerConnection.Dispose();
        }
    }
}
