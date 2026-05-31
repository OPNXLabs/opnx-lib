using OPNX.Lib.Streaming.RTSP.Commons;
using OPNX.Lib.Streaming.RTSP.Commons.Interfaces;
using OPNX.Lib.Streaming.RTSP.Messages;
using OPNX.Lib.Streaming.RTSP.Onvif;
using OPNX.Lib.Streaming.RTSP.RTCP;
using OPNX.Lib.Streaming.RTSP.RTP;
using OPNX.Lib.Streaming.RTSP.Sdp;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Text;

namespace OPNX.Lib.Streaming.RTSP
{
    public class RTSPClient : IRTSPClient
    {
        #region Fields
        private class KeepAliveContext
        {
        }
        private readonly KeepAliveContext keepAliveContext = new();

        private enum RTP_TRANSPORT
        {
            UDP,
            TCP,
            MULTICAST
        };
        private enum MEDIA_REQUEST
        {
            VIDEO_ONLY = 1,
            AUDIO_ONLY = 2,
            VIDEO_AND_AUDIO = VIDEO_ONLY | AUDIO_ONLY
        };

        private enum RTSP_STATUS
        {
            WaitingToConnect,
            Connecting,
            ConnectFailed,
            Connected
        };

        private IRtspTransport? _rtspSocket = null; // RTSP connection
        private volatile RTSP_STATUS rtspSocketStatus = RTSP_STATUS.WaitingToConnect;
        private RTSPListener? _rtspClient = null;   // this wraps around a the RTSP tcp_socket stream
        private RTP_TRANSPORT _rtpTransport = RTP_TRANSPORT.UDP; // Mode, either RTP over UDP or RTP over TCP using the RTSP socket

        private IRtpTransport? videoRtpTransport;
        private IRtpTransport? audioRtpTransport;

        private Authentication? _authentication;
        private NetworkCredential? _credentials = new();

        private readonly uint _ssrc = 12345;
        private Uri? _uri = null;
        private string _session = "";             // RTSP Session

        private bool clientWantsVideo = false; // Client wants to receive Video
        private bool clientWantsAudio = false; // Client wants to receive Audio        

        private bool _ready = false;    // Helper to avoid sending any method before setup has been completed.
                                        // If this will happen, all the chain will break (and goodbye to connection, without errors)...

        private readonly List<Uri> video_uris = [];
        private int _videoClockRate = 90000;

        private readonly List<Uri> audio_uris = [];

        private bool _serverSupportsGetParameter = false; // Used with RTSP keepalive        
        private readonly System.Timers.Timer? _keepaliveTimer = null; // Used with RTSP keepalive

        private readonly Dictionary<int, string> videoPayloadMapping = [];
        private readonly Dictionary<int, string> audioPayloadMapping = [];

        private readonly Dictionary<int, IPayloadProcessor> videoPayloadProcessors = [];
        private readonly Dictionary<int, IPayloadProcessor> audioPayloadProcessors = [];

        private readonly Queue<RtspRequestSetup> _setupMessages = new();

        private readonly VideoSource _videoSource;

        private readonly ILogger _logger = Log.ForContext<RTSPClient>();// LogManager.GetLogger("RtspClient");        

        //private DateTime _prevReceivedFrameTime = DateTime.MinValue;
        //private const int UPDATE_INTERVAL_MS = 1000; // 1초를 밀리초로 변환
        private DateTime _prevCalcTime = DateTime.MinValue;
        private uint _prevTimeStamp = 0;
        private int _receivedFrameCount = 0;
        private long _receivedBytes = 0; // 총 수신 데이터 양 (바이트 단위)
        private double _fps = 30.0f;
        private double _bitrate = 0.0; // 비트레이트 (bps)         

        /// <summary>
        /// If true, the client must send an "onvif-replay" header on every play request.
        /// </summary>
        bool _playbackSession = false;
        #endregion

        #region Constructors
        public RTSPClient(VideoSource videoSource)
        {
            _videoSource = videoSource;

            _keepaliveTimer = new System.Timers.Timer();
            _keepaliveTimer.Elapsed += SendKeepAlive;
            _keepaliveTimer.Interval = 20 * 1000;
        }
        #endregion

        #region Events
        public event EventHandler<StreamStartedEventArgs>? Started;
        public event EventHandler<StreamStoppedEventArgs>? Stopped;
        public event EventHandler<StreamConfigurationDataEventArgs>? StreamConfigured;
        public event EventHandler<RTPDataEventArgs>? RtpPacketReceived;
        public event EventHandler<NalUnitDataEventArgs>? NalUnitExtracted;
        #endregion

        #region Properties
        //public byte[] SdpData { get; private set; }        

        public VideoSource VideoSource => _videoSource;
        public int EntityID => _videoSource.EntityID;
        public string? URL => _videoSource.RtspURL;

        public bool IsConnected => rtspSocketStatus == RTSP_STATUS.Connected;

        public string UserName => _credentials?.UserName ?? string.Empty;
        public string Password => _credentials?.Password ?? string.Empty;

        public double FPS => _fps;

        public double BitRate => _bitrate;

        string? _setupPreferredVideoRtpMap = null;
        string? _setupPreferredAudioRtpMap = null;

        #endregion

        #region Public Methods
        public void Start()
        {
            _logger.Information($"Start rtsp client for source {EntityID}");

            Connect(_videoSource, _videoSource.UseTCP ? RTP_TRANSPORT.TCP : RTP_TRANSPORT.UDP);
        }

        public async Task StartAsync()
        {
            _logger.Information($"Start rtsp client for source {EntityID}");

            await ConnectAsync(_videoSource, _videoSource.UseTCP ? RTP_TRANSPORT.TCP : RTP_TRANSPORT.UDP);
        }

        // return true if this connection failed, or if it connected but is no longer connected.
        public bool StreamingFinished() => rtspSocketStatus switch
        {
            RTSP_STATUS.ConnectFailed => true,
            RTSP_STATUS.Connected when !(_rtspSocket?.Connected ?? false) => true,
            _ => false,
        };

        public bool Pause()
        {
            if (_rtspSocket is null || _uri is null)
            {
                _logger.Information("Not Connected");
                return false;
            }

            if (!_ready) { return false; }

            // Send PAUSE
            RtspRequest pause_message = new RtspRequestPause
            {
                RtspUri = _uri,
                Session = _session
            };
            pause_message.AddAuthorization(_authentication, _uri, _rtspSocket.NextCommandIndex());
            _rtspClient?.SendMessage(pause_message);
            return true;
        }

        public bool Play()
        {
            if (_rtspSocket is null || _uri is null)
            {
                _logger.Information("Not Connected");
                return false;
            }

            if (!_ready) { return false; }

            // Send PLAY
            var playMessage = new RtspRequestPlay
            {
                RtspUri = _uri,
                Session = _session
            };
            playMessage.AddAuthorization(_authentication, _uri, _rtspSocket.NextCommandIndex());

            //// Need for old sony camera SNC-CS20
            playMessage.Headers.Add("range", "npt=0.000-");
            if (_playbackSession)
            {
                playMessage.AddRequireOnvifRequest();
                playMessage.AddRateControlOnvifRequest(false);
            }
            _rtspClient?.SendMessage(playMessage);

            return true;
        }

        /// <summary>
        /// Generate a Play request from required time
        /// </summary>
        /// <param name="seekTime">The playback time to start from</param>
        /// <param name="speed">Speed information (1.0 means normal speed, -1.0 backward speed), other values >1.0 and <-1.0 allow a different speed</param>
        public bool Play(DateTime seekTime, double speed = 1.0)
        {
            if (_rtspSocket is null || _uri is null)
            {
                _logger.Information("Not connected");
                return false;
            }
            if (!_ready) return false;

            var playMessage = new RtspRequestPlay
            {
                RtspUri = _uri,
                Session = _session,
            };
            playMessage.AddAuthorization(_authentication, _uri, _rtspSocket.NextCommandIndex());
            playMessage.AddPlayback(seekTime, speed);
            if (_playbackSession)
            {
                playMessage.AddRequireOnvifRequest();
                playMessage.AddRateControlOnvifRequest(false);
            }

            _rtspClient?.SendMessage(playMessage);
            return true;
        }

        /// <summary>
        /// Generate a Play request with a time range
        /// </summary>
        /// <param name="seekTimeFrom">Starting time for playback</param>
        /// <param name="seekTimeTo">Ending time for playback</param>
        /// <param name="speed">Speed information (1.0 means normal speed, -1.0 backward speed), other values >1.0 and <-1.0 allow a different speed</param>
        /// <exception cref="InvalidOperationException"></exception>
        public bool Play(DateTime seekTimeFrom, DateTime seekTimeTo, double speed = 1.0)
        {
            if (_rtspSocket is null || _uri is null)
            {
                _logger.Information("Not connected");
                return false;
            }
            if (!_ready) return false;

            if (seekTimeFrom > seekTimeTo)
            {
                throw new ArgumentOutOfRangeException(nameof(seekTimeFrom),
                    "Starting seek cannot be major than ending seek.");
            }

            var playMessage = new RtspRequestPlay
            {
                RtspUri = _uri,
                Session = _session,
            };

            playMessage.AddAuthorization(_authentication, _uri, _rtspSocket.NextCommandIndex());
            playMessage.AddPlayback(seekTimeFrom, seekTimeTo, speed);
            if (_playbackSession)
            {
                playMessage.AddRequireOnvifRequest();
                playMessage.AddRateControlOnvifRequest(false);
            }

            _rtspClient?.SendMessage(playMessage);
            return true;
        }

        public bool Stop(RTSPClientStopReason reason)
        {
            if (_rtspSocket is null || _uri is null)
            {
                _logger.Information("Not connected");
                return false;
            }

            if (!_ready) return false;

            if (_rtspClient != null && _session != null)
            {
                var teardownMessage = new RtspRequestTeardown
                {
                    RtspUri = _uri,
                    Session = _session
                };
                teardownMessage.AddAuthorization(_authentication, _uri, _rtspSocket?.NextCommandIndex() ?? 0);
                _rtspClient.SendMessage(teardownMessage);
            }

            _keepaliveTimer?.Stop();

            foreach (var transport in new[] { videoRtpTransport, audioRtpTransport })
            {
                if (transport != null)
                {
                    try
                    {
                        transport.Stop();
                        transport.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Transport disposal failed: {ex.Message}");
                    }
                }
            }
            videoRtpTransport = null;
            audioRtpTransport = null;

            foreach (var videoPayloadProcessor in videoPayloadProcessors.Values)
            {
                try
                {
                    videoPayloadProcessor?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Video payload processor dispose failed");
                }
            }

            foreach (var audioPayloadProcessor in audioPayloadProcessors.Values)
            {
                try
                {
                    audioPayloadProcessor?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Audio payload processor dispose failed");
                }
            }

            videoPayloadProcessors.Clear();
            audioPayloadProcessors.Clear();

            videoPayloadMapping.Clear();
            audioPayloadMapping.Clear();

            video_uris.Clear();
            audio_uris.Clear();

            _rtspClient?.Stop();
            _rtspClient?.Dispose();
            _rtspClient = null;

            _authentication = null;
            _credentials = null;

            _videoClockRate = 90000;

            _setupMessages.Clear();

            OnStopped(reason);

            return true;
        }

        public async Task<bool> StopAsync(RTSPClientStopReason reason)
        {
            var rtspClient = _rtspClient;
            if (_rtspSocket is null || _uri is null || rtspClient is null)
            {
                _logger.Information("Not connected");
                return false;
            }
            if (!_ready) { return false; }

            var teardownMessage = new RtspRequestTeardown
            {
                RtspUri = _uri,
                Session = _session
            };
            teardownMessage.AddAuthorization(_authentication, _uri, _rtspSocket?.NextCommandIndex() ?? 0);
            await rtspClient.SendMessageAsync(teardownMessage);

            _keepaliveTimer?.Stop();

            foreach (var transport in new[] { videoRtpTransport, audioRtpTransport })
            {
                try
                {
                    transport?.Stop();
                    transport?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Transport disposal failed: {ex.Message}");
                }
            }

            videoRtpTransport = null;
            audioRtpTransport = null;

            foreach (var videoPayloadProcessor in videoPayloadProcessors.Values)
            {
                try
                {
                    videoPayloadProcessor?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Video payload processor dispose failed");
                }
            }

            foreach (var audioPayloadProcessor in audioPayloadProcessors.Values)
            {
                try
                {
                    audioPayloadProcessor?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Audio payload processor dispose failed");
                }
            }

            videoPayloadProcessors.Clear();
            audioPayloadProcessors.Clear();

            videoPayloadMapping.Clear();
            audioPayloadMapping.Clear();

            video_uris.Clear();
            audio_uris.Clear();

            _rtspClient?.Stop();
            _rtspClient?.Dispose();
            _rtspClient = null;

            _authentication = null;
            _credentials = null;

            _videoClockRate = 90000;

            _setupMessages.Clear();

            OnStopped(reason);

            return true;
        }

        #endregion

        #region Private / Protected Methods
        private void RtcpControlDataReceived(object? sender, RtspDataEventArgs e)
        {
            if (e.Data.Data.IsEmpty)
                return;

            if (sender is not IRtpTransport transport)
            {
                _logger.Warning("No RTP Transport");
                return;
            }

            _logger.Debug("Received a RTCP message ");

            // RTCP Packet
            // - Version, Padding and Receiver Report Count
            // - Packet Type
            // - Length
            // - SSRC
            // - payload

            // There can be multiple RTCP packets transmitted together. Loop ever each one

            var rtcpPacket = new RtcpPacket(e.Data.Data.Span);
            while (!rtcpPacket.IsEmpty)
            {
                if (!rtcpPacket.IsWellFormed)
                {
                    _logger.Debug("Invalid RTCP packet");
                    break;
                }


                // 200 = SR = Sender Report
                // 201 = RR = Receiver Report
                // 202 = SDES = Source Description
                // 203 = Bye = Goodbye
                // 204 = APP = Application Specific Method
                // 207 = XR = Extended Reports

                _logger.Debug("RTCP Data. PacketType={rtcp_packet_type}", rtcpPacket.PacketType);

                if (rtcpPacket.PacketType == RtcpPacketUtil.RTCP_PACKET_TYPE_SENDER_REPORT)
                {
                    // We have received a Sender Report
                    // Use it to convert the RTP timestamp into the UTC time
                    var time = rtcpPacket.SenderReport.Clock;
                    var rtp_timestamp = rtcpPacket.SenderReport.RtpTimestamp;

                    _logger.Debug("RTCP time (UTC) for RTP timestamp {timestamp} is {time} SSRC {ssrc}", rtp_timestamp, time, rtcpPacket.SenderSsrc);
                    _logger.Debug("Packet Count {packetCount} Octet Count {octetCount}", rtcpPacket.SenderReport.PacketCount, rtcpPacket.SenderReport.OctetCount);

                    // Send a Receiver Report
                    try
                    {
                        byte[] rtcp_receiver_report = new byte[8];
                        const int reportCount = 0; // an empty report
                        int length = (rtcp_receiver_report.Length / 4) - 1; // num 32 bit words minus 1
                        RtcpPacketUtil.WriteHeader(
                            rtcp_receiver_report,
                            RtcpPacketUtil.RTCP_VERSION,
                            false,
                            reportCount,
                            RtcpPacketUtil.RTCP_PACKET_TYPE_RECEIVER_REPORT,
                            length,
                            _ssrc);

                        transport.WriteToControlPort(rtcp_receiver_report);
                    }
                    catch
                    {
                        _logger.Debug("Error writing RTCP packet");
                    }
                }
                rtcpPacket = rtcpPacket.Next;
            }

            e.Data.Dispose();
        }

        protected void OnStarted()
        {
            Started?.Invoke(this, new());
        }

        protected void OnStopped(RTSPClientStopReason reason)
        {
            Stopped?.Invoke(this, new(reason));
        }

        protected void OnStreamConfigured(ChannelTypes channelType, string payloadName, IStreamConfigurationData? streamConfigurationData)
        {
            StreamConfigured?.Invoke(this, new(channelType, payloadName, streamConfigurationData));
        }

        protected void OnNalUnitExtracted(ChannelTypes channelType, string payloadName, bool isKeyFrame, IEnumerable<ReadOnlyMemory<byte>> data, uint timeStamp)
        {
            if (channelType == ChannelTypes.Video)
                UpdateMetrics(timeStamp, data);

            NalUnitExtracted?.Invoke(this, new(channelType, payloadName, isKeyFrame, data, timeStamp));
        }

        protected void OnRtpPacketReceived(ChannelTypes channelType, Memory<byte> data)
        {
            RtpPacketReceived?.Invoke(this, new(channelType, data));
        }

        private void Connect(VideoSource videoSource, RTP_TRANSPORT rtpTransport)
        {
            _ = ConnectAsync(videoSource, rtpTransport);
        }

        private async Task ConnectAsync(VideoSource videoSource,
                                        RTP_TRANSPORT rtpTransport,
                                        MEDIA_REQUEST mediaRequest = MEDIA_REQUEST.VIDEO_AND_AUDIO,
                                        bool playbackSession = false,
                                        string? rtpMapVideo = null,
                                        string? rtpMapAudio = null,
                                        RemoteCertificateValidationCallback? userCertificateSelectionCallback = null)
        {

            ArgumentNullException.ThrowIfNull(videoSource);

            if (!Uri.TryCreate(videoSource.RtspURL, UriKind.Absolute, out Uri? uri))
            {
                throw new ArgumentException(
                    $"Invalid RTSP URL: '{videoSource.RtspURL}'",
                    nameof(videoSource));
            }

            RtspUtils.RegisterUri();

            _logger.Debug($"{EntityID} Connecting to " + videoSource.RtspURL);
            _uri = uri;

            _playbackSession = playbackSession;
            _setupPreferredVideoRtpMap = rtpMapVideo;
            _setupPreferredAudioRtpMap = rtpMapAudio;

            try
            {
                if (_uri.UserInfo.Length > 0)
                {
                    var parts = _uri.UserInfo.Split(':');
                    _credentials = new(parts[0], parts.Length > 1 ? parts[1] : "");
                    _uri = new Uri(_uri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.UserInfo, UriFormat.UriEscaped));
                }
                else
                {
                    _credentials = new(videoSource.RtspID, _videoSource.RtspPW);
                }
            }
            catch
            {
                _credentials = new();
            }

            clientWantsVideo = (mediaRequest is MEDIA_REQUEST.VIDEO_ONLY or MEDIA_REQUEST.VIDEO_AND_AUDIO);
            clientWantsAudio = (mediaRequest is MEDIA_REQUEST.AUDIO_ONLY or MEDIA_REQUEST.VIDEO_AND_AUDIO);


            rtspSocketStatus = RTSP_STATUS.Connecting;

            try
            {
                _rtspSocket?.Dispose();
                _rtspSocket = null;

                _rtspSocket = RtspUtils.CreateRtspTransportFromUrl(_uri, _credentials, userCertificateSelectionCallback);

                //_rtspSocket = _uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.InvariantCultureIgnoreCase)
                //    ? await RTSPHttpTransport.CreateAsync(_uri, _credentials)
                //    : await RTSPTcpTransport.CreateAsync(_uri);
            }
            catch
            {
                rtspSocketStatus = RTSP_STATUS.ConnectFailed;
                _logger.Warning($"{EntityID} Error - connection failed");
                OnStopped(RTSPClientStopReason.CONNECTION_FAILED);
                return;
            }

            if (!_rtspSocket.Connected)
            {
                rtspSocketStatus = RTSP_STATUS.ConnectFailed;
                _logger.Warning($"{EntityID} Error - connection failed");
                OnStopped(RTSPClientStopReason.CONNECTION_FAILED);
                return;
            }

            rtspSocketStatus = RTSP_STATUS.Connected;

            if (_rtspClient != null)
            {
                _rtspClient.MessageReceived -= Rtsp_MessageReceived;
                _rtspClient.Opened -= RtspClient_Opened;
                _rtspClient.Closed -= RtspClient_Closed;

                _rtspClient.Dispose();
                _rtspClient = null;
            }

            _rtspClient = new RTSPListener(_rtspSocket)
            {
                AutoReconnect = false
            };

            _rtspClient.MessageReceived += Rtsp_MessageReceived;
            _rtspClient.Opened += RtspClient_Opened;
            _rtspClient.Closed += RtspClient_Closed;
            _rtspClient.Start();

            this._rtpTransport = rtpTransport;

            videoRtpTransport?.Dispose();
            videoRtpTransport = null;

            audioRtpTransport?.Dispose();
            audioRtpTransport = null;

            switch (_rtpTransport)
            {
                case RTP_TRANSPORT.UDP:
                    videoRtpTransport = new UDPSocket(50000, 51000);
                    audioRtpTransport = new UDPSocket(50000, 51000);
                    break;
                case RTP_TRANSPORT.TCP:
                    int nextFreeRtpChannel = 0;
                    videoRtpTransport = new RtpTcpTransport(_rtspClient)
                    {
                        DataChannel = nextFreeRtpChannel++,
                        ControlChannel = nextFreeRtpChannel++,
                    };
                    audioRtpTransport = new RtpTcpTransport(_rtspClient)
                    {
                        DataChannel = nextFreeRtpChannel++,
                        ControlChannel = nextFreeRtpChannel++,
                    };
                    break;
                case RTP_TRANSPORT.MULTICAST:
                    // Will setup after SETUP message
                    break;
            }

            // Send OPTIONS message
            RtspRequest options_message = new RtspRequestOptions
            {
                RtspUri = _uri
            };

            await _rtspClient.SendMessageAsync(options_message); // 가정: SendMessageAsync 존재

            _ready = false;
        }

        private void RtspClient_Opened(object? sender, EventArgs e)
        {

        }

        private void RtspClient_Closed(object? sender, EventArgs e)
        {
            Stop(RTSPClientStopReason.CONNECTION_LOST);
        }

        //protected void UpdateMetrics(IEnumerable<ReadOnlyMemory<byte>> nalUnits)
        //{
        //    DateTime nowTime = DateTime.Now;

        //    int framesCount = nalUnits.Count();
        //    long bytesCount = nalUnits.Sum(nalUnit => nalUnit.Length);

        //    // 초기화 또는 첫 호출 시
        //    if (_prevCalcTime == DateTime.MinValue)
        //    {
        //        _receivedFrameCount = framesCount;
        //        _receivedBytes = bytesCount;
        //        _prevCalcTime = nowTime;
        //        return;
        //    }

        //    // 경과 시간 계산
        //    TimeSpan interval = nowTime - _prevCalcTime;

        //    // 프레임 수 및 데이터 양 누적
        //    _receivedFrameCount += framesCount;
        //    _receivedBytes += bytesCount;

        //    // 1초 이상의 간격 처리
        //    if (interval.TotalMilliseconds >= UPDATE_INTERVAL_MS)
        //    {
        //        // FPS 계산
        //        _fps = _receivedFrameCount / interval.TotalSeconds;

        //        // 비트레이트 계산 (초당 비트 수)
        //        _bitrate = (_receivedBytes * 8) / (interval.TotalSeconds * 1000);

        //        // 초기화
        //        _receivedFrameCount = 0; // 누적 프레임 수 초기화
        //        _receivedBytes = 0; // 누적 데이터 양 초기화
        //        _prevCalcTime = nowTime; // 현재 시간을 다음 계산의 기준으로 설정
        //    }
        //}

        protected void UpdateMetrics(uint timeStamp, IEnumerable<ReadOnlyMemory<byte>> nalUnits)
        {
            DateTime nowTime = DateTime.Now;

            int framesCount = nalUnits.Count();
            long bytesCount = nalUnits.Sum(nalUnit => nalUnit.Length);

            // 첫 호출 시 초기화
            if (_prevCalcTime == DateTime.MinValue)
            {
                _receivedFrameCount = framesCount;
                _receivedBytes = bytesCount;
                _prevCalcTime = nowTime;
                _prevTimeStamp = timeStamp;  // 타임스탬프 초기화
                return;
            }

            uint timeDiff = timeStamp - _prevTimeStamp;
            if (timeDiff > 0)
            {
                double timeInterval = timeDiff / (double)_videoClockRate;  // 90,000Hz 기준으로 변환                
                _fps = framesCount / timeInterval;// FPS 계산 (타임스탬프 간격에 따른 FPS)
                if (double.IsNaN(_fps) || double.IsInfinity(_fps))
                    _fps = 0;
            }
            _prevTimeStamp = timeStamp;


            // 비트레이트 계산 (초당 비트 수)
            _receivedFrameCount += framesCount;
            _receivedBytes += bytesCount;

            double elapsedSeconds = (nowTime - _prevCalcTime).TotalSeconds;
            if (elapsedSeconds > 0)
            {
                _bitrate = (_receivedBytes * 8) / elapsedSeconds;
            }

            // 매 1초마다 초기화
            if (elapsedSeconds >= 1)
            {
                _receivedFrameCount = 0;
                _receivedBytes = 0;
                _prevCalcTime = nowTime;
            }
        }

        //private void TrySendTeardown()
        //{
        //    try
        //    {

        //        RtspRequest teardown_message = new RtspRequestTeardown();
        //        teardown_message.RtspUri = new Uri(_url);
        //        teardown_message.Session = _session;
        //        if (_authType != null)
        //        {
        //            AddAuthorization(teardown_message, _username, _password, _authType, _realm, _nonce, _url);
        //            _rtspClient.SendMessage(teardown_message);
        //        }

        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.Warning($"Error on sending teardown rtsp client for source {VideoSourceID}: {ex}");
        //    }

        //}


        //public void VideoRtpDataReceived(object? sender, RtspDataEventArgs e)
        //{
        //    if (e.Data.Data.IsEmpty)
        //        return;

        //    using var data = e.Data;
        //    var rtpPacket = new RTPPacket(data.Data.Span);

        //    if (rtpPacket.PayloadType != _videoPayload)
        //    {
        //        // Check the payload type in the RTP packet matches the Payload Type value from the SDP
        //        _logger.Debug("Ignoring this Video RTP payload");
        //        return; // ignore this data
        //    }

        //    if (videoPayloadProcessor is null)
        //    {
        //        _logger.Warning("No video Processor");
        //        return;
        //    }

        //    using RawMediaFrame nal_units = videoPayloadProcessor.ProcessPacket(rtpPacket); // this will cache the Packets until there is a Frame

        //    if (nal_units.Any())
        //    {
        //        OnNalUnitDataReceived(ChannelTypes.Video, nal_units.Data, nal_units.RtpTimestamp);             
        //    }
        //}

        //// RTP packet (or RTCP packet) has been received.
        //public void AudioRtpDataReceived(object? sender, RtspDataEventArgs e)
        //{
        //    if (e.Data.Data.IsEmpty)
        //        return;

        //    using var data = e.Data;
        //    // Received some Audio Data on the correct channel.
        //    var rtpPacket = new RTPPacket(data.Data.Span);

        //    // Check the payload type in the RTP packet matches the Payload Type value from the SDP
        //    if (rtpPacket.PayloadType != _audioPayload)
        //    {
        //        _logger.Debug("Ignoring this Audio RTP payload");
        //        return; // ignore this data
        //    }

        //    if (audioPayloadProcessor is null)
        //    {
        //        _logger.Warning("No parser for RTP payload {audioPayload}", _audioPayload);
        //        return;
        //    }

        //    using var audioFrames = audioPayloadProcessor.ProcessPacket(rtpPacket);

        //    if (audioFrames.Any())
        //    {
        //        OnNalUnitDataReceived(ChannelTypes.Audio, audioFrames.Data, audioFrames.RtpTimestamp);                
        //        // AAC
        //        // Write the audio frames to the file
        //        //  ReceivedAAC?.Invoke(this, new(audio_codec, audioFrames.Data, aacPayload.ObjectType, aacPayload.FrequencyIndex, aacPayload.ChannelConfiguration, audioFrames.Timestamp));
        //    }
        //}        

        //private void Rtp_ParseMessageData(byte[] data, out int rtp_payload_type, out int rtp_payload_start, out int rtp_marker, bool trace)
        //{

        //    // RTP Packet Header
        //    // 0 - Version, P, X, CC, M, PT and Sequence Number
        //    //32 - Timestamp
        //    //64 - SSRC
        //    //96 - CSRCs (optional)
        //    //nn - Extension ID and Length
        //    //nn - Extension header

        //    int rtp_version = (data[0] >> 6);
        //    int rtp_padding = (data[0] >> 5) & 0x01;
        //    int rtp_extension = (data[0] >> 4) & 0x01;
        //    int rtp_csrc_count = (data[0] >> 0) & 0x0F;
        //    rtp_marker = (data[1] >> 7) & 0x01;
        //    rtp_payload_type = (data[1] >> 0) & 0x7F;
        //    uint rtp_sequence_number = ((uint)data[2] << 8) + (uint)(data[3]);
        //    uint rtp_timestamp = ((uint)data[4] << 24) + (uint)(data[5] << 16) + (uint)(data[6] << 8) + (uint)(data[7]);
        //    uint rtp_ssrc = ((uint)data[8] << 24) + (uint)(data[9] << 16) + (uint)(data[10] << 8) + (uint)(data[11]);

        //    DateTime orign = new DateTime(1970, 1, 1, 0, 0, 0);
        //    DateTime test = orign.AddMilliseconds(rtp_timestamp);

        //    rtp_payload_start = 4 // V,P,M,SEQ
        //                        + 4 // time stamp
        //                        + 4 // ssrc
        //                        + (4 * rtp_csrc_count); // zero or more csrcs

        //    uint rtp_extension_id = 0;
        //    uint rtp_extension_size = 0;
        //    if (rtp_extension == 1)
        //    {
        //        rtp_extension_id = ((uint)data[rtp_payload_start + 0] << 8) + (uint)(data[rtp_payload_start + 1] << 0);
        //        rtp_extension_size = ((uint)data[rtp_payload_start + 2] << 8) + (uint)(data[rtp_payload_start + 3] << 0) * 4; // units of extension_size is 4-bytes
        //        rtp_payload_start += 4 + (int)rtp_extension_size;  // extension header and extension payload
        //    }

        //    if (trace)
        //    {
        //        _logger.Verbose("RTP Data"
        //        + " V=" + rtp_version
        //        + " P=" + rtp_padding
        //        + " X=" + rtp_extension
        //        + " CC=" + rtp_csrc_count
        //        + " M=" + rtp_marker
        //        + " PT=" + rtp_payload_type
        //        + " Seq=" + rtp_sequence_number
        //        + " Time (MS)=" + rtp_timestamp / 90 // convert from 90kHZ clock to ms
        //        + " SSRC=" + rtp_ssrc
        //        + " Size=" + data.Length);
        //    }

        //}

        //private byte[] Rtp_ExtractPayload(byte[] data, int rtp_payload_start)
        //{
        //    byte[] rtp_payload = new byte[data.Length - rtp_payload_start]; // payload with RTP header removed
        //    System.Array.Copy(data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload
        //    return rtp_payload;
        //}       

        //private void Rtp_ProcessH264Payload(uint timestamp, byte[] rtp_payload, int rtp_marker)
        //{
        // H264 RTP Packet

        // If rtp_marker is '1' then this is the final transmission for this packet.
        // If rtp_marker is '0' we need to accumulate data with the same timestamp

        // ToDo - Check Timestamp
        // Add the RTP packet to the tempoary_rtp list until we have a complete 'Frame'            

        //List<byte[]> nal_units = _h264Payload.Process_H264_RTP_Packet(rtp_payload, rtp_marker); // this will cache the Packets until there is a Frame
        //if (nal_units == null)
        //{
        //    // we have not passed in enough RTP packets to make a Frame of video
        //}
        //else
        //{
        //    // If we did not have a SPS and PPS in the SDP then search for the SPS and PPS
        //    // in the NALs and fire the Received_SPS_PPS event.
        //    // We assume the SPS and PPS are in the same Frame.
        //    //if (_h264SpsPpsFired == false)
        //    //{
        //    //    // Check this frame for SPS and PPS
        //    //    byte[] sps = null;
        //    //    byte[] pps = null;

        //    //    foreach (byte[] nal_unit in nal_units)
        //    //    {
        //    //        if (nal_unit.Length > 0)
        //    //        {
        //    //            int nal_ref_idc = (nal_unit[0] >> 5) & 0x03;
        //    //            int nal_unit_type = nal_unit[0] & 0x1F;

        //    //            switch (nal_unit_type)
        //    //            {
        //    //                case 7: sps = nal_unit; break;  //SPS
        //    //                case 8: pps = nal_unit; break;  //PPS
        //    //            }

        //    //            //if (nal_unit_type == 7) sps = nal_unit; // SPS
        //    //            //if (nal_unit_type == 8) pps = nal_unit; // PPS
        //    //        }
        //    //    }
        //    //    if (sps != null && pps != null)
        //    //    {
        //    //        // Fire the Event
        //    //        if (Received_ParameterSets != null)
        //    //        {
        //    //            Received_ParameterSets(this, _videoCodec, new List<byte[]>() { sps, pps });
        //    //        }
        //    //        _h264SpsPpsFired = true;
        //    //    }
        //    //}

        //    // we have a frame of NAL Units. Write them to the file
        //    OnReceived_NALs(_videoCodec, timestamp, nal_units);
        //}
        //}

        //private void Rtp_ProcessH265Payload(uint timestamp, byte[] rtp_payload, int rtp_marker)
        //{
        //    // H265 RTP Packet

        //    // If rtp_marker is '1' then this is the final transmission for this packet.
        //    // If rtp_marker is '0' we need to accumulate data with the same timestamp

        //    // Add the RTP packet to the tempoary_rtp list until we have a complete 'Frame'

        //    List<byte[]> nal_units = _h265Payload.Process_H265_RTP_Packet(rtp_payload, rtp_marker); // this will cache the Packets until there is a Frame

        //    if (nal_units == null)
        //    {
        //        // we have not passed in enough RTP packets to make a Frame of video
        //    }
        //    else
        //    {
        //        // If we did not have a VPS, SPS and PPS in the SDP then search for the VPS SPS and PPS
        //        // in the NALs and fire the Received_VPS_SPS_PPS event.
        //        // We assume the VPS, SPS and PPS are in the same Frame.
        //        if (_h265VpsSpsPpsFired == false)
        //        {
        //            // Check this frame for VPS, SPS and PPS
        //            byte[] vps = null;
        //            byte[] sps = null;
        //            byte[] pps = null;

        //            foreach (byte[] nal_unit in nal_units)
        //            {
        //                if (nal_unit.Length > 0)
        //                {
        //                    int nal_unit_type = (nal_unit[0] >> 1) & 0x3F;

        //                    switch (nal_unit_type)
        //                    {
        //                        case 32: vps = nal_unit; break;  //VPS
        //                        case 33: pps = nal_unit; break;  //PPS
        //                        case 34: pps = nal_unit; break;  //PPS
        //                    }

        //                    //if (nal_unit_type == 32) vps = nal_unit; // VPS
        //                    //if (nal_unit_type == 33) sps = nal_unit; // SPS
        //                    //if (nal_unit_type == 34) pps = nal_unit; // PPS
        //                }
        //            }
        //            if (vps != null && sps != null && pps != null)
        //            {
        //                //// Fire the Event
        //                if (Received_ParameterSets != null)
        //                {
        //                    Received_ParameterSets(this,_videoCodec, new List<byte[]>() { vps, sps, pps });
        //                }
        //                _h265VpsSpsPpsFired = true;
        //            }
        //        }

        //        // we have a frame of NAL Units. Write them to the file
        //        OnReceived_NALs(_videoCodec, timestamp, nal_units);
        //    }
        //}

        //private void Rtp_ProcessG711Payload(byte[] rtp_payload, int rtp_marker)
        //{
        //    // G711 PCMA or G711 PCMU
        //    List<byte[]> audio_frames = _g711Payload.Process_G711_RTP_Packet(rtp_payload, rtp_marker);

        //    if (audio_frames == null)
        //    {
        //        // some error
        //    }
        //    else
        //    {
        //        // Write the audio frames to the file
        //        if (Received_G711 != null)
        //        {
        //            Received_G711(this, _audioCodec, audio_frames);
        //        }
        //    }
        //}

        //private void Rtp_ProcessAMRPayload(byte[] rtp_payload, int rtp_marker)
        //{
        //    //AMR
        //    List<byte[]> audio_frames = _amrPayload.Process_AMR_RTP_Packet(rtp_payload, rtp_marker);

        //    if (audio_frames == null)
        //    {
        //        // some error
        //    }
        //    else
        //    {
        //        // Write the audio frames to the file
        //        if (Received_AMR != null)
        //        {
        //            Received_AMR(this,_audioCodec, audio_frames);
        //        }
        //    }
        //}

        //private void Rtp_ProcessAACPayload(byte[] rtp_payload, int rtp_marker)
        //{
        //    //AAC
        //    List<byte[]> audio_frames = _aacPayload.Process_AAC_RTP_Packet(rtp_payload, rtp_marker);

        //    if (audio_frames == null)
        //    {
        //        // some error
        //    }
        //    else
        //    {
        //        // Write the audio frames to the file
        //        if (Received_AAC != null)
        //        {
        //            Received_AAC(this, _audioCodec, audio_frames, _aacPayload.ObjectType, _aacPayload.FrequencyIndex, _aacPayload.ChannelConfiguration);
        //        }
        //    }
        //}

        //public void Rtp_DataReceived(object sender, OPNX.Lib.Streaming.RTSP.RTSPChunkEventArgs e)
        //{
        //    RtspData rtspData = e.Message as RtspData; 

        //    // Check which channel the Data was received on.
        //    // eg the Video Channel, the Video Control Channel (RTCP)
        //    // the Audio Channel or the Audio Control Channel (RTCP)

        //    if (rtspData.Channel == _videoDataChannel || rtspData.Channel == _audioDataChannel)
        //    {
        //        //var handler = Received_Rtp;
        //        //if (handler != null)
        //        //{
        //        //    handler(this, data_received.Channel, e.Message.Data);
        //        //}

        //        ChannelTypes channelType = ChannelTypes.None;
        //        if (rtspData.Channel == _videoDataChannel)
        //            channelType = ChannelTypes.Video;
        //        else if (rtspData.Channel == _audioDataChannel)
        //            channelType = ChannelTypes.Audio;

        //        OnRTPDataReceived(channelType, e.Message.Data);

        //        RTPPacket rtpPacket = new RTPPacket(e.Message.Data.Span);

        //        switch (rtspData.Channel)
        //        {
        //            case int channel when channel == _videoDataChannel:
        //                {
        //                    switch (rtpPacket.PayloadType)
        //                    {
        //                        case int payloadType when payloadType != _videoPayload || payloadType == 26:
        //                            {
        //                                _logger.Debug(payloadType == 26 ? "No parser has been written for JPEG RTP packets. Please help write one" : "Ignoring this Video RTP payload");
        //                            }
        //                            return;
        //                        default:
        //                            {
        //                                //VideoRtpDataReceived(rtpPacket);                                        
        //                            }
        //                            break;
        //                    }
        //                }
        //                break;
        //            case int channel when channel == _audioDataChannel:
        //                {
        //                    //switch (rtpPacket.PayloadType)
        //                    //{
        //                    //    case int payloadType when payloadType != _audioPayload:
        //                    //        {
        //                    //            _logger.Debug("Ignoring this Audio RTP payload");
        //                    //        }
        //                    //        break;
        //                    //    case int payloadType when payloadType == 0 || payloadType == 8:
        //                    //        {
        //                    //            switch (_audioCodec)
        //                    //            {
        //                    //                case "AMR":
        //                    //                    {
        //                    //                        Rtp_ProcessAMRPayload(rtpPacket.Payload.ToArray(), rtpPacket.MarkerBit);
        //                    //                    }
        //                    //                    break;
        //                    //                case "MPEG4-GENERIC":
        //                    //                    {
        //                    //                        Rtp_ProcessAACPayload(rtpPacket.Payload.ToArray(), rtpPacket.MarkerBit);
        //                    //                    }
        //                    //                    break;
        //                    //                case "PCMA":
        //                    //                case "PCMU":
        //                    //                    {
        //                    //                        Rtp_ProcessG711Payload(rtpPacket.Payload.ToArray(), rtpPacket.MarkerBit);
        //                    //                    }
        //                    //                    break;
        //                    //            }
        //                    //        }
        //                    //        break;
        //                    //}
        //                }
        //                break;
        //            default:
        //                {
        //                    _logger.Warning("No parser for RTP payload " + rtpPacket.PayloadType);
        //                }
        //                break;
        //        }


        //        //int rtp_payload_type;
        //        //int rtp_payload_start;
        //        //int rtp_marker;
        //        //Rtp_ParseMessageData(e.Message.Data, out rtp_payload_type, out rtp_payload_start, out rtp_marker, false);                


        //        //// Check the payload type in the RTP packet matches the Payload Type value from the SDP
        //        //if (data_received.Channel == _videoDataChannel && rtp_payload_type != _videoPayload)
        //        //{
        //        //    _logger.Debug("Ignoring this Video RTP payload");
        //        //    return; // ignore this data
        //        //}

        //        //// Check the payload type in the RTP packet matches the Payload Type value from the SDP
        //        //else if (data_received.Channel == _audioDataChannel && rtp_payload_type != _audioPayload)
        //        //{
        //        //    _logger.Debug("Ignoring this Audio RTP payload");
        //        //    return; // ignore this data
        //        //}
        //        //else if (data_received.Channel == _videoDataChannel
        //        //         && rtp_payload_type == _videoPayload
        //        //         && _videoCodec.Equals("H264"))
        //        //{
        //        //    byte[] rtp_payload = Rtp_ExtractPayload(e.Message.Data, rtp_payload_start);
        //        //    Rtp_ProcessH264Payload(rtp_payload, rtp_marker);
        //        //}
        //        //else if (data_received.Channel == _videoDataChannel
        //        //         && rtp_payload_type == _videoPayload
        //        //         && _videoCodec.Equals("H265"))
        //        //{
        //        //    byte[] rtp_payload = Rtp_ExtractPayload(e.Message.Data, rtp_payload_start);
        //        //    Rtp_ProcessH265Payload(rtp_payload, rtp_marker);
        //        //}
        //        //else if (data_received.Channel == _audioDataChannel && (rtp_payload_type == 0 || rtp_payload_type == 8 || _audioCodec.Equals("PCMA") || _audioCodec.Equals("PCMU")))
        //        //{
        //        //    byte[] rtp_payload = Rtp_ExtractPayload(e.Message.Data, rtp_payload_start);
        //        //    Rtp_ProcessG711Payload(rtp_payload, rtp_marker);
        //        //}
        //        //else if (data_received.Channel == _audioDataChannel
        //        //          && rtp_payload_type == _audioPayload
        //        //          && _audioCodec.Equals("AMR"))
        //        //{
        //        //    byte[] rtp_payload = Rtp_ExtractPayload(e.Message.Data, rtp_payload_start);
        //        //    Rtp_ProcessAMRPayload(rtp_payload, rtp_marker);
        //        //}
        //        //else if (data_received.Channel == _audioDataChannel
        //        //         && rtp_payload_type == _audioPayload
        //        //         && _audioCodec.Equals("MPEG4-GENERIC")
        //        //        && _aacPayload != null)
        //        //{
        //        //    byte[] rtp_payload = Rtp_ExtractPayload(e.Message.Data, rtp_payload_start);
        //        //    Rtp_ProcessAACPayload(rtp_payload, rtp_marker);
        //        //}
        //        //else if (data_received.Channel == _videoDataChannel && rtp_payload_type == 26)
        //        //{
        //        //    _logger.Warn("No parser has been written for JPEG RTP packets. Please help write one");
        //        //    return; // ignore this data
        //        //}
        //        //else
        //        //{
        //        //    _logger.Warn("No parser for RTP payload " + rtp_payload_type);
        //        //}
        //    }

        //    rtspData?.Dispose();
        //}

        //private void Rtsp_ProcessResponseAuthorization(RtspResponse message)
        //{
        //    // Process the WWW-Authenticate header
        //    // EG:   Basic realm="AProxy"
        //    // EG:   Digest realm="AXIS_WS_ACCC8E3A0A8F", nonce="000057c3Y810622bff50b36005eb5efeae118626a161bf", stale=FALSE
        //    // EG:   Digest realm="IP Camera(21388)", nonce="534407f373af1bdff561b7b4da295354", stale="FALSE"

        //    String www_authenticate = message.Headers[RtspHeaderNames.WWWAuthenticate];
        //    String auth_params = "";

        //    if (www_authenticate.StartsWith("basic", StringComparison.InvariantCultureIgnoreCase))
        //    {
        //        _authType = "Basic";
        //        auth_params = www_authenticate.Substring(5);
        //    }
        //    if (www_authenticate.StartsWith("digest", StringComparison.InvariantCultureIgnoreCase))
        //    {
        //        _authType = "Digest";
        //        auth_params = www_authenticate.Substring(6);
        //    }

        //    string[] items = auth_params.Split(new char[] { ',' }); // NOTE, does not handle Commas in Quotes

        //    foreach (string item in items)
        //    {
        //        // Split on the = symbol and update the realm and nonce
        //        string[] parts = item.Trim().Split(new char[] { '=' }, 2); // max 2 parts in the results array
        //        if (parts.Count() >= 2 && parts[0].Trim().Equals("realm"))
        //        {
        //            _realm = parts[1].Trim(new char[] { ' ', '\"' }); // trim space and quotes
        //        }
        //        else if (parts.Count() >= 2 && parts[0].Trim().Equals("nonce"))
        //        {
        //            _nonce = parts[1].Trim(new char[] { ' ', '\"' }); // trim space and quotes
        //        }
        //    }

        //    _logger.Debug($"{VideoSourceID} WWW Authorize parsed for " + _authType + " " + _realm + " " + _nonce);
        //}


        //private void SdpParseMediaAttributes(OPNX.Lib.Streaming.RTSP.Sdp.Media media, bool video, bool audio, ref string control, ref AttributFmtp fmtp, ref AttributRtpMap rtpMap)
        //{
        //    // search the attributes for control, rtpmap and fmtp
        //    // (fmtp only applies to video)
        //    foreach (OPNX.Lib.Streaming.RTSP.Sdp.Attribut attrib in media.Attributs)
        //    {
        //        if (attrib.Key.Equals("control"))
        //        {
        //            String sdp_control = attrib.Value;
        //            if (sdp_control.ToLower().StartsWith("rtsp://"))
        //            {
        //                control = sdp_control; //absolute path
        //            }
        //            else
        //            {
        //                control = _uri.OriginalString + "/" + sdp_control; // relative path
        //            }
        //            if (video) _videoUri = new Uri(control);
        //            if (audio) _audioUri = new Uri(control);
        //        }
        //        if (attrib.Key.Equals("fmtp"))
        //        {
        //            fmtp = attrib as OPNX.Lib.Streaming.RTSP.Sdp.AttributFmtp;
        //        }
        //        if (attrib.Key.Equals("rtpmap"))
        //        {
        //            rtpMap = attrib as OPNX.Lib.Streaming.RTSP.Sdp.AttributRtpMap;
        //        }
        //    }
        //}

        private RtspTransport? CalculateTransport(IRtpTransport? transport)
        {
            return _rtpTransport switch
            {
                // Server interleaves the RTP packets over the RTSP connection
                // Example for TCP mode (RTP over RTSP)   Transport: RTP/AVP/TCP;interleaved=0-1
                RTP_TRANSPORT.TCP => new RtspTransport()
                {
                    LowerTransport = RtspTransport.LowerTransportType.TCP,
                    // Eg Channel 0 for RTP video data. Channel 1 for RTCP status reports
                    Interleaved = (transport as RtpTcpTransport)?.Channels ?? throw new ApplicationException("TCP transport asked and no tcp channel allocated"),
                },
                RTP_TRANSPORT.UDP => new RtspTransport()
                {
                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                    IsMulticast = false,
                    ClientPort = (transport as UDPSocket)?.Ports ?? throw new ApplicationException("UDP transport asked and no udp port allocated"),
                },
                // Server sends the RTP packets to a Pair of UDP ports (one for data, one for rtcp control messages)
                // using Multicast Address and Ports that are in the reply to the SETUP message
                // Example for MULTICAST mode     Transport: RTP/AVP;multicast
                RTP_TRANSPORT.MULTICAST => new RtspTransport()
                {
                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                    IsMulticast = true,
                    ClientPort = new PortCouple(5000, 5001)
                },
                _ => null,
            };
        }

        //private RtspTransport CreateRtspTransport(bool video, bool audio, ref int next_free_rtp_channel, ref int next_free_rtcp_channel)
        //{
        //    RtspTransport transport = null;

        //    if (_rtpTransport == RTP_TRANSPORT.TCP)
        //    {
        //        // Server interleaves the RTP packets over the RTSP connection
        //        // Example for TCP mode (RTP over RTSP)   Transport: RTP/AVP/TCP;interleaved=0-1
        //        if (video)
        //        {
        //            _videoDataChannel = next_free_rtp_channel;
        //            _videoRtcpChannel = next_free_rtcp_channel;
        //        }
        //        if (audio)
        //        {
        //            _audioDataChannel = next_free_rtp_channel;
        //            _audioRtcpChannel = next_free_rtcp_channel;
        //        }
        //        transport = new RtspTransport()
        //        {
        //            LowerTransport = RtspTransport.LowerTransportType.TCP,
        //            Interleaved = new PortCouple(next_free_rtp_channel, next_free_rtcp_channel), // Eg Channel 0 for RTP video data. Channel 1 for RTCP status reports
        //        };

        //        next_free_rtp_channel += 2;
        //        next_free_rtcp_channel += 2;
        //    }
        //    if (_rtpTransport == RTP_TRANSPORT.UDP)
        //    {
        //        int rtp_port = 0;
        //        int rtcp_port = 0;
        //        // Server sends the RTP packets to a Pair of UDP Ports (one for data, one for rtcp control messages)
        //        // Example for UDP mode                   Transport: RTP/AVP;unicast;client_port=8000-8001
        //        if (video)
        //        {
        //            _videoDataChannel = _videoUdpPair._dataPort;     // Used in DataReceived event handler
        //            _videoRtcpChannel = _videoUdpPair._controlPort;  // Used in DataReceived event handler
        //            rtp_port = _videoUdpPair._dataPort;
        //            rtcp_port = _videoUdpPair._controlPort;
        //        }
        //        if (audio)
        //        {
        //            _audioDataChannel = _audioUdpPair._dataPort;     // Used in DataReceived event handler
        //            _audioRtcpChannel = _audioUdpPair._controlPort;  // Used in DataReceived event handler
        //            rtp_port = _audioUdpPair._dataPort;
        //            rtcp_port = _audioUdpPair._controlPort;
        //        }
        //        transport = new RtspTransport()
        //        {
        //            LowerTransport = RtspTransport.LowerTransportType.UDP,
        //            IsMulticast = false,
        //            ClientPort = new PortCouple(rtp_port, rtcp_port), // a UDP Port for data (video or audio). a UDP Port for RTCP status reports
        //        };
        //    }
        //    if (_rtpTransport == RTP_TRANSPORT.MULTICAST)
        //    {
        //        // Server sends the RTP packets to a Pair of UDP ports (one for data, one for rtcp control messages)
        //        // using Multicast Address and Ports that are in the reply to the SETUP message
        //        // Example for MULTICAST mode     Transport: RTP/AVP;multicast
        //        if (video)
        //        {
        //            _videoDataChannel = 0; // we get this information in the SETUP message reply
        //            _videoRtcpChannel = 0; // we get this information in the SETUP message reply
        //        }
        //        if (audio)
        //        {
        //            _audioDataChannel = 0; // we get this information in the SETUP message reply
        //            _audioRtcpChannel = 0; // we get this information in the SETUP message reply
        //        }
        //        transport = new RtspTransport()
        //        {
        //            LowerTransport = RtspTransport.LowerTransportType.UDP,
        //            IsMulticast = true
        //        };
        //    }

        //    return transport;
        //}

        //private void ProcessH264Fmtp(OPNX.Lib.Streaming.RTSP.Sdp.AttributFmtp fmtp)
        //{
        //    var param = OPNX.Lib.Streaming.RTSP.Sdp.H264Parameters.Parse(fmtp.FormatParameter);
        //    var sps_pps = param.SpropParameterSets;
        //    if (sps_pps.Count() >= 2)
        //    {
        //        string test = Encoding.Default.GetString(sps_pps[0]);
        //        string tes2 = Encoding.Default.GetString(sps_pps[1]);

        //        if (Received_ParameterSets != null)
        //        {
        //            Received_ParameterSets(this, _videoCodec, sps_pps);
        //        }
        //        _h264SpsPpsFired = true;
        //    }
        //}

        //private void ProcessH265Fmtp(OPNX.Lib.Streaming.RTSP.Sdp.AttributFmtp fmtp)
        //{
        //    var param = OPNX.Lib.Streaming.RTSP.Sdp.H265Parameters.Parse(fmtp.FormatParameter);
        //    var vps_sps_pps = param.SpropParameterSets;
        //    if (vps_sps_pps.Count() >= 3)
        //    {
        //        if (Received_ParameterSets != null)
        //        {
        //            Received_ParameterSets(this,_videoCodec, vps_sps_pps);                    
        //        }
        //        _h265VpsSpsPpsFired = true;
        //    }
        //}

        //private void Rtsp_ProcessResponseDescribe(RtspResponse message)
        //{
        //    // If we get a reply to DESCRIBE (which was our second command), then prosess SDP and send the SETUP

        //    // Got a reply for DESCRIBE
        //    if (message.IsOk == false)
        //    {
        //        _logger.Debug($"{EntityID} Got Error in DESCRIBE Reply " + message.ReturnCode + " " + message.ReturnMessage);
        //        return;
        //    }

        //    // Examine the SDP

        //    _logger.Debug($"{EntityID} " + System.Text.Encoding.UTF8.GetString(message.Data.Span));            

        //    SdpFile sdp_data;
        //    using (var sdpStream = new MemoryStream(message.Data.ToArray()))
        //    {
        //        sdp_data = SdpFile.ReadLoose(new StreamReader(sdpStream));
        //    }

        //    // RTP and RTCP 'channels' are used in TCP Interleaved mode (RTP over RTSP)
        //    // These are the channels we request. The camera confirms the channel in the SETUP Reply.
        //    // But, a Panasonic decides to use different channels in the reply.
        //    //int next_free_rtp_channel = 0;
        //    //int next_free_rtcp_channel = 1;

        //    // Process each 'Media' Attribute in the SDP (each sub-stream)

        //    foreach (Media media in sdp_data.Medias)
        //    {

        //        bool audio = (media.MediaType == Media.MediaTypes.audio);
        //        bool video = (media.MediaType == Media.MediaTypes.video);

        //        if (video && _videoPayload != -1) continue; // have already matched a video payload. don't match another
        //        if (audio && _audioPayload != -1) continue; // have already matched an audio payload. don't match another

        //        if (audio || video)
        //        {
        //            String control = "";  // the "track" or "stream id"
        //            AttributFmtp fmtp = null; // holds SPS and PPS in base64 (h264 video)
        //            AttributRtpMap rtpMap = null;
        //            SdpParseMediaAttributes(media, video, audio, ref control, ref fmtp, ref rtpMap);

        //            int fmtpPayloadNumber = -1;
        //            if (fmtp != null)
        //            {
        //                fmtpPayloadNumber = fmtp.PayloadNumber;
        //            }

        //            if (video)
        //            {
        //                _videoCodec = rtpMap.EncodingName.ToUpper();
        //                _videoPayload = media.PayloadType;
        //                if (!string.IsNullOrEmpty(rtpMap.ClockRate))
        //                    _videoClockRate = Convert.ToDouble(rtpMap.ClockRate);


        //                bool h265HasDonl = false;

        //                if ((rtpMap?.EncodingName?.ToUpper().Equals("H265") ?? false) && !string.IsNullOrEmpty(fmtp?.FormatParameter))
        //                {
        //                    var param = H265Parameters.Parse(fmtp.FormatParameter);
        //                    if (param.ContainsKey("sprop-max-don-diff") && int.TryParse(param["sprop-max-don-diff"], out int donl) && donl > 0)
        //                    {
        //                        h265HasDonl = true;
        //                    }
        //                }

        //                string payloadName = string.Empty;
        //                if (rtpMap != null
        //                    && (((fmtpPayloadNumber > -1 && rtpMap.PayloadNumber == fmtpPayloadNumber) || fmtpPayloadNumber == -1)
        //                    && rtpMap.EncodingName != null))
        //                {
        //                    // found a valid codec
        //                    payloadName = rtpMap.EncodingName.ToUpper();
        //                    videoPayloadProcessor = payloadName switch
        //                    {
        //                        "H264" => new H264Payload(null),
        //                        "H265" => new H265Payload(h265HasDonl, null),
        //                        "JPEG" => new JPEGPayload(),
        //                        "MP4V-ES" => new RawPayload(),
        //                        _ => null,
        //                    };
        //                }
        //                else
        //                {
        //                    if (media.PayloadType < 96)
        //                    {
        //                        // PayloadType is a static value, so we can use it to determine the codec
        //                        videoPayloadProcessor = media.PayloadType switch
        //                        {
        //                            26 => new JPEGPayload(),
        //                            33 => new MP2TransportPayload(),
        //                            _ => null,
        //                        };
        //                        payloadName = media.PayloadType switch
        //                        {
        //                            26 => "JPEG",
        //                            33 => "MP2T",
        //                            _ => string.Empty,
        //                        };
        //                    }
        //                }

        //                IStreamConfigurationData streamConfigurationData = null;

        //                if (videoPayloadProcessor is H264Payload && fmtp?.FormatParameter is not null)
        //                {
        //                    var param = H264Parameters.Parse(fmtp.FormatParameter);
        //                    var sps_pps = param.SpropParameterSets;
        //                    if (sps_pps.Count >= 2)
        //                    {
        //                        byte[] sps = sps_pps[0];
        //                        byte[] pps = sps_pps[1];
        //                        streamConfigurationData = new H264StreamConfigurationData() { SPS = sps, PPS = pps };
        //                    }
        //                }
        //                else if (videoPayloadProcessor is H265Payload && fmtp?.FormatParameter is not null)
        //                {
        //                    // If the rtpmap contains H265 then split the fmtp to get the sprop-vps, sprop-sps and sprop-pps
        //                    // The RFC makes the VPS, SPS and PPS OPTIONAL so they may not be present. In which we pass back NULL values
        //                    var param = H265Parameters.Parse(fmtp.FormatParameter);
        //                    var vps_sps_pps = param.SpropParameterSets;
        //                    if (vps_sps_pps.Count >= 3)
        //                    {
        //                        byte[] vps = vps_sps_pps[0];
        //                        byte[] sps = vps_sps_pps[1];
        //                        byte[] pps = vps_sps_pps[2];
        //                        streamConfigurationData = new H265StreamConfigurationData() { VPS = vps, SPS = sps, PPS = pps };
        //                    }
        //                }

        //                if (videoPayloadProcessor is not null)
        //                {
        //                    RtspTransport transport = CalculateTransport(videoRtpTransport);
        //                    if (transport != null)
        //                    {
        //                        RtspRequestSetup setup_message = new()
        //                        {
        //                            RtspUri = _videoUri
        //                        };
        //                        setup_message.AddTransport(transport);
        //                        setup_message.AddAuthorization(_authentication, _uri!, _rtspSocket!.NextCommandIndex());
        //                        //if (_playbackSession) { setup_message.AddRequireOnvifRequest(); }
        //                        // Add SETUP message to list of mesages to send
        //                        _setupMessages.Enqueue(setup_message);

        //                        OnStreamConfiguration(ChannelTypes.Video, payloadName, streamConfigurationData);                                
        //                    }
        //                }
        //            }

        //            if (audio)
        //            {
        //                _audioPayload = media.PayloadType;

        //                IStreamConfigurationData streamConfigurationData = null;
        //                if (media.PayloadType < 96)
        //                {
        //                    // fixed payload type
        //                    (audioPayloadProcessor, _audioCodec) = media.PayloadType switch
        //                    {
        //                        0 => (new G711Payload(), "PCMU"),
        //                        8 => (new G711Payload(), "PCMA"),
        //                        _ => (null, ""),
        //                    };
        //                }
        //                else
        //                {
        //                    // dynamic payload type
        //                    _audioCodec = rtpMap?.EncodingName?.ToUpper() ?? string.Empty;
        //                    audioPayloadProcessor = _audioCodec switch
        //                    {
        //                        // Create AAC RTP Parser
        //                        // Example fmtp is "96 profile-level-id=1;mode=AAC-hbr;sizelength=13;indexlength=3;indexdeltalength=3;config=1490"
        //                        // Example fmtp is ""96 streamtype=5;profile-level-id=1;mode=AAC-hbr;sizelength=13;indexlength=3;indexdeltalength=3;config=1210"
        //                        "MPEG4-GENERIC" when fmtp?["mode"].ToLower() == "aac-hbr" => new AACPayload(fmtp["config"]),
        //                        "PCMA" => new G711Payload(),
        //                        "PCMU" => new G711Payload(),
        //                        "AMR" => new AMRPayload(),
        //                        _ => null,
        //                    };
        //                    if (audioPayloadProcessor is AACPayload aacPayloadProcessor)
        //                    {
        //                        _audioCodec = "AAC";
        //                        streamConfigurationData = new AacStreamConfigurationData()
        //                        {
        //                            ObjectType = aacPayloadProcessor.ObjectType,
        //                            FrequencyIndex = aacPayloadProcessor.FrequencyIndex,
        //                            SamplingFrequency = aacPayloadProcessor.SamplingFrequency,
        //                            ChannelConfiguration = aacPayloadProcessor.ChannelConfiguration
        //                        };
        //                    }
        //                }

        //                if (audioPayloadProcessor is not null)
        //                {
        //                    RtspTransport transport = CalculateTransport(audioRtpTransport);
        //                    if (transport != null)
        //                    {
        //                        RtspRequestSetup setup_message = new()
        //                        {
        //                            RtspUri = _audioUrl
        //                        };
        //                        setup_message.AddTransport(transport);
        //                        setup_message.AddAuthorization(_authentication, _uri!, _rtspSocket!.NextCommandIndex());
        //                        //if (_playbackSession) { setup_message.AddRequireOnvifRequest(); }
        //                        // Add SETUP message to list of mesages to send
        //                        _setupMessages.Enqueue(setup_message);


        //                        OnStreamConfiguration(ChannelTypes.Audio, _audioCodec, streamConfigurationData);
        //                    }
        //                }
        //            }

        //            // Send the SETUP RTSP command if we have a matching Payload Decoder
        //            //if (video && _videoPayload == -1) continue;
        //            //if (audio && _audioPayload == -1) continue;

        //            //RtspTransport transport = CreateRtspTransport(video, audio, ref next_free_rtp_channel, ref next_free_rtcp_channel);

        //            //// Generate SETUP messages
        //            //OPNX.Lib.Streaming.RTSP.Messages.RtspRequestSetup setup_message = new RtspRequestSetup();
        //            //setup_message.RtspUri = new Uri(control);
        //            //setup_message.AddTransport(transport);
        //            //if (_authType != null)
        //            //{
        //            //    AddAuthorization(setup_message, _username, _password, _authType, _realm, _nonce, _url);
        //            //}

        //            //// Add SETUP message to list of mesages to send
        //            //_setupMessages.Add(setup_message);

        //        }
        //    }
        //    // Send the FIRST SETUP message and remove it from the list of Setup Messages
        //    _rtspClient.SendMessage(_setupMessages.Dequeue());            
        //}

        //private void Rtsp_ProcessResponseSetup(RtspResponse message)
        //{
        //    // If we get a reply to SETUP (which was our third command), then we
        //    // (i) check if the Interleaved Channel numbers have been modified by the camera (eg Panasonic cameras)
        //    // (ii) check if we have any more SETUP commands to send out (eg if we are doing SETUP for Video and Audio)
        //    // (iii) send a PLAY command if all the SETUP command have been sent

        //    // Got Reply to SETUP
        //    if (message.IsOk == false)
        //    {
        //        _logger.Debug($"{EntityID} Got Error in SETUP Reply " + message.ReturnCode + " " + message.ReturnMessage);
        //        return;
        //    }

        //    _logger.Debug($"{EntityID} Got reply from Setup. Session is " + message.Session);

        //    _session = message.Session; // Session value used with Play, Pause, Teardown and and additional Setups
        //    if (_keepaliveTimer != null && message.Timeout > 0 && message.Timeout > _keepaliveTimer.Interval / 1000)
        //    {
        //        _keepaliveTimer.Interval = message.Timeout * 1000 / 2;
        //    }

        //    bool isVideoChannel = message.OriginalRequest.RtspUri == _videoUri;
        //    bool isAudioChannel = message.OriginalRequest.RtspUri == _audioUrl;

        //    // Check the Transport header
        //    var transportString = message.Headers[RtspHeaderNames.Transport];
        //    if (transportString is not null)
        //    {
        //        RtspTransport transport = RtspTransport.Parse(transportString);

        //        // Check if Transport header includes Multicast
        //        if (transport.IsMulticast)
        //        {
        //            string multicastAddress = transport.Destination;
        //            var videoDataChannel = transport.Port?.First;
        //            var videoRtcpChannel = transport.Port?.Second;

        //            if (!string.IsNullOrEmpty(multicastAddress)
        //                && videoDataChannel.HasValue
        //                && videoRtcpChannel.HasValue)
        //            {
        //                // Create the Pair of UDP Sockets in Multicast mode
        //                if (isVideoChannel)
        //                {
        //                    videoRtpTransport = new MulticastUDPSocket(multicastAddress, videoDataChannel.Value, multicastAddress, videoRtcpChannel.Value);
        //                }
        //                else if (isAudioChannel)
        //                {
        //                    audioRtpTransport = new MulticastUDPSocket(multicastAddress, videoDataChannel.Value, multicastAddress, videoRtcpChannel.Value);
        //                }
        //            }
        //        }

        //        // check if the requested Interleaved channels have been modified by the camera
        //        // in the SETUP Reply (Panasonic have a camera that does this)
        //        if (transport.LowerTransport == RtspTransport.LowerTransportType.TCP)
        //        {
        //            RtpTcpTransport tcpTransport = null;
        //            if (isVideoChannel)
        //            {
        //                tcpTransport = videoRtpTransport as RtpTcpTransport;
        //            }

        //            if (isAudioChannel)
        //            {
        //                tcpTransport = audioRtpTransport as RtpTcpTransport;
        //            }
        //            if (tcpTransport is not null)
        //            {
        //                tcpTransport.DataChannel = transport.Interleaved?.First ?? tcpTransport.DataChannel;
        //                tcpTransport.ControlChannel = transport.Interleaved?.Second ?? tcpTransport.ControlChannel;
        //            }
        //        }
        //        else if (!transport.IsMulticast)
        //        {
        //            UDPSocket udpSocket = null;
        //            if (isVideoChannel)
        //            {
        //                udpSocket = videoRtpTransport as UDPSocket;
        //            }

        //            if (isAudioChannel)
        //            {
        //                udpSocket = audioRtpTransport as UDPSocket;
        //            }
        //            if (udpSocket is not null)
        //            {
        //                udpSocket.SetDataDestination(_uri!.Host, transport.ServerPort?.First ?? 0);
        //                udpSocket.SetControlDestination(_uri!.Host, transport.ServerPort?.Second ?? 0);
        //            }
        //        }

        //        if (isVideoChannel && videoRtpTransport is not null)
        //        {
        //            videoRtpTransport.DataReceived += VideoRtpTransport_DataReceived;
        //            videoRtpTransport.ControlReceived += RtcpControlDataReceived;
        //            videoRtpTransport.Start();
        //        }

        //        if (isAudioChannel && audioRtpTransport is not null)
        //        {
        //            audioRtpTransport.DataReceived += AudioRtpTransport_DataReceived;
        //            audioRtpTransport.ControlReceived += RtcpControlDataReceived;
        //            audioRtpTransport.Start();
        //        }
        //    }

        //    // Check if we have another SETUP command to send, then remote it from the list
        //    if (_setupMessages.Count > 0)
        //    {
        //        // send the next SETUP message, after adding in the 'session'
        //        RtspRequestSetup next_setup = _setupMessages.Dequeue();
        //        next_setup.Session = _session;
        //        _rtspClient.SendMessage(next_setup);
        //    }
        //    else
        //    {
        //        // Send PLAY
        //        Play();                
        //    }
        //}

        private void AudioRtpTransport_DataReceived(object? sender, RtspDataEventArgs e)
        {
            if (e.Data.Data.IsEmpty)
                return;

            OnRtpPacketReceived(ChannelTypes.Audio, e.Data.Data);

            var rtpPacket = new RTPPacket(e.Data.Data.Span);

            if (!audioPayloadProcessors.TryGetValue(rtpPacket.PayloadType, out IPayloadProcessor? audioPayloadProcessor))
            {
                _logger.Debug($"No audiopayload for this type.");
                return;
            }

            if (!audioPayloadMapping.TryGetValue(rtpPacket.PayloadType, out string? payloadName))
            {
                _logger.Debug($"No audiopayload mapping for this type.");
                return;
            }

            RawMediaFrame rawMediaFrame = audioPayloadProcessor.ProcessPacket(rtpPacket); // this will cache the Packets until there is a Frame

            if (rawMediaFrame.Any())
            {
                OnNalUnitExtracted(ChannelTypes.Audio, payloadName, rawMediaFrame.IsKeyFrame, rawMediaFrame.Data, rawMediaFrame.RtpTimestamp);
            }
        }

        private void VideoRtpTransport_DataReceived(object? sender, RtspDataEventArgs e)
        {
            if (e.Data.Data.IsEmpty)
                return;

            var rtpPacket = new RTPPacket(e.Data.Data.Span);

            OnRtpPacketReceived(ChannelTypes.Video, e.Data.Data);

            if (!videoPayloadProcessors.TryGetValue(rtpPacket.PayloadType, out IPayloadProcessor? videoPayloadProcessor))
            {
                _logger.Warning($"No videopayload for this type.");
                return;
            }

            if (!videoPayloadMapping.TryGetValue(rtpPacket.PayloadType, out string? payloadName))
            {
                _logger.Warning($"No videopayload mapping for this type.");
                return;
            }
            if (videoPayloadProcessor is null)
            {
                _logger.Warning("No video Processor");
                return;
            }

            RawMediaFrame rawMediaFrame = videoPayloadProcessor.ProcessPacket(rtpPacket); // this will cache the Packets until there is a Frame
            if (rawMediaFrame.Any())
            {
                OnNalUnitExtracted(ChannelTypes.Video, payloadName, rawMediaFrame.IsKeyFrame, rawMediaFrame.Data, rawMediaFrame.RtpTimestamp);
            }
        }

        //private void Rtsp_ProcessResponsePlay(RtspResponse message)
        //{
        //    // If we get a reply to PLAY (which was our fourth command), then we should have video being received

        //    // Got Reply to PLAY
        //    if (message.IsOk == false)
        //    {
        //        _logger.Debug($"{VideoSourceID} Got Error in PLAY Reply " + message.ReturnCode + " " + message.ReturnMessage);
        //        _readyEvent.Set();
        //        return;
        //    }

        //    _logger.Debug($"{VideoSourceID} Got reply from Play  " + message.Command);
        //    _readyEvent.Set();
        //    _playing = true;

        //    OnStarted();
        //}
        //private void Rtsp_ProcessResponseTeardown(RtspResponse message)
        //{
        //    Stop(RTSPClientStopReason.SESSION_CLOSED);
        //}

        //private void Rtsp_TryResendMessage(RtspResponse message)
        //{
        //    if (_resendMessageTrys < MAX_RESEND_MESSAGE_TRYS)
        //    {
        //        _resendMessageTrys++;
        //        RtspMessage resend_message = message.OriginalRequest.Clone() as RtspMessage;

        //        if (_authType != null)
        //        {
        //            AddAuthorization(resend_message, _username, _password, _authType, _realm, _nonce, _url);
        //        }
        //        _logger.Debug($"{VideoSourceID} Resend failed message " + resend_message.GetType().ToString());
        //        _rtspClient.SendMessage(resend_message);
        //    }
        //    else
        //    {
        //        Stop(RTSPClientStopReason.SESSION_FAILED);
        //    }
        //}

        // RTSP Messages are OPTIONS, DESCRIBE, SETUP, PLAY etc
        private void Rtsp_MessageReceived(object? sender, RTSPChunkEventArgs e)
        {
            if (e.Message is not RtspResponse message)
                return;

            _logger.Debug($"{EntityID} Received RTSP Message " + message.OriginalRequest?.ToString());

            // If message has a 401 - Unauthorised Error, then we re-send the message with Authorization
            // using the most recently received 'realm' and 'nonce'
            if (message.IsOk == false)

            {
                _logger.Debug($"{EntityID} Got Error in RTSP Reply " + message.ReturnCode + " " + message.ReturnMessage);

                if (message.ReturnCode == 401
                    && message.OriginalRequest?.Headers.ContainsKey(RtspHeaderNames.Authorization) == true
                    && message.OriginalRequest?.ContextData != keepAliveContext)
                {

                    // the authorization failed.
                    Stop(RTSPClientStopReason.AUTHORIZATION_FAILED);
                    return;
                }

                // Check if the Reply has an Authenticate header.
                if (message.ReturnCode == 401 && message.Headers.TryGetValue(RtspHeaderNames.WWWAuthenticate, out string? value))
                {
                    // Process the WWW-Authenticate header
                    // EG:   Basic realm="AProxy"
                    // EG:   Digest realm="AXIS_WS_ACCC8E3A0A8F", nonce="000057c3Y810622bff50b36005eb5efeae118626a161bf", stale=FALSE
                    // EG:   Digest realm="IP Camera(21388)", nonce="534407f373af1bdff561b7b4da295354", stale="FALSE"

                    string www_authenticate = value ?? string.Empty;
                    _authentication = Authentication.Create(_credentials, www_authenticate);
                    _logger.Debug("WWW Authorize parsed for {authentication}", _authentication);
                }

                RtspMessage? resend_message = message.OriginalRequest?.Clone() as RtspMessage;

                if (resend_message is not null)
                {
                    resend_message.AddAuthorization(_authentication, _uri!, _rtspSocket!.NextCommandIndex());
                    _rtspClient?.SendMessage(resend_message);
                }

                return;
            }

            switch (message.OriginalRequest)
            {
                case RtspRequestOptions when message.OriginalRequest.ContextData != keepAliveContext:
                    {
                        // Check the capabilities returned by OPTIONS
                        // The Public: header contains the list of commands the RTSP server supports
                        // Eg   DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE, OPTIONS, ANNOUNCE, RECORD, GET_PARAMETER]}
                        var supportedCommand = RTSPHeaderUtils.ParsePublicHeader(message);
                        _serverSupportsGetParameter = supportedCommand.Contains("GET_PARAMETER", StringComparer.OrdinalIgnoreCase);

                        _keepaliveTimer?.Enabled = true;

                        // Send DESCRIBE
                        RtspRequest describe_message = new RtspRequestDescribe
                        {
                            RtspUri = _uri,
                            Headers = { { "Accept", "application/sdp" } },
                        };
                        describe_message.AddAuthorization(_authentication, _uri!, _rtspSocket!.NextCommandIndex());
                        _rtspClient?.SendMessage(describe_message);
                    }
                    break;
                case RtspRequestDescribe:
                    {
                        HandleDescribeResponse(message);
                        break;
                    }
                case RtspRequestSetup:
                    {
                        HandleSetupResponse(message);
                    }
                    break;
                case RtspRequestPlay:
                    OnStarted();
                    break;
            }
        }

        private Uri? GetControlUri(Media media)
        {
            var attrib = media.Attributs.FirstOrDefault(a => a.Key == "control");
            if (attrib is null) return null;

            var sdpControl = attrib.Value;
            if (
                sdpControl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                || sdpControl.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase)
                || sdpControl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || sdpControl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // the "track" or "stream id"
                return new(sdpControl);
            }

            // add trailing / if necessary
            var baseUriWithTrailingSlash = _uri!.ToString().EndsWith('/') ? _uri : new($"{_uri}/");
            // relative path
            return new(baseUriWithTrailingSlash, sdpControl);
        }

        private void HandleSetupResponse(RtspResponse message)
        {
            Debug.Assert(message.OriginalRequest is RtspRequestSetup, "Expected a SETUP request");

            _logger.Debug("Got reply from Setup. Session is {session}", message.Session);

            // Session value used with Play, Pause, Teardown and and additional Setups
            _session = message.Session ?? "";
            if (_keepaliveTimer != null && message.Timeout > 0 && message.Timeout > _keepaliveTimer.Interval / 1000)
            {
                _keepaliveTimer.Interval = message.Timeout * 1000 / 2;
            }

            bool isVideoChannel = message.OriginalRequest.RtspUri != null && video_uris.Contains(message.OriginalRequest.RtspUri); // == video_uri;
            bool isAudioChannel = message.OriginalRequest.RtspUri != null && audio_uris.Contains(message.OriginalRequest.RtspUri); // == audio_uri;
            Debug.Assert(isVideoChannel || isAudioChannel, "Unknown channel response");

            // Check the Transport header
            var transportString = message.Headers[RtspHeaderNames.Transport];
            if (transportString is not null)
            {
                RtspTransport transport = RtspTransport.Parse(transportString);

                // Check if Transport header includes Multicast
                if (transport.IsMulticast)
                {
                    string? multicastAddress = transport.Destination;
                    var videoDataChannel = transport.Port?.First;
                    var videoRtcpChannel = transport.Port?.Second;

                    if (!string.IsNullOrEmpty(multicastAddress)
                        && videoDataChannel.HasValue
                        && videoRtcpChannel.HasValue)
                    {
                        // Create the Pair of UDP Sockets in Multicast mode
                        if (isVideoChannel)
                        {
                            videoRtpTransport = new MulticastUDPSocket(multicastAddress, videoDataChannel.Value,
                                multicastAddress, videoRtcpChannel.Value);
                        }
                        else if (isAudioChannel)
                        {
                            audioRtpTransport = new MulticastUDPSocket(multicastAddress, videoDataChannel.Value,
                                multicastAddress, videoRtcpChannel.Value);
                        }
                    }
                }

                // check if the requested Interleaved channels have been modified by the camera
                // in the SETUP Reply (Panasonic have a camera that does this)
                if (transport.LowerTransport == RtspTransport.LowerTransportType.TCP)
                {
                    RtpTcpTransport? tcpTransport = null;
                    if (isVideoChannel)
                    {
                        tcpTransport = videoRtpTransport as RtpTcpTransport;
                    }

                    if (isAudioChannel)
                    {
                        tcpTransport = audioRtpTransport as RtpTcpTransport;
                    }

                    if (tcpTransport is not null)
                    {
                        tcpTransport.DataChannel = transport.Interleaved?.First ?? tcpTransport.DataChannel;
                        tcpTransport.ControlChannel = transport.Interleaved?.Second ?? tcpTransport.ControlChannel;
                    }
                }
                else if (!transport.IsMulticast)
                {
                    UDPSocket? udpSocket = null;
                    if (isVideoChannel)
                    {
                        udpSocket = videoRtpTransport as UDPSocket;
                    }

                    if (isAudioChannel)
                    {
                        udpSocket = audioRtpTransport as UDPSocket;
                    }

                    if (udpSocket is not null)
                    {
                        udpSocket.SetDataDestination(_uri!.Host, transport.ServerPort?.First ?? 0);
                        udpSocket.SetControlDestination(_uri!.Host, transport.ServerPort?.Second ?? 0);
                    }
                }

                if (isVideoChannel && videoRtpTransport is not null)
                {
                    videoRtpTransport.DataReceived += VideoRtpTransport_DataReceived;
                    videoRtpTransport.ControlReceived += RtcpControlDataReceived;
                    videoRtpTransport.Start();
                }

                if (isAudioChannel && audioRtpTransport is not null)
                {
                    audioRtpTransport.DataReceived += AudioRtpTransport_DataReceived;
                    audioRtpTransport.ControlReceived += RtcpControlDataReceived;
                    audioRtpTransport.Start();
                }
            }

            // Check if we have another SETUP command to send, then remote it from the list
            if (_setupMessages.Count > 0)
            {
                // send the next SETUP message, after adding in the 'session'
                RtspRequestSetup nextSetup = _setupMessages.Dequeue();
                nextSetup.Session = _session;
                _rtspClient?.SendMessage(nextSetup);
            }
            else
            {
                _ready = true;
                // use the event for setup completed, so the main program can call the Play command with or without the playback request.
                //SetupMessageCompleted?.Invoke(this, EventArgs.Empty);

                // Send PLAY
                Play();
            }
        }


        private void HandleDescribeResponse(RtspResponse message)
        {
            if (message.Data.IsEmpty)
            {
                _logger.Warning("Invalid SDP");
                return;
            }

            // Examine the SDP
            _logger.Debug("Sdp:\n{sdp}", Encoding.UTF8.GetString(message.Data.Span));

            SdpFile sdp_data;
            using (var sdpStream = new MemoryStream(message.Data.ToArray()))
            {
                sdp_data = SdpFile.ReadLoose(new StreamReader(sdpStream));
            }

            // For old sony cameras, we need to use the control uri from the sdp
            var customControlUri = sdp_data.Attributs.FirstOrDefault(x => x.Key == "control");
            if (customControlUri is not null && !string.Equals(customControlUri.Value, "*"))
            {
                _uri = new Uri(_uri!, customControlUri.Value);
            }

            // Process each 'Media' Attribute in the SDP (each sub-stream)
            // to look for first supported video substream
            if (clientWantsVideo)
            {
                foreach (Media media in sdp_data.Medias.Where(m => m.MediaType == Media.MediaTypes.video))
                {
                    int video_payload = -1;
                    IPayloadProcessor? videoPayloadProcessor = null;

                    // search the attributes for control, rtpmap and fmtp
                    // holds SPS and PPS in base64 (h264 video)
                    AttributFmtp? fmtp = media.Attributs.FirstOrDefault(x => x.Key == "fmtp") as AttributFmtp;
                    AttributRtpMap? rtpmap = media.Attributs.FirstOrDefault(x => x.Key == "rtpmap") as AttributRtpMap;
                    Uri? video_uri = GetControlUri(media);

                    if (!string.IsNullOrEmpty(_setupPreferredVideoRtpMap) && !(rtpmap?.EncodingName?.Equals(_setupPreferredVideoRtpMap, StringComparison.OrdinalIgnoreCase) ?? true))
                    {
                        _logger.Debug($"Not requested one.");
                        continue;
                    }

                    int fmtpPayloadNumber = -1;
                    if (fmtp != null)
                    {
                        fmtpPayloadNumber = fmtp.PayloadNumber;
                    }

                    if (int.TryParse(rtpmap?.ClockRate, NumberStyles.Integer, NumberFormatInfo.CurrentInfo, out int clockRate))
                    {
                        // a rtsp client can have a single clockrate by url (I hope)...
                        _videoClockRate = clockRate;
                    }

                    // extract h265 donl if available...
                    bool h265HasDonl = false;

                    if ((rtpmap?.EncodingName?.ToUpper().Equals("H265") ?? false) &&
                        !string.IsNullOrEmpty(fmtp?.FormatParameter))
                    {
                        var param = H266Parameters.Parse(fmtp.FormatParameter);
                        if (param.ContainsKey("sprop-max-don-diff") &&
                            int.TryParse(param["sprop-max-don-diff"], out int donl) && donl > 0)
                        {
                            h265HasDonl = true;
                        }
                    }

                    // some cameras are really mess with the payload type.
                    // must check also the rtpmap for the corrent format to load (sending an h265 payload when giving an h264 stream [Some Bosch camera])

                    string payloadName = string.Empty;
                    if (rtpmap != null
                        && (((fmtpPayloadNumber > -1 && rtpmap.PayloadNumber == fmtpPayloadNumber) ||
                             fmtpPayloadNumber == -1)
                            && rtpmap.EncodingName != null))
                    {
                        // found a valid codec
                        payloadName = rtpmap.EncodingName.ToUpper();
                        videoPayloadProcessor = payloadName switch
                        {
                            "H264" => new H264Payload(),
                            "H265" => new H265Payload(h265HasDonl),
                            "JPEG" => new JPEGPayload(),
                            "MP4V-ES" => new RawPayload(),
                            _ => null,
                        };
                        video_payload = media.PayloadType;
                    }
                    else
                    {
                        video_payload = media.PayloadType;
                        if (media.PayloadType < 96)
                        {
                            // PayloadType is a static value, so we can use it to determine the codec
                            videoPayloadProcessor = media.PayloadType switch
                            {
                                26 => new JPEGPayload(),
                                33 => new MP2TransportPayload(),
                                _ => null,
                            };
                            payloadName = media.PayloadType switch
                            {
                                26 => "JPEG",
                                33 => "MP2T",
                                _ => string.Empty,
                            };
                        }
                        else if (rtpmap != null)
                        {
                            payloadName = rtpmap.EncodingName?.ToUpperInvariant() ?? string.Empty;
                            videoPayloadProcessor = payloadName switch
                            {
                                "H264" => new H264Payload(),
                                "H265" => new H265Payload(h265HasDonl),
                                "JPEG" => new JPEGPayload(),
                                "MP4V-ES" => new RawPayload(),
                                _ => null,
                            };
                            video_payload = media.PayloadType;
                        }
                    }

                    IStreamConfigurationData? streamConfigurationData = null;

                    if (videoPayloadProcessor is H264Payload && fmtp?.FormatParameter is not null)
                    {
                        // If the rtpmap contains H264 then split the fmtp to get the sprop-parameter-sets which hold the SPS and PPS in base64
                        var param = H264Parameters.Parse(fmtp.FormatParameter);
                        if (param.SpropParameterSets.Count >= 2)
                        {
                            streamConfigurationData = new H264StreamConfigurationData
                            {
                                OutOfBandNal = [.. param.SpropParameterSets]
                            };
                        }
                    }
                    else if (videoPayloadProcessor is H265Payload && fmtp?.FormatParameter is not null)
                    {
                        // If the rtpmap contains H265 then split the fmtp to get the sprop-vps, sprop-sps and sprop-pps
                        // The RFC makes the VPS, SPS and PPS OPTIONAL so they may not be present. In which we pass back NULL values
                        var param = H265Parameters.Parse(fmtp.FormatParameter);
                        streamConfigurationData = new H265StreamConfigurationData()
                        {
                            OutOfBandNal = [.. param.SpropParameterSets]
                        };
                    }

                    // Send the SETUP RTSP command if we have a matching Payload Decoder
                    if (videoPayloadProcessor is not null)
                    {
                        var transport = CalculateTransport(videoRtpTransport);

                        // Generate SETUP messages
                        if (transport != null)
                        {
                            RtspRequestSetup setupMessage = new()
                            {
                                RtspUri = video_uri
                            };
                            setupMessage.AddTransport(transport);
                            setupMessage.AddAuthorization(_authentication, _uri!, _rtspSocket!.NextCommandIndex());
                            if (_playbackSession)
                            {
                                setupMessage.AddRequireOnvifRequest();
                            }

                            // Add SETUP message to list of messages to send
                            _setupMessages.Enqueue(setupMessage);

                            OnStreamConfigured(ChannelTypes.Video, payloadName, streamConfigurationData);
                        }

                        if (!videoPayloadProcessors.TryGetValue(video_payload, out _))
                        {
                            videoPayloadProcessors.Add(video_payload, videoPayloadProcessor);
                        }
                        if (!videoPayloadMapping.TryGetValue(video_payload, out _))
                        {
                            videoPayloadMapping.Add(video_payload, payloadName);
                        }

                        if (video_uri != null && !video_uris.Contains(video_uri)) { video_uris.Add(video_uri); }

                        if (!string.IsNullOrEmpty(_setupPreferredVideoRtpMap))
                        {
                            // break here, the requested one has been setup.
                            // there should be no other video stream setup now...
                            break;
                        }
                    }
                }

                if (videoPayloadProcessors.Count == 0)
                {
                    // send an info about video not available?
                    //NoVideoPayload?.Invoke(this, EventArgs.Empty);
                }
            }

            if (clientWantsAudio)
            {
                foreach (var media in sdp_data.Medias.Where(m => m.MediaType == Media.MediaTypes.audio))
                {
                    int audio_payload = -1;
                    string audio_codec;
                    IPayloadProcessor? audioPayloadProcessor = null;

                    // search the attributes for control, rtpmap and fmtp
                    AttributFmtp? fmtp = media.Attributs.FirstOrDefault(x => x.Key == "fmtp") as AttributFmtp;
                    AttributRtpMap? rtpmap = media.Attributs.FirstOrDefault(x => x.Key == "rtpmap") as AttributRtpMap;

                    Uri? audio_uri = GetControlUri(media);
                    audio_payload = media.PayloadType;

                    IStreamConfigurationData? streamConfigurationData = null;
                    if (media.PayloadType < 96)
                    {
                        // fixed payload type
                        (audioPayloadProcessor, audio_codec) = media.PayloadType switch
                        {
                            0 => (new G711Payload(), "PCMU"),
                            8 => (new G711Payload(), "PCMA"),
                            _ => (null, ""),
                        };
                    }
                    else
                    {
                        // dynamic payload type
                        audio_codec = rtpmap?.EncodingName?.ToUpper() ?? string.Empty;
                        audioPayloadProcessor = audio_codec switch
                        {
                            // Create AAC RTP Parser
                            // Example fmtp is "96 profile-level-id=1;mode=AAC-hbr;sizelength=13;indexlength=3;indexdeltalength=3;config=1490"
                            // Example fmtp is ""96 streamtype=5;profile-level-id=1;mode=AAC-hbr;sizelength=13;indexlength=3;indexdeltalength=3;config=1210"
                            "MPEG4-GENERIC" when fmtp?["mode"].ToLower() == "aac-hbr" => new AACPayload(fmtp["config"]),
                            "PCMA" => new G711Payload(),
                            "PCMU" => new G711Payload(),
                            "AMR" => new AMRPayload(),
                            _ => null,
                        };
                        if (audioPayloadProcessor is AACPayload aacPayloadProcessor)
                        {
                            audio_codec = "AAC";
                            streamConfigurationData = new AacStreamConfigurationData()
                            {
                                ObjectType = aacPayloadProcessor.ObjectType,
                                FrequencyIndex = aacPayloadProcessor.FrequencyIndex,
                                SamplingFrequency = aacPayloadProcessor.SamplingFrequency,
                                ChannelConfiguration = aacPayloadProcessor.ChannelConfiguration
                            };
                        }
                    }

                    // Send the SETUP RTSP command if we have a matching Payload Decoder
                    if (audioPayloadProcessor is not null)
                    {
                        RtspTransport? transport = CalculateTransport(audioRtpTransport);

                        // Generate SETUP messages
                        if (transport != null)
                        {
                            RtspRequestSetup setupMessage = new()
                            {
                                RtspUri = audio_uri,
                            };
                            setupMessage.AddTransport(transport);
                            setupMessage.AddAuthorization(_authentication, _uri!, _rtspSocket!.NextCommandIndex());
                            //if (_playbackSession)
                            //{
                            //    setupMessage.AddRequireOnvifRequest();
                            //    setupMessage.AddRateControlOnvifRequest(false);
                            //}

                            // Add SETUP message to list of messages to send
                            _setupMessages.Enqueue(setupMessage);
                            //NewAudioStream?.Invoke(this, new(_audioCodec, streamConfigurationData));
                            OnStreamConfigured(ChannelTypes.Audio, audio_codec, streamConfigurationData);
                        }

                        if (!audioPayloadProcessors.TryGetValue(audio_payload, out _))
                        {
                            audioPayloadProcessors.Add(audio_payload, audioPayloadProcessor);
                        }
                        if (!audioPayloadMapping.TryGetValue(audio_payload, out _))
                        {
                            audioPayloadMapping.Add(audio_payload, audio_codec);
                        }

                        if (audio_uri != null && !audio_uris.Contains(audio_uri)) { audio_uris.Add(audio_uri); }

                        if (!string.IsNullOrEmpty(_setupPreferredAudioRtpMap))
                        {
                            // break here, the requested one has been setup.
                            // there should be no other video stream setup now...
                            break;
                        }
                    }
                }
            }

            if (_setupMessages.Count == 0)
            {
                // No SETUP messages were generated
                // So we cannot continue
                throw new ApplicationException("Unable to setup media stream");
            }

            // Send the FIRST SETUP message and remove it from the list of Setup Messages
            _rtspClient?.SendMessage(_setupMessages.Dequeue());
        }

        //private void RTSP_SocketException_Raised(object sender, RTSPSocketExceptionEventArgs e)
        //{
        //    RTSPListener listener = sender as RTSPListener;
        //    SocketException ex = e.Ex;

        //    HandleClientSocketException(ex, listener);
        //}

        //private void RTSP_Disconnected(object sender, EventArgs e)
        //{
        //    Stop(RTSPClientStopReason.CONNECTION_LOST);
        //}

        //internal void HandleClientSocketException(SocketException se, RTSPListener listener)
        //{
        //    if (se == null) return;

        //    switch (se.SocketErrorCode)
        //    {
        //        case SocketError.TimedOut:
        //        case SocketError.ConnectionAborted:
        //        case SocketError.ConnectionReset:
        //        case SocketError.Disconnecting:
        //        case SocketError.Shutdown:
        //        case SocketError.NotConnected:
        //            {
        //                Stop(RTSPClientStopReason.CONNECTION_LOST);
        //                return;
        //            }
        //        default:
        //            {
        //                _logger.Error(se.Message);
        //                return;
        //            }
        //    }
        //}

        void SendKeepAlive(object? sender, System.Timers.ElapsedEventArgs e)
        {
            // Send Keepalive message
            // The ONVIF Standard uses SET_PARAMETER as "an optional method to keep an RTSP session alive"
            // RFC 2326 (RTSP Standard) says "GET_PARAMETER with no entity body may be used to test client or server liveness("ping")"

            // This code uses GET_PARAMETER (unless OPTIONS report it is not supported, and then it sends OPTIONS as a keepalive)
            RtspRequest keepAliveMessage =
                    _serverSupportsGetParameter
                    ? new RtspRequestGetParameter
                    {
                        RtspUri = _uri,
                        Session = _session
                    }
                    : new RtspRequestOptions
                    {
                        // RtspUri = new Uri(url)
                    };

            keepAliveMessage.ContextData = keepAliveContext;
            keepAliveMessage.AddAuthorization(_authentication, _uri!, _rtspSocket!.NextCommandIndex());
            _rtspClient?.SendMessage(keepAliveMessage);
        }

        // Generate Basic or Digest Authorization
        //public void AddAuthorization(RtspMessage message, string username, string password,
        //    string auth_type, string realm, string nonce, string url)
        //{

        //    if (username == null || username.Length == 0) return;
        //    if (password == null || password.Length == 0) return;
        //    if (realm == null || realm.Length == 0) return;
        //    if (auth_type.Equals("Digest") && (nonce == null || nonce.Length == 0)) return;

        //    if (auth_type.Equals("Basic"))
        //    {
        //        byte[] credentials = System.Text.Encoding.UTF8.GetBytes(username + ":" + password);
        //        String credentials_base64 = Convert.ToBase64String(credentials);
        //        String basic_authorization = "Basic " + credentials_base64;

        //        message.Headers.Add(RtspHeaderNames.Authorization, basic_authorization);

        //        return;
        //    }
        //    else if (auth_type.Equals("Digest"))
        //    {

        //        string method = message.Method; // DESCRIBE, SETUP, PLAY etc

        //        MD5 md5 = System.Security.Cryptography.MD5.Create();
        //        String hashA1 = CalculateMD5Hash(md5, username + ":" + realm + ":" + password);
        //        String hashA2 = CalculateMD5Hash(md5, method + ":" + url);
        //        String response = CalculateMD5Hash(md5, hashA1 + ":" + nonce + ":" + hashA2);

        //        const String quote = "\"";
        //        String digest_authorization = "Digest username=" + quote + username + quote + ", "
        //            + "realm=" + quote + realm + quote + ", "
        //            + "nonce=" + quote + nonce + quote + ", "
        //            + "uri=" + quote + url + quote + ", "
        //            + "response=" + quote + response + quote;

        //        message.Headers.Add(RtspHeaderNames.Authorization, digest_authorization);

        //        return;
        //    }
        //    else
        //    {
        //        return;
        //    }

        //}

        // MD5 (lower case)
        //public string CalculateMD5Hash(MD5 md5_session, string input)
        //{
        //    byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        //    byte[] hash = md5_session.ComputeHash(inputBytes);

        //    StringBuilder output = new StringBuilder();
        //    for (int i = 0; i < hash.Length; i++)
        //    {
        //        output.Append(hash[i].ToString("x2"));
        //    }

        //    return output.ToString();
        //}


        #endregion
    }
}
