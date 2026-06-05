using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Common.Primitives.Media;
using OPNX.Lib.Streaming.RTSP.Commons;
using OPNX.Lib.Streaming.RTSP.Commons.Interfaces;
using OPNX.Lib.Streaming.RTSP.Messages;
using OPNX.Lib.Streaming.RTSP.RTCP;
using OPNX.Lib.Streaming.RTSP.Sdp;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using static OPNX.Lib.Streaming.RTSP.Sdp.Media;

namespace OPNX.Lib.Streaming.RTSP
{
    // RTSP Server Example (c) Roger Hardiman, 2016, 2018, 2020
    // Released uder the MIT Open Source Licence
    //
    // Re-uses some code from the Multiplexer example of SharpRTSP
    //
    // Creates a server to listen for RTSP Commands (eg OPTIONS, DESCRIBE, SETUP, PLAY)
    // Accepts SPS/PPS/NAL H264 video data and sends out to RTSP clients

    public class RTSPServer : IDisposable
    {
        #region Fields
        const uint global_ssrc = 0x4321FADE; // 8 hex digits
        const int DEFAULT_RTSP_TIMEOUT = 60;

        private readonly IRtspListenSocket _RtspServerListener;

        //private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private CancellationTokenSource? _Stopping;
        //private Thread _ListenTread;
        private Task? _ListenTask;

        //byte[] rawSps;
        //byte[] rawPs;        

        //private ushort audioSequenceNumber = (ushort)Random.Shared.Next();
        //private ushort videoSequenceNumber = (ushort)Random.Shared.Next();
        private static readonly Random _random = new();
        private readonly ushort audioSequenceNumber = (ushort)_random.Next(ushort.MinValue, ushort.MaxValue + 1);
        private readonly ushort videoSequenceNumber = (ushort)_random.Next(ushort.MinValue, ushort.MaxValue + 1);

        private readonly ConcurrentDictionary<Guid, RTSPConnection> dicRTSPConnection = new();

        int session_handle = 1;
        private readonly NetworkCredential credential;
        private readonly Authentication? auth;

        private readonly bool _useRTSPS = false;
        private readonly string _pfxFile = "";
        private readonly int _rtspPort = 0;
        #endregion

        #region Constructors
        /// <summary>
        /// 
        /// </summary>
        /// <param name="portNumber"></param>
        public RTSPServer(int portNumber, ILogger? logger = null)
            : this(portNumber, string.Empty, string.Empty, logger)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="portNumber"></param>
        /// <param name="userName"></param>
        /// <param name="password"></param>
        public RTSPServer(int portNumber, string userName, string password, ILogger? logger = null)
            : this(portNumber, userName, password, false, logger)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPServer"/> class in RTSPS (TLS) Mode.
        /// </summary>
        /// <param name="portNumber">A numero port.</param>
        /// <param name="username">username.</param>
        /// <param name="password">password.</param>
        /// <param name="pfxFile">pfxFile used for RTSPS TLS Server Certificate.</param>
        public RTSPServer(int portNumber, string username, string password, string pfxFile, ILogger? logger = null)
            : this(portNumber, username, password, false, logger)
        {
            if (string.IsNullOrEmpty(pfxFile))
            {
                throw new ArgumentOutOfRangeException(nameof(pfxFile), "PFX File must not be null or empty for RTSPS mode");
            }
            _useRTSPS = true;
            _pfxFile = pfxFile;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPServer"/> class.
        /// </summary>
        /// <param name="portNumber">A numero port.</param>
        /// <param name="username">username.</param>
        /// <param name="password">password.</param>
        public RTSPServer(int portNumber, string username, string password, bool useHttpTunnel, ILogger? logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
            if (portNumber < IPEndPoint.MinPort || portNumber > IPEndPoint.MaxPort)
            {
                throw new ArgumentOutOfRangeException(nameof(portNumber), portNumber, "Port number must be between System.Net.IPEndPoint.MinPort and System.Net.IPEndPoint.MaxPort");
            }

            Contract.EndContractBlock();

            //_loggerFactory = loggerFactory;
            //_logger = loggerFactory?.CreateLogger<RTSPServer>();

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                const string realm = "SIMPLE_RTSP_SERVER";
                credential = new(username, password);
                // The original RFC for Digest Auth used the MD5 Hash Algorithm
                // There is a newer RFC that allows the SHA-256 Hash Algorithm
                // Enable SHA-256 for some FIPS setups
                // There is also an ONVIF "MD5 then SHA-256" which sends two WWW-Autneiticate headers
                // which is not yet supported.
                var useSHA256 = false;
                var algorithm = (useSHA256 ? AuthenticationDigest.HashAlgorithm.SHA256 : AuthenticationDigest.HashAlgorithm.MD5);
                auth = new AuthenticationDigest(credential, realm, new Random().Next(100000000, 999999999).ToString(), string.Empty, algorithm);
            }
            else
            {
                credential = new();
                auth = null;
            }

            RtspUtils.RegisterUri();
            X509Certificate2? certificate = null;
            if (_useRTSPS)
            {
                //certificate = X509CertificateLoader.LoadPkcs12FromFile(_pfxFile, "");
                //certificate = new X509Certificate2(_pfxFile, (string?)null,
                //                                    X509KeyStorageFlags.MachineKeySet |
                //                                    X509KeyStorageFlags.PersistKeySet |
                //                                    X509KeyStorageFlags.Exportable);
                certificate = X509CertificateLoader.LoadPkcs12FromFile(_pfxFile, password: null,
                    keyStorageFlags: X509KeyStorageFlags.MachineKeySet |
                    X509KeyStorageFlags.PersistKeySet |
                    X509KeyStorageFlags.Exportable);
            }

            var tcpListener = new TcpListener(IPAddress.Any, portNumber);
            _RtspServerListener = useHttpTunnel switch
            {
                true when certificate is null => new RtspOverHttpListenSocket(tcpListener),
                true => new RtspOverHttpTLSListenSocket(tcpListener, certificate),
                false when certificate is null => new RtspListenSocket(tcpListener),
                false => new RtspTlsListenSocket(tcpListener, certificate),
            };

            _rtspPort = portNumber;

            StartListen();
        }
        #endregion

        #region Properties
        public int Port => _rtspPort;
        public int RtspTimeOut { get; set; } = DEFAULT_RTSP_TIMEOUT;
        #endregion

        #region Events
        public event RtspConnectionAddedHandler? ConnectionAdded;
        public event RtspConnectionRemovedHandler? ConnectionRemoved;
        //public event RtspProvideSdpDataHandler ProvideSdpData; 
        #endregion

        #region Public Methods
        public async Task SendRtpAudioDataAsync(Guid rtspConnectionID, uint rtpTimestamp, ReadOnlyMemory<byte> rtpData)
        {
            await SendRTPDataAsync(rtspConnectionID, MediaTypes.audio, rtpTimestamp, rtpData);
        }

        public async Task SendRtpVideoDataAsync(Guid rtspConnectionID, uint rtpTimestamp, ReadOnlyMemory<byte> rtpData)
        {
            await SendRTPDataAsync(rtspConnectionID, MediaTypes.video, rtpTimestamp, rtpData);
        }

        public async Task SendRTPDataAsync(Guid rtspConnectionID, MediaTypes mediaType, uint rtpTimestamp, ReadOnlyMemory<byte> rtpData)
        {
            if (dicRTSPConnection.TryGetValue(rtspConnectionID, out var rtspConnection))
            {
                await SendRTPDataAsync(rtspConnection, mediaType, rtpTimestamp, rtpData);
            }
        }

        public async Task SendRTPDataAsync(RTSPConnection rtspConnection, MediaTypes mediaType, uint rtpTimestamp, ReadOnlyMemory<byte> rtpData)
        {
            if (!rtspConnection.play)
                return;

            // 연결 상태 확인
            if (CheckTimeout(rtspConnection))
            {
                RemoveSession(rtspConnection.ConnectionID);
                return;
            }

            // 대상 스트림 선택
            RTPStream? stream = mediaType switch
            {
                MediaTypes.audio when rtspConnection.audio?.rtpChannel != null => rtspConnection.audio,
                MediaTypes.video when rtspConnection.video?.rtpChannel != null => rtspConnection.video,
                _ => null
            };
            if (stream is null) return;

            // RTCP 송신 (필요시)
            if (stream.mustSendRtcpPacket)
            {
                bool rtcpSent = await SendRTCPAsync(rtpTimestamp, rtspConnection, stream);
                if (!rtcpSent)
                {
                    RemoveSession(rtspConnection.ConnectionID);
                    return;
                }
            }

            var rtpChannel = stream.rtpChannel;
            if (rtpChannel is null) return;

            try
            {
                // RTP 패킷 전송
                await rtpChannel.WriteToDataPortAsync(rtpData);
                if (rtspConnection?.video?.rtpChannel is RtpTcpTransport)
                {
                    // for tcp transport a successful write means the connection is alive
                    rtspConnection.UpdateKeepAlive();
                }

                stream.packetCount++;
                stream.octetCount += (uint)rtpData.Length;
            }
            catch (Exception ex)
            {
                _logger.LogError($"RTP Write Exception: {ex.Message}");
                _logger.LogError($"Error writing to listener {rtspConnection.Listener.RemoteEndPoint}");
                RemoveSession(rtspConnection.ConnectionID);
            }
        }

        public void SendRtpAudioData(Guid rtspConnectionID, uint rtpTimestamp, ReadOnlySpan<byte> rtpData)
        {
            SendRTPData(rtspConnectionID, MediaTypes.audio, rtpTimestamp, rtpData);
        }

        public void SendRtpVideoData(Guid rtspConnectionID, uint rtpTimestamp, ReadOnlySpan<byte> rtpData)
        {
            SendRTPData(rtspConnectionID, MediaTypes.video, rtpTimestamp, rtpData);
        }

        public void SendRTPData(Guid rtspConnectionID, MediaTypes mediaType, uint rtpTimestamp, ReadOnlySpan<byte> rtpData)
        {
            if (dicRTSPConnection.TryGetValue(rtspConnectionID, out var rtspConnection))
            {
                SendRTPData(rtspConnection, mediaType, rtpTimestamp, rtpData);
            }
        }

        public void SendRTPData(RTSPConnection rtspConnection, MediaTypes mediaType, uint rtpTimestamp, ReadOnlySpan<byte> rtpData)
        {
            if (!rtspConnection.play)
                return;

            // 연결 상태 확인
            if (CheckTimeout(rtspConnection))
            {
                RemoveSession(rtspConnection.ConnectionID);
                return;
            }

            // 대상 스트림 선택
            RTPStream? stream = mediaType switch
            {
                MediaTypes.audio when rtspConnection.audio?.rtpChannel != null => rtspConnection.audio,
                MediaTypes.video when rtspConnection.video?.rtpChannel != null => rtspConnection.video,
                _ => null
            };
            if (stream is null) return;

            // RTCP 송신 (필요시)
            if (stream.mustSendRtcpPacket)
            {
                bool rtcpSent = SendRTCP(rtpTimestamp, rtspConnection, stream);
                if (!rtcpSent)
                {
                    RemoveSession(rtspConnection.ConnectionID);
                    return;
                }
            }

            var rtpChannel = stream.rtpChannel;
            if (rtpChannel is null) return;

            try
            {
                // RTP 패킷 전송
                rtpChannel.WriteToDataPort(rtpData);

                stream.packetCount++;
                stream.octetCount += (uint)rtpData.Length;
            }
            catch (Exception ex)
            {
                _logger.LogError($"RTP Write Exception: {ex.Message}");
                _logger.LogError($"Error writing to listener {rtspConnection.Listener.RemoteEndPoint}");
                RemoveSession(rtspConnection.ConnectionID);
            }
        }

        public void CloseConnection(Guid connectionID)
        {
            RemoveSession(connectionID);
        }
        #endregion

        #region Private / Protected Methods

        /// <summary>
        /// Starts the listen.
        /// </summary>
        private void StartListen()
        {
            _RtspServerListener.Start();

            _Stopping = new CancellationTokenSource();
            _ListenTask = Task.Factory.StartNew(async () => await AcceptConnectionAsync(_Stopping.Token).ConfigureAwait(false),
                _Stopping.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Current);
        }

        private void StopListen()
        {
            _RtspServerListener.Stop();
            _Stopping?.Cancel();
            _ListenTask?.Wait();
        }

        /// <summary>
        /// Accepts the connection.
        /// </summary>
        private async Task AcceptConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Wait for an incoming TCP Connection
                        IRtspTransport rtsp_socket = await _RtspServerListener.AcceptAsync(cancellationToken);

                        var newListener = new RTSPListener(rtsp_socket);
                        newListener.MessageReceived += RTSPMessageReceived;

                        RTSPConnection newConnection = new()
                        {
                            Listener = newListener,
                        };

                        newConnection.Listener.ConnectionID = newConnection.ConnectionID;

                        dicRTSPConnection.TryAdd(newConnection.ConnectionID, newConnection);

                        newListener.Start();
                    }
                    catch (OperationCanceledException)
                    {

                    }
                    catch (AuthenticationException)
                    {
                        _logger.LogDebug("Invalid client (maybe RTSP on RTSPS socket)");
                    }
                }
            }
            catch (SocketException)
            {
                // _logger.Warn("Got an error listening, I have to handle the stopping which also throw an error", error);
            }
        }

        // Process each RTSP message that is received
        private void RTSPMessageReceived(object? sender, RTSPChunkEventArgs e)
        {
            // Cast the 'sender' and 'e' into the RTSP Listener (the Socket) and the RTSP Message
            RTSPListener listener = sender as RTSPListener ?? throw new ArgumentException("Invalid sender", nameof(sender));

            if (e.Message is not RtspRequest message)
            {
                _logger.LogDebug("RTSP message is not a request. Invalid dialog.");
                return;
            }

            _logger.LogDebug($"RTSP message received {message}");

            // Check if the RTSP Message has valid authentication (validating against username,password,realm and nonce)
            // skip authentication for OPTIONS for VLC
            if (auth != null && message is not RtspRequestOptions)
            {
                if (message.Headers.ContainsKey("Authorization"))
                {
                    // The Header contained Authorization
                    // Check the message has the correct Authorization
                    // If it does not have the correct Authorization then close the RTSP connection
                    if (!auth.IsValid(message))
                    {
                        // Send a 401 Authentication Failed reply, then close the RTSP Socket
                        RtspResponse authorization_response = message.CreateResponse();
                        authorization_response.AddHeader("WWW-Authenticate: " + auth.GetServerResponse()); // 'Basic' or 'Digest'
                        authorization_response.ReturnCode = 401;
                        listener.SendMessage(authorization_response);
                        RemoveSession(listener.ConnectionID);
                        listener.Dispose();
                        return;
                    }
                }
                else
                {
                    // Send a 401 Authentication Failed with extra info in WWW-Authenticate
                    // to tell the Client if we are using Basic or Digest Authentication
                    RtspResponse authorization_response = message.CreateResponse();
                    authorization_response.AddHeader("WWW-Authenticate: " + auth.GetServerResponse());
                    authorization_response.ReturnCode = 401;
                    listener.SendMessage(authorization_response);
                    return;
                }
            }

            dicRTSPConnection.TryGetValue(listener.ConnectionID, out var rtspConnection);

            // Handle message without session
            switch (message)
            {
                case RtspRequestOptions:
                    if (rtspConnection != null)
                    {
                        ConnectionAdded?.Invoke(message.RtspUri, rtspConnection.ConnectionID, ref rtspConnection.VideoSource);
                    }
                    listener.SendMessage(message.CreateResponse());
                    return;
                case RtspRequestDescribe describeMessage:
                    HandleDescribe(listener, message);
                    return;
                case RtspRequestSetup setupMessage:
                    HandleSetup(listener, setupMessage);
                    return;
            }

            var connection = dicRTSPConnection.Values.FirstOrDefault(x => x.session_id == message.Session);
            if (connection is null)
            {
                // Session ID was not found in the list of Sessions. Send a 454 error
                RtspResponse notFound = message.CreateResponse();
                notFound.ReturnCode = 454; // Session Not Found
                listener.SendMessage(notFound);
                return;
            }

            switch (message)
            {
                case RtspRequestPlay playMessage:
                    // Search for the Session in the Sessions List. Change the state to "PLAY"
                    const string range = "npt=0-";   // Playing the 'video' from 0 seconds until the end
                    string rtp_info = "url=" + message.RtspUri + ";seq=" + videoSequenceNumber; // TODO Add rtptime  +";rtptime="+session.rtp_initial_timestamp;
                                                                                                // Add audio too
                    rtp_info += ",url=" + message.RtspUri + ";seq=" + audioSequenceNumber; // TODO Add rtptime  +";rtptime="+session.rtp_initial_timestamp;

                    //    'RTP-Info: url=rtsp://192.168.1.195:8557/h264/track1;seq=33026;rtptime=3014957579,url=rtsp://192.168.1.195:8557/h264/track2;seq=42116;rtptime=3335975101'

                    // Send the reply
                    RtspResponse play_response = message.CreateResponse();
                    play_response.AddHeader("Range: " + range);
                    play_response.AddHeader("RTP-Info: " + rtp_info);
                    listener.SendMessage(play_response);

                    connection.video.mustSendRtcpPacket = true;
                    connection.audio.mustSendRtcpPacket = true;

                    // Allow video and audio to go to this client
                    connection.play = true;
                    return;
                case RtspRequestPause pauseMessage:
                    connection.play = false;
                    RtspResponse pause_response = message.CreateResponse();
                    listener.SendMessage(pause_response);
                    return;
                case RtspRequestGetParameter getParameterMessage:
                    // Create the reponse to GET_PARAMETER
                    RtspResponse getparameter_response = message.CreateResponse();
                    listener.SendMessage(getparameter_response);
                    return;
                case RtspRequestTeardown teardownMessage:
                    RemoveSession(connection.ConnectionID);
                    listener.Dispose();
                    return;
            }
        }

        private void HandleSetup(RTSPListener listener, RtspRequestSetup setupMessage)
        {
            // Check the RTSP transport
            // If it is UDP or Multicast, create the sockets
            // If it is RTP over RTSP we send data via the RTSP Listener

            // FIXME client may send more than one possible transport.
            // very rare
            RtspTransport transport = setupMessage.GetTransports()[0];

            // Construct the Transport: reply from the Server to the client
            RtspTransport? transport_reply = null;
            IRtpTransport? rtpTransport = null;

            if (transport.LowerTransport == RtspTransport.LowerTransportType.TCP)
            {
                Debug.Assert(transport.Interleaved != null, "If transport.Interleaved is null here the program did not handle well connection problem");
                rtpTransport = new RtpTcpTransport(listener)
                {
                    DataChannel = transport.Interleaved.First,
                    ControlChannel = transport.Interleaved.Second,
                };
                // RTP over RTSP mode
                transport_reply = new()
                {
                    SSrc = global_ssrc.ToString("X8"), // Convert to Hex, padded to 8 characters
                    LowerTransport = RtspTransport.LowerTransportType.TCP,
                    Interleaved = new PortCouple(transport.Interleaved.First, transport.Interleaved.Second)
                };
            }
            else if (transport.LowerTransport == RtspTransport.LowerTransportType.UDP && !transport.IsMulticast)
            {
                Debug.Assert(transport.ClientPort != null, "If transport.ClientPort is null here the program did not handle well connection problem");

                // RTP over UDP mode
                // Create a pair of UDP sockets - One is for the Data (eg Video/Audio), one is for the RTCP
                var udp_pair = new UDPSocket(50000, 51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
                udp_pair.SetDataDestination(listener.RemoteEndPoint.Address.ToString(), transport.ClientPort.First);
                udp_pair.SetControlDestination(listener.RemoteEndPoint.Address.ToString(), transport.ClientPort.Second);
                udp_pair.ControlReceived += (local_sender, local_e) =>
                {
                    // RTCP data received
                    _logger.LogDebug($"RTCP data received {local_sender} {local_e.Data.Data.Length}");
                    if (dicRTSPConnection.TryGetValue(listener.ConnectionID, out var connection))
                    {
                        connection.UpdateKeepAlive();
                    }
                    local_e.Data.Dispose();
                };
                udp_pair.Start(); // start listening for data on the UDP ports

                // Pass the Port of the two sockets back in the reply
                transport_reply = new()
                {
                    SSrc = global_ssrc.ToString("X8"), // Convert to Hex, padded to 8 characters
                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                    IsMulticast = false,
                    ServerPort = new PortCouple(udp_pair.DataPort, udp_pair.ControlPort),
                    ClientPort = transport.ClientPort
                };

                rtpTransport = udp_pair;
            }
            else if (transport.LowerTransport == RtspTransport.LowerTransportType.UDP && transport.IsMulticast)
            {
                // RTP over Multicast UDP mode}
                // Create a pair of UDP sockets in Multicast Mode
                // Pass the Ports of the two sockets back in the reply
                transport_reply = new()
                {
                    SSrc = global_ssrc.ToString("X8"), // Convert to Hex, padded to 8 characters
                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                    IsMulticast = true,
                    Port = new PortCouple(7000, 7001)  // FIX
                };

                // for now until implemented
                transport_reply = null;
            }

            if (transport_reply != null)
            {
                // Update the stream within the session with transport information
                // If a Session ID is passed in we should match SessionID with other SessionIDs but we can match on RemoteAddress
                string copy_of_session_id = "";
                if (dicRTSPConnection.TryGetValue(listener.ConnectionID, out var setupConnection))
                {
                    // Check the Track ID to determine if this is a SETUP for the Video Stream
                    // or a SETUP for an Audio Stream.
                    // In the SDP the H264 video track is TrackID 0
                    // and the Audio Track is TrackID 1
                    RTPStream? stream = null;

                    string? setupUri = setupMessage.RtspUri?.AbsoluteUri;
                    if (setupUri?.EndsWith("trackID=0", StringComparison.Ordinal) == true) stream = setupConnection.video;
                    else if (setupUri?.EndsWith("trackID=1", StringComparison.Ordinal) == true) stream = setupConnection.audio;

                    stream?.rtpChannel = rtpTransport;
                    // When there is Video and Audio there are two SETUP commands.
                    // For the first SETUP command we will generate the connection.session_id and return a SessionID in the Reply.
                    // For the 2nd command the client will send is the SessionID.
                    if (string.IsNullOrEmpty(setupConnection.session_id))
                    {
                        setupConnection.session_id = session_handle.ToString();
                        session_handle++;
                    }
                    // ELSE, could check the Session passed in matches the Session we generated on last SETUP command
                    // Copy the Session ID, as we use it in the reply
                    copy_of_session_id = setupConnection.session_id;
                }

                RtspResponse setup_response = setupMessage.CreateResponse();
                setup_response.Headers[RtspHeaderNames.Transport] = transport_reply.ToString();
                setup_response.Session = copy_of_session_id;
                setup_response.Timeout = RtspTimeOut;
                listener.SendMessage(setup_response);
            }
            else
            {
                RtspResponse setup_response = setupMessage.CreateResponse();
                // unsuported transport
                setup_response.ReturnCode = 461;
                listener.SendMessage(setup_response);
            }
        }

        private void HandleDescribe(RTSPListener listener, RtspRequest message)
        {
            if (!dicRTSPConnection.TryGetValue(listener.ConnectionID, out var rtspConnection))
                return;

            //_logger.LogDebug($"Request for {message.RtspUri}");

            // TODO. Check the requsted_url is valid. In this example we accept any RTSP URL

            //// if the SPS and PPS are not defined yet, we have to return an error
            //if (rawSps == null || rawPs == null)
            //{
            //    RtspResponse describe_response2 = message.CreateResponse();
            //    describe_response2.ReturnCode = 400; // 400 Bad Request
            //    listener.SendMessage(describe_response2);
            //    return;
            //}

            //// Make the profile-level-id for H264
            //const int profile_idc = 77; // Main Profile
            //const int profile_iop = 0; // bit 7 (msb) is 0 so constrained_flag is false
            //const int level = 42; // Level 4.2

            //string profile_level_id_str = profile_idc.ToString("X2") // convert to hex, padded to 2 characters
            //                            + profile_iop.ToString("X2")
            //                            + level.ToString("X2");

            StringBuilder sdp = new();

            // Generate the SDP
            sdp.Append("v=0\n");
            sdp.Append("o=- 0 0 IN IP4 127.0.0.1\n");
            sdp.Append("s=Stream\n"); // 세션 이름 (필수)
            sdp.Append("t=0 0\n"); // 타이밍 정보 (필수)
            sdp.Append("c=IN IP4 0.0.0.0\n"); // 세션 레벨 연결 정보

            // VIDEO
            sdp.Append($"m=video 0 RTP/AVP {rtspConnection.VideoSource.Video.PayloadType}\n");

            string videoCodecName = rtspConnection.VideoSource.Video.Codec switch
            {
                CodecId.H264 => "H264",
                CodecId.MJPEG => "JPEG",
                CodecId.H265 => "H265",
                CodecId.AV1 => "AV1",
                _ => "H264" // 기본값
            };

            sdp.Append($"a=rtpmap:{rtspConnection.VideoSource.Video.PayloadType} {videoCodecName}/90000\n");

            // 코덱별 fmtp 설정
            if (rtspConnection.VideoSource.Video.Codec == CodecId.AV1)
            {
                // RFC 9368 준수 - AV1 RTP payload format
                sdp.Append($"a=fmtp:{rtspConnection.VideoSource.Video.PayloadType} profile=0;level-idx=8;tier=0\n");
            }
            else if (rtspConnection.VideoSource.Video.Codec == CodecId.H264)
            {
                // H264 SPS/PPS가 있으면 추가
                //if (rawSps != null && rawPs != null)
                //{
                //    string sps_str = Convert.ToBase64String(rawSps);
                //    string pps_str = Convert.ToBase64String(rawPs);
                //    sdp.Append($"a=fmtp:{rtspConnection.VideoSource.Video.PayloadType} profile-level-id={profile_level_id_str}; sprop-parameter-sets={sps_str},{pps_str};\n");
                //}
            }
            else if (rtspConnection.VideoSource.Video.Codec == CodecId.H265)
            {
                // H265 설정 (VLC/Live555 지원)
                // VPS/SPS/PPS가 있으면 추가
                //if (rawVps != null && rawSps != null && rawPps != null)
                //{
                //    string vps_str = Convert.ToBase64String(rawVps);
                //    string sps_str = Convert.ToBase64String(rawSps);
                //    string pps_str = Convert.ToBase64String(rawPps);
                //    sdp.Append($"a=fmtp:{rtspConnection.VideoSource.Video.PayloadType} sprop-vps={vps_str}; sprop-sps={sps_str}; sprop-pps={pps_str}\n");
                //}
            }

            sdp.Append("a=control:trackID=0\n");

            // AUDIO
            sdp.Append($"m=audio 0 RTP/AVP {rtspConnection.VideoSource.Audio.PayloadType}\n");

            string audioCodecName = rtspConnection.VideoSource.Audio.Codec switch
            {
                CodecId.PCMU => "PCMU",
                _ => "PCMU" // 기본값
            };

            sdp.Append($"a=rtpmap:{rtspConnection.VideoSource.Audio.PayloadType} {audioCodecName}/8000\n");
            sdp.Append("a=control:trackID=1\n");

            // 디버깅용 출력 (옵션)
            string sdpOutput = sdp.ToString();
            //_logger.LogDebug("=== Generated SDP ===");
            //_logger.LogDebug(sdpOutput);
            //_logger.LogDebug("====================");

            byte[] sdp_bytes = Encoding.ASCII.GetBytes(sdpOutput);

            // Create the reponse to DESCRIBE
            // This must include the Session Description Protocol (SDP)
            RtspResponse describe_response = message.CreateResponse();

            describe_response.AddHeader("Content-Base: " + message.RtspUri);
            describe_response.AddHeader("Content-Type: application/sdp");
            describe_response.Data = sdp_bytes;
            describe_response.AdjustContentLength();

            using (StreamReader sdp_stream = new(new MemoryStream(describe_response.Data.ToArray())))
            {
                if (describe_response.Headers.TryGetValue(RtspHeaderNames.ContentBase, out string? contentBase))
                    rtspConnection.ContentBase = contentBase;
                rtspConnection.SdpFile = SdpFile.ReadLoose(sdp_stream);
            }

            listener.SendMessage(describe_response);
        }

        private bool CheckTimeout(RTSPConnection connection)
        {
            return (DateTime.UtcNow - connection.TimeSinceLastRtspKeepalive).Ticks > TimeSpan.TicksPerSecond * RtspTimeOut;
            //DateTime now = DateTime.UtcNow;
            //var timeOut = now.AddSeconds(-rtspTimeOut);

            //if ((now - connection.TimeSinceLastRtspKeepalive).TotalSeconds > rtspTimeOut)
            //{
            //    return true;
            //}
            //return false;
        }

        //public void CheckTimeouts(out int current_rtsp_count, out int current_rtsp_play_count)
        //{
        //    DateTime now = DateTime.UtcNow;
        //    var connectionsToRemove = new List<Guid>();

        //    foreach (var kvp in dicRTSPConnection)
        //    {
        //        var connection = kvp.Value;

        //        if (!connection.play ||
        //            (connection.video?.rtpChannel != null && (now - connection.video.lastRtpSentTime).TotalSeconds > rtspTimeOut))
        //        {
        //            connectionsToRemove.Add(kvp.Key);
        //        }
        //    }

        //    foreach (var key in connectionsToRemove)
        //    {
        //        if (dicRTSPConnection.TryRemove(key, out var removed))
        //        {
        //            _logger.LogDebug($"Removing session {removed.session_id} due to TIMEOUT");
        //        }
        //    }

        //    current_rtsp_count = dicRTSPConnection.Count;
        //    current_rtsp_play_count = dicRTSPConnection.Values.Count(c => c.play);
        //}

        // Feed in Raw SPS/PPS data - no 32 bit headers, no 00 00 00 01 headers
        //public void FeedInRawSPSandPPS(byte[] sps_data, byte[] pps_data) // SPS data without any headers (00 00 00 01 or 32 bit lengths)
        //{
        //    rawSps = sps_data;
        //    rawPs = pps_data;
        //}

        //public void FeedInAudioPacket(uint timestamp_ms, ReadOnlyMemory<byte> audio_packet)
        //{
        //    CheckTimeouts(out int currentRtspCount, out int currentRtspPlayCount);

        //    // Console.WriteLine(current_rtsp_count + " RTSP clients connected. " + current_rtsp_play_count + " RTSP clients in PLAY mode");

        //    if (currentRtspPlayCount == 0) return;

        //    uint rtpTimestamp = timestamp_ms * 8; // 8kHz clock

        //    // Put the whole Audio Packet into one RTP packet.
        //    // 12 is header size when there are no CSRCs or extensions
        //    var size = 12 + audio_packet.Length;
        //    using var owner = MemoryPool<byte>.Shared.Rent(size);
        //    var rtpPacket = owner.Memory[..size];
        //    // Create an single RTP fragment

        //    // RTP Packet Header
        //    // 0 - Version, P, X, CC, M, PT and Sequence Number
        //    //32 - Timestamp. H264 uses a 90kHz clock
        //    //64 - SSRC
        //    //96 - CSRCs (optional)
        //    //nn - Extension ID and Length
        //    //nn - Extension header

        //    const bool rtpPadding = false;
        //    const bool rtpHasExtension = false;
        //    uint[] csrc = Array.Empty<uint>();
        //    const bool rtpMarker = true; // always 1 as this is the last (and only) RTP packet for this audio timestamp

        //    RtpPacketUtil.WriteHeader(rtpPacket.Span,
        //        RtpPacketUtil.RTP_VERSION, rtpPadding, rtpHasExtension, csrc.Length, rtpMarker, audio_payload_type);

        //    RtpPacketUtil.WriteSequenceNumber(rtpPacket.Span, audioSequenceNumber++);
        //    RtpPacketUtil.WriteSSRC(rtpPacket.Span, global_ssrc);
        //    RtpPacketUtil.WriteTimestamp(rtpPacket.Span, rtpTimestamp);

        //    // Now append the audio packet
        //    audio_packet.CopyTo(rtpPacket[12..]);


        //    // Go through each RTSP connection and output the Audio data to the Audio Session
        //    var tasks = dicRTSPConnection.Values.Select(async (connection) =>
        //    {
        //        // Only process Sessions in Play Mode
        //        if (!connection.play) return;

        //        // The client may have only subscribed to Video. Check if the client wants audio
        //        if (connection.audio.rtpChannel is null) return;

        //        Console.WriteLine("Sending audio session " + connection.session_id + " " + TransportLogName(connection.audio.rtpChannel) + " Timestamp(ms)=" + timestamp_ms + ". RTP timestamp=" + rtpTimestamp + ". Sequence=" + audioSequenceNumber);
        //        bool write_error = false;

        //        if (connection.audio.mustSendRtcpPacket)
        //        {
        //            if (!await SendRTCP(rtpTimestamp, connection, connection.audio))
        //            {
        //                RemoveSession(connection.ConnectionID);
        //            }
        //        }

        //        // There could be more than 1 RTP packet (if the data is fragmented)
        //        {
        //            try
        //            {
        //                // send the whole RTP packet
        //                await connection.audio.rtpChannel.WriteToDataPortAsync(rtpPacket);
        //            }
        //            catch (Exception e)
        //            {
        //                _logger.LogError($"UDP Write Exception. {e.Message}");
        //                _logger.LogError($"Error writing to listener {connection.Listener.RemoteEndPoint}");
        //                write_error = true;
        //            }
        //        }
        //        if (write_error)
        //        {
        //            Console.WriteLine("Removing session " + connection.session_id + " due to write error");
        //            RemoveSession(connection.ConnectionID);
        //        }

        //        connection.audio.packetCount++;
        //        connection.audio.octetCount += (uint)audio_packet.Length; // QUESTION - Do I need to include the RTP header bytes/fragmenting bytes
        //    }).ToArray();
        //    Task.WaitAll(tasks);
        //}

        // Feed in Raw NALs - no 32 bit headers, no 00 00 00 01 headers
        //public void FeedInRawNAL(uint timestamp_ms, List<byte[]> nal_array)
        //{
        //    CheckTimeouts(out int current_rtsp_count, out int current_rtsp_play_count);

        //    if (current_rtsp_play_count == 0) return;

        //    uint rtp_timestamp = timestamp_ms * 90; // 90kHz clock

        //    // Build a list of 1 or more RTP packets
        //    // The last packet will have the M bit set to '1'
        //    (List<Memory<byte>> rtp_packets, List<IMemoryOwner<byte>> memoryOwners) = PrepareVideoRtpPackets(nal_array, rtp_timestamp);

        //    // Go through each RTSP connection and output the NAL on the Video Session
        //    var tasks = dicRTSPConnection.Values.Select(async (connection) =>
        //    {
        //        // Only process Sessions in Play Mode
        //        if (!connection.play) return;

        //        if (connection.video.rtpChannel is null) return;
        //        _logger.LogDebug($"Sending video session {connection.session_id} {TransportLogName(connection.video.rtpChannel)} Timestamp(ms)={timestamp_ms}. RTP timestamp={rtp_timestamp}. Sequence={videoSequenceNumber}");

        //        if (connection.video.mustSendRtcpPacket && !await SendRTCP(rtp_timestamp, connection, connection.video))
        //        {
        //            RemoveSession(connection.ConnectionID);
        //            return;
        //        }

        //        // There could be more than 1 RTP packet (if the data is fragmented)
        //        foreach (var rtp_packet in rtp_packets)
        //        {
        //            Debug.Assert(connection.video.rtpChannel != null, "If connection.video.rptChannel is null here the program did not handle well connection problem");
        //            try
        //            {
        //                // send the whole NAL. ** We could fragment the RTP packet into smaller chuncks that fit within the MTU
        //                // Send to the IP address of the Client
        //                // Send to the UDP Port the Client gave us in the SETUP command
        //                await connection.video.rtpChannel.WriteToDataPortAsync(rtp_packet);
        //            }
        //            catch (Exception e)
        //            {
        //                Console.WriteLine("UDP Write Exception " + e);
        //                Console.WriteLine("Error writing to listener " + connection.Listener.RemoteEndPoint);
        //                Console.WriteLine("Removing session " + connection.session_id + " due to write error");
        //                RemoveSession(connection.ConnectionID);
        //                break; // exit out of foreach loop
        //            }
        //        }
        //        connection.video.octetCount += (uint)nal_array.Sum(nal => nal.Length); // QUESTION - Do I need to include the RTP header bytes/fragmenting bytes
        //    }).ToArray();

        //    Task.WaitAll(tasks);

        //    foreach (var owner in memoryOwners)
        //    {
        //        owner.Dispose();
        //    }
        //}

        private static bool SendRTCP(uint rtpTimestamp, RTSPConnection connection, RTPStream stream)
        {
            var now = DateTime.UtcNow;

            // MemoryPool을 사용하여 불필요한 배열 할당 최소화
            const int RtcpPacketSize = 28;
            using var rtcpOwner = MemoryPool<byte>.Shared.Rent(RtcpPacketSize);
            var rtcpBuffer = rtcpOwner.Memory[..RtcpPacketSize];

            // RTCP 헤더 작성
            const bool hasPadding = false;
            const int reportCount = 0;
            int lengthInWords = (RtcpPacketSize / 4) - 1; // 32bit 단위 길이 -1
            RtcpPacketUtil.WriteHeader(rtcpBuffer.Span, RtcpPacketUtil.RTCP_VERSION, hasPadding, reportCount,
                RtcpPacketUtil.RTCP_PACKET_TYPE_SENDER_REPORT, lengthInWords, global_ssrc);

            // Sender Report 작성
            RtcpPacketUtil.WriteSenderReport(rtcpBuffer.Span, now, rtpTimestamp, stream.packetCount, stream.octetCount);

            // RTP 채널이 null이면 바로 false 반환
            var rtpChannel = stream.rtpChannel;
            if (rtpChannel == null)
            {
                return false;
            }

            try
            {
                rtpChannel.WriteToControlPort(rtcpBuffer.Span);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> SendRTCPAsync(uint rtpTimestamp, RTSPConnection connection, RTPStream stream)
        {
            var now = DateTime.UtcNow;

            // MemoryPool을 사용하여 불필요한 배열 할당 최소화
            const int RtcpPacketSize = 28;
            using var rtcpOwner = MemoryPool<byte>.Shared.Rent(RtcpPacketSize);
            var rtcpBuffer = rtcpOwner.Memory[..RtcpPacketSize];

            // RTCP 헤더 작성
            const bool hasPadding = false;
            const int reportCount = 0;
            int lengthInWords = (RtcpPacketSize / 4) - 1; // 32bit 단위 길이 -1
            RtcpPacketUtil.WriteHeader(rtcpBuffer.Span, RtcpPacketUtil.RTCP_VERSION, hasPadding, reportCount,
                RtcpPacketUtil.RTCP_PACKET_TYPE_SENDER_REPORT, lengthInWords, global_ssrc);

            // Sender Report 작성
            RtcpPacketUtil.WriteSenderReport(rtcpBuffer.Span, now, rtpTimestamp, stream.packetCount, stream.octetCount);

            // RTP 채널이 null이면 바로 false 반환
            var rtpChannel = stream.rtpChannel;
            if (rtpChannel == null)
            {
                return false;
            }

            try
            {
                await rtpChannel.WriteToControlPortAsync(rtcpBuffer);
                return true;
            }
            catch
            {
                return false;
            }
        }

        //private (List<Memory<byte>>, List<IMemoryOwner<byte>>) PrepareVideoRtpPackets(List<byte[]> nal_array, uint rtp_timestamp)
        //{
        //    List<Memory<byte>> rtp_packets = new List<Memory<byte>>();
        //    List<IMemoryOwner<byte>> memoryOwners = new List<IMemoryOwner<byte>>();
        //    for (int x = 0; x < nal_array.Count; x++)
        //    {
        //        var raw_nal = nal_array[x];
        //        bool last_nal = false;
        //        if (x == nal_array.Count - 1)
        //        {
        //            last_nal = true; // last NAL in our nal_array
        //        }

        //        // The H264 Payload could be sent as one large RTP packet (assuming the receiver can handle it)
        //        // or as a Fragmented Data, split over several RTP packets with the same Timestamp.
        //        bool fragmenting = false;

        //        int packetMTU = 1400; // 65535; 
        //        packetMTU += -8 - 20 - 16; // -8 for UDP header, -20 for IP header, -16 normal RTP header len. ** LESS RTP EXTENSIONS !!!

        //        if (raw_nal.Length > packetMTU) fragmenting = true;

        //        // INDIGO VISION DOES NOT SUPPORT FRAGMENTATION. Send as one jumbo RTP packet and let OS split over MTUs.
        //        // NOTE TO SELF... perhaps this was because the SDP did not have the extra packetization flag
        //        //  fragmenting = false;

        //        if (!fragmenting)
        //        {
        //            // Put the whole NAL into one RTP packet.
        //            // Note some receivers will have maximum buffers and be unable to handle large RTP packets.
        //            // Also with RTP over RTSP there is a limit of 65535 bytes for the RTP packet.

        //            // 12 is header size when there are no CSRCs or extensions
        //            var owner = MemoryPool<byte>.Shared.Rent(12 + raw_nal.Length);
        //            memoryOwners.Add(owner);
        //            var rtp_packet = owner.Memory[..(12 + raw_nal.Length)];

        //            // Create an single RTP fragment

        //            // RTP Packet Header
        //            // 0 - Version, P, X, CC, M, PT and Sequence Number
        //            //32 - Timestamp. H264 uses a 90kHz clock
        //            //64 - SSRC
        //            //96 - CSRCs (optional)
        //            //nn - Extension ID and Length
        //            //nn - Extension header

        //            const bool rtpPadding = false;
        //            const bool rtpHasExtension = false;
        //            const int rtp_csrc_count = 0;

        //            RtpPacketUtil.WriteHeader(rtp_packet.Span,
        //                RtpPacketUtil.RTP_VERSION,
        //                rtpPadding,
        //                rtpHasExtension, rtp_csrc_count, last_nal, video_payload_type);

        //            RtpPacketUtil.WriteSequenceNumber(rtp_packet.Span, videoSequenceNumber++);
        //            RtpPacketUtil.WriteSSRC(rtp_packet.Span, global_ssrc);

        //            RtpPacketUtil.WriteTimestamp(rtp_packet.Span, rtp_timestamp);

        //            // Now append the raw NAL
        //            raw_nal.CopyTo(rtp_packet[12..]);

        //            rtp_packets.Add(rtp_packet);
        //        }
        //        else
        //        {
        //            int data_remaining = raw_nal.Length;
        //            int nal_pointer = 0;
        //            int start_bit = 1;
        //            int end_bit = 0;

        //            // consume first byte of the raw_nal. It is used in the FU header
        //            byte first_byte = raw_nal[0];
        //            nal_pointer++;
        //            data_remaining--;

        //            while (data_remaining > 0)
        //            {
        //                int payload_size = Math.Min(packetMTU, data_remaining);
        //                if (data_remaining == payload_size) end_bit = 1;

        //                // 12 is header size. 2 bytes for FU-A header. Then payload
        //                var destSize = 12 + 2 + payload_size;
        //                var owner = MemoryPool<byte>.Shared.Rent(destSize);
        //                memoryOwners.Add(owner);
        //                var rtp_packet = owner.Memory[..destSize];

        //                // RTP Packet Header
        //                // 0 - Version, P, X, CC, M, PT and Sequence Number
        //                //32 - Timestamp. H264 uses a 90kHz clock
        //                //64 - SSRC
        //                //96 - CSRCs (optional)
        //                //nn - Extension ID and Length
        //                //nn - Extension header

        //                const bool rtpPadding = false;
        //                const bool rtpHasExtension = false;
        //                const int rtp_csrc_count = 0;

        //                RtpPacketUtil.WriteHeader(rtp_packet.Span, RtpPacketUtil.RTP_VERSION,
        //                    rtpPadding, rtpHasExtension, rtp_csrc_count, last_nal && end_bit == 1, video_payload_type);

        //                RtpPacketUtil.WriteSequenceNumber(rtp_packet.Span, videoSequenceNumber++);
        //                RtpPacketUtil.WriteSSRC(rtp_packet.Span, global_ssrc);
        //                RtpPacketUtil.WriteTimestamp(rtp_packet.Span, rtp_timestamp);

        //                // Now append the Fragmentation Header (with Start and End marker) and part of the raw_nal
        //                const byte f_bit = 0;
        //                byte nri = (byte)(first_byte >> 5 & 0x03); // Part of the 1st byte of the Raw NAL (NAL Reference ID)
        //                const byte type = 28; // FU-A Fragmentation

        //                rtp_packet.Span[12] = (byte)((f_bit << 7) + (nri << 5) + type);
        //                rtp_packet.Span[13] = (byte)((start_bit << 7) + (end_bit << 6) + (0 << 5) + (first_byte & 0x1F));

        //                raw_nal.AsSpan(nal_pointer, payload_size).CopyTo(rtp_packet[14..].Span);
        //                nal_pointer += payload_size;
        //                data_remaining -= payload_size;

        //                rtp_packets.Add(rtp_packet);

        //                start_bit = 0;
        //            }
        //        }
        //    }

        //    return (rtp_packets, memoryOwners);
        //}

        private void RemoveSession(Guid connectionID)
        {
            if (dicRTSPConnection.TryRemove(connectionID, out var removeConnection))
            {
                removeConnection.play = false; // stop sending data
                removeConnection.video.rtpChannel?.Dispose();
                removeConnection.video.rtpChannel = null;
                removeConnection.audio.rtpChannel?.Dispose();
                removeConnection.audio.rtpChannel = null;
                removeConnection.Listener.Dispose();

                ConnectionRemoved?.Invoke(connectionID, removeConnection.VideoSource);
            }
        }

        private static string TransportLogName(IRtpTransport transport)
        {
            return transport switch
            {
                RtpTcpTransport => "TCP",
                MulticastUDPSocket => "Multicast",
                UDPSocket => "UDP",
                _ => "",
            };
        }
        #endregion

        #region IDisposable Membres

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopListen();
                _Stopping?.Dispose();
            }
        }

        #endregion

        // An RTPStream can be a Video Stream, Audio Stream or a MetaData Stream
        public class RTPStream
        {
            public int trackID;
            public bool mustSendRtcpPacket = false; // when true will send out a RTCP packet to match Wall Clock Time to RTP Payload timestamps
                                                    // 16 bit RTP packet sequence number used with this client connection
            public IRtpTransport? rtpChannel;     // Pair of UDP sockets (data and control) used when sending via UDP
            public uint packetCount = 0;       // Used in the RTCP Sender Report to state how many RTP packets have been transmitted (for packet loss)
            public uint octetCount = 0;        // number of bytes of video that have been transmitted (for average bandwidth monitoring)
        }

        public class RTSPConnection
        {
            public RTSPConnection()
            {
                ConnectionID = Guid.NewGuid();
            }
            // The RTSP client connection
            public required RTSPListener Listener { get; init; }
            // set to true when Session is in Play mode
            public bool play;

            // Time since last RTSP message received - used to spot dead UDP clients
            public DateTime TimeSinceLastRtspKeepalive { get; private set; } = DateTime.UtcNow;
            // Client Hostname/IP Address
            public string session_id = "";             // RTSP Session ID used with this client connection

            public Guid ConnectionID { get; init; }

            public VideoSource VideoSource = new();
            public readonly RTPStream video = new();
            public readonly RTPStream audio = new();

            public SdpFile? SdpFile { get; set; }

            public string? ContentBase { get; set; }

            public void UpdateKeepAlive()
            {
                TimeSinceLastRtspKeepalive = DateTime.UtcNow;
            }
        }
    }
}





