using DataChannelDotnet;
using DataChannelDotnet.Bindings;
using DataChannelDotnet.Data;
using DataChannelDotnet.Impl;
using OPNX.Lib.Common.Primitives.Media;
using System.Buffers.Binary;

namespace OPNX.Lib.Streaming.WebRTC.DataChannel
{
    public sealed class DataChannelPeerConnection : IDisposable
    {
        private const int H264PayloadType = (int)VideoPayloadType.H264;
        private const int PcmuPayloadType = 0;

        private readonly RtcPeerConnection _peerConnection;
        private readonly IRtcTrack _videoTrack;
        private readonly IRtcTrack _audioTrack;
        private readonly uint _videoSsrc = CreateSsrc();
        private readonly uint _audioSsrc = CreateSsrc();

        private ushort _videoSequenceNumber;
        private ushort _audioSequenceNumber;

        private readonly Lock _connectionLock = new();
        private bool _disposed;
        private readonly List<RtcCandidate> _remoteCandidates = [];

        public int VideoSourceID = int.MinValue;

        public Guid SessionID { get; }
        public rtcState ConnectionState
        {
            get
            {
                lock (_connectionLock)
                    return _disposed ? rtcState.RTC_CLOSED : _peerConnection.ConnectionState;
            }
        }
        public bool IsConnected => ConnectionState == rtcState.RTC_CONNECTED;
        public string? RemoteDescription => _peerConnection.RemoteDescription;

        public event Action<DataChannelPeerConnection, RtcDescription>? LocalDescriptionCreated;
        public event Action<DataChannelPeerConnection, RtcCandidate>? LocalCandidateCreated;
        public event Action<DataChannelPeerConnection, rtcState>? ConnectionStateChanged;

        public DataChannelPeerConnection(RtcPeerConfiguration? configuration = null, Guid? sessionID = null)
        {
            SessionID = sessionID ?? Guid.NewGuid();
            _peerConnection = new RtcPeerConnection(configuration ?? CreateDefaultConfiguration());

            _peerConnection.OnLocalDescriptionSafe += (_, description) =>
                LocalDescriptionCreated?.Invoke(this, description);

            _peerConnection.OnCandidateSafe += (_, candidate) =>
                LocalCandidateCreated?.Invoke(this, candidate);

            _peerConnection.OnConnectionStateChange += (_, state) =>
                ConnectionStateChanged?.Invoke(this, state);

            try
            {
                _videoTrack = CreateVideoTrack();
                _audioTrack = CreateAudioTrack();
            }
            catch
            {
                _videoTrack?.Dispose();
                _peerConnection.Dispose();
                throw;
            }

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
                lock (_connectionLock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _peerConnection.SetLocalDescription(RtcDescriptionType.Offer);
                }

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
            lock (_connectionLock)
            {
                if (_disposed)
                    return;

                _peerConnection.SetRemoteDescription(new RtcDescription { Type = RtcDescriptionType.Answer, Sdp = sdp });
                foreach (var candidate in _remoteCandidates)
                    _peerConnection.AddRemoteCandidate(candidate);
                _remoteCandidates.Clear();
            }
        }

        public void AddRemoteCandidate(string candidate, string mid)
        {
            lock (_connectionLock)
            {
                if (_disposed)
                    return;

                RtcCandidate remoteCandidate = new() { Content = candidate, Mid = mid };
                if (_peerConnection.RemoteDescription == null)
                    _remoteCandidates.Add(remoteCandidate);
                else
                    _peerConnection.AddRemoteCandidate(remoteCandidate);
            }
        }

        public void SendRtpVideoData(int payloadType, ReadOnlySpan<byte> payload, uint timestamp, int markerBit)
        {
            lock (_connectionLock)
            {
                if (!_disposed && _videoTrack.IsOpen && payloadType == H264PayloadType)
                    SendRtpPacket(_videoTrack, H264PayloadType, payload, timestamp, markerBit != 0, _videoSsrc, ref _videoSequenceNumber);
            }
        }

        public void SendRtpAudioData(int payloadType, ReadOnlySpan<byte> payload, uint timestamp, int markerBit)
        {
            lock (_connectionLock)
            {
                if (!_disposed && _audioTrack.IsOpen && payloadType == PcmuPayloadType)
                    SendRtpPacket(_audioTrack, PcmuPayloadType, payload, timestamp, markerBit != 0, _audioSsrc, ref _audioSequenceNumber);
            }
        }

        private IRtcTrack CreateVideoTrack()
        {
            return _peerConnection.CreateTrack(new RtcCreateTrackArgs
            {
                Direction = rtcDirection.RTC_DIRECTION_SENDONLY,
                Codec = rtcCodec.RTC_CODEC_H264,
                PayloadType = H264PayloadType,
                Ssrc = _videoSsrc,
                Mid = "video",
                Name = "video",
                Msid = SessionID.ToString("N"),
                TrackId = "video0",
                Profile = "42e01f"
            });
        }

        private IRtcTrack CreateAudioTrack()
        {
            return _peerConnection.CreateTrack(new RtcCreateTrackArgs
            {
                Direction = rtcDirection.RTC_DIRECTION_SENDONLY,
                Codec = rtcCodec.RTC_CODEC_PCMU,
                PayloadType = PcmuPayloadType,
                Ssrc = _audioSsrc,
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
                IceServers = ["stun:stun.l.google.com:19302"],
                DisableAutoNegotiation = true
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
            lock (_connectionLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            _audioTrack.Dispose();
            _videoTrack.Dispose();
            _peerConnection.Dispose();
        }
    }
}
