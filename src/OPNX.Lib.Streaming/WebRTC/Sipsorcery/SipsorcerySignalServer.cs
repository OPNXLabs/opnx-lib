using OPNX.Lib.Common.Logging;
using OPNX.Lib.Common.Primitives.Media;
using OPNX.Lib.Streaming.WebRTC;
using OPNX.Lib.Streaming.WebRTC.Abstractions;
using OPNX.Lib.Streaming.WebRTC.Events;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace OPNX.Lib.Streaming.WebRTC.Sipsorcery
{
    public class SipsorcerySignalServer : IWebRtcSignalServer
    {
        #region Fields        
        private readonly WebSocketServer webSocketServer = null;

        private readonly ConcurrentDictionary<Guid, SipsorceryPeerConnection> connections = new();
        #endregion

        #region Constructors
        public SipsorcerySignalServer(int port)
        {
            webSocketServer = new WebSocketServer(IPAddress.Any, port);

            webSocketServer.AddWebSocketService<SipsorceryClientSession>("/", (client) =>
            {
                client.SocketOpened += OnSocketOpened;
                client.SocketClosed += OnSocketClosed;
                client.MessageReceived += OnMessageReceived;
            });

            webSocketServer.Start();
        }
        #endregion

        #region Events        
        public delegate void WebSocketOpenedEventHandler(object sender, Uri requestUri, ref SipsorceryPeerConnection pc);
        public event WebSocketOpenedEventHandler WebSocketOpened;
        private void OnWebSocketOpened(WebSocketContext context, ref SipsorceryPeerConnection pc)
        {
            WebSocketOpened?.Invoke(this, context.RequestUri, ref pc);

            Guid connectionID = Guid.Parse(pc.SessionID);
            WebRtcClientOpenedEventArgs args = new(context.RequestUri, connectionID, pc.VideoSourceID);
            ClientOpened?.Invoke(this, args);
            pc.VideoSourceID = args.VideoSourceID;
        }

        public delegate void WebSocketClosedEventHandler(object sender, Uri requestUri, int videosourceID, Guid connectionID);
        public event WebSocketClosedEventHandler WebSocketClosed;

        public event EventHandler<WebRtcClientOpenedEventArgs>? ClientOpened;
        public event EventHandler<WebRtcClientClosedEventArgs>? ClientClosed;
        private void OnWebSocketClosed(WebSocketContext context, SipsorceryPeerConnection pc)
        {
            int videoSourceID = pc == null ? int.MinValue : pc.VideoSourceID;
            Guid connectionID = pc == null ? Guid.Empty : Guid.Parse(pc.SessionID);

            WebSocketClosed?.Invoke(this, context.RequestUri, videoSourceID, connectionID);
            ClientClosed?.Invoke(this, new WebRtcClientClosedEventArgs(context.RequestUri, connectionID, videoSourceID));
        }

        //public delegate void DataChannelReceivedDataEventHandler(object sender, Guid connectionID, string label, byte[] data);
        //public event DataChannelReceivedDataEventHandler DataChannelReceivedData;
        //private void OnDataChannelReceivedData(Guid connectionID, string label, byte[] data)
        //{
        //    if (DataChannelReceivedData != null)
        //    {
        //        DataChannelReceivedData(this, connectionID, label, data);
        //    }
        //}
        #endregion

        #region Properties
        public int Port
        {
            get
            {
                if (webSocketServer != null)
                    return webSocketServer.Port;
                return int.MinValue;
            }
        }
        #endregion

        #region Private / Protected Methods
        //private Task SendDataProcessor() => Task.Run(async () =>
        //{
        //    while (true)
        //    {
        //        if (sendDatas.IsEmpty)
        //        {
        //            await Task.Delay(30).ConfigureAwait(false);
        //            continue;
        //        }

        //        if (isDisposing)
        //        {
        //            return;
        //        }

        //        if (this.sendDatas.TryDequeue(out Tuple<Guid, byte[]> sendData))
        //        {
        //            var findConnection = connections.FirstOrDefault(x => Guid.Parse(x.Value.SessionID) == sendData.Item1);
        //            if (findConnection.Value == null || findConnection.Value.connectionState != RTCPeerConnectionState.connected)
        //                continue;

        //            RTPHeader rtpHeader = new RTPHeader(sendData.Item2);

        //            byte[] payload = new byte[rtpHeader.PayloadSize];

        //            Buffer.BlockCopy(sendData.Item2, rtpHeader.Length, payload, 0, payload.Length);

        //            findConnection.Value.SendRtpRaw(SDPMediaTypesEnum.video, payload, rtpHeader.Timestamp, rtpHeader.MarkerBit, 100);
        //        }
        //    }
        //});

        private void OnMessageReceived(WebSocketContext context, RTCPeerConnection pc, string message)
        {
            if (pc == null || string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                if (pc.remoteDescription == null)
                {
                    pc.AcceptRtpFromAny = true;
                    pc.setRemoteDescription(new RTCSessionDescriptionInit
                    {
                        sdp = message,
                        type = RTCSdpType.answer
                    });
                    return;
                }

                if (message.Equals(SDP.END_ICE_CANDIDATES_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
                    return;

                try
                {
                    var candInit = JsonSerializer.Deserialize<RTCIceCandidateInit>(message);
                    if (candInit != null)
                        pc.addIceCandidate(candInit);
                }
                catch (JsonException)
                {

                }
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }

        private async Task<SipsorceryPeerConnection> OnSocketOpened(WebSocketContext context)
        {
            try
            {
                SipsorceryPeerConnection resultPC = CreatePeerConnection(context);

                var offerInit = resultPC.createOffer(null);
                await resultPC.setLocalDescription(offerInit);

                //string sdpMessage = context.RequestUri.Host.ToLower() == "localhost"
                //    ? offerInit.sdp
                //    : SdpModifier.ModifySdpCandidateIP(offerInit.sdp, context.RequestUri.Host);
                //sdpMessage = SdpModifier.ModifySdpBitrate(sdpMessage, 5000);

                string sdpMessage = string.Equals(context.RequestUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                    ? offerInit.sdp : SdpModifier.ModifySdpCandidateIP(offerInit.sdp, context.RequestUri.Host);

                context.WebSocket.Send(sdpMessage);

                OnWebSocketOpened(context, ref resultPC);

                if (resultPC.VideoSourceID == int.MinValue)
                {
                    resultPC.Dispose();
                    return null;
                }

                connections.TryAdd(Guid.Parse(resultPC.SessionID), resultPC);
                return resultPC;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Exception in OnSocketOpened: {ex.Message}");
                return null;
            }

            //if (connections.ContainsKey(context))
            //    return connections[context];

            //SipsorceryPeerConnection resultPC = null;

            //try
            //{
            //    resultPC = CreatePeerConnection(context);

            //    if (resultPC != null)
            //    {
            //        if (connections.TryAdd(context, resultPC))
            //        {
            //            var offerInit = resultPC.createOffer(null);

            //            await resultPC.setLocalDescription(offerInit);

            //            if (context.WebSocket.ReadyState == WebSocketSharp.WebSocketState.Open)
            //                context.WebSocket.Send(offerInit.sdp);

            //            OnWebSocketOpened(context, ref resultPC);
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{

            //}

            //return resultPC;
        }


        private void OnSocketClosed(WebSocketContext context, SipsorceryPeerConnection peerConnection)
        {
            try
            {
                if (connections.TryRemove(Guid.Parse(peerConnection?.SessionID), out var removeItem))
                {
                    removeItem?.Dispose();
                }
            }
            catch
            {
            }

            OnWebSocketClosed(context, peerConnection);
        }

#pragma warning disable IDE0060
        private static SipsorceryPeerConnection CreatePeerConnection(WebSocketContext context)
        {
            //RTCConfiguration configuration = new RTCConfiguration()
            //{
            //    iceServers = new List<RTCIceServer> {
            //        new RTCIceServer {
            //            urls = "stun:stun.l.google.com:19302"
            //        },
            //        //new RTCIceServer{
            //        //    urls= "turn:global.relay.metered.ca:80",
            //        //    username= "f58980467897cb8c4826bdc1",
            //        //    credential= "/LgCzwXnHzflMTUf"
            //        //},
            //        //new RTCIceServer {
            //        //    urls= "turn:global.relay.metered.ca:80?transport=tcp",
            //        //    username= "f58980467897cb8c4826bdc1",
            //        //    credential= "/LgCzwXnHzflMTUf",
            //        //},
            //        //new RTCIceServer {
            //        //    urls= "turn:global.relay.metered.ca:443",
            //        //    username= "f58980467897cb8c4826bdc1",
            //        //    credential= "/LgCzwXnHzflMTUf",
            //        //},
            //        //    urls= "turns:global.relay.metered.ca:443?transport=tcp",
            //        //    username= "f58980467897cb8c4826bdc1",
            //        //    credential= "/LgCzwXnHzflMTUf",
            //        //},
            //    },
            //    iceTransportPolicy = RTCIceTransportPolicy.relay,
            //    iceCandidatePoolSize = 10
            //};

            MediaStreamTrack videoTrack = new(
                SDPMediaTypesEnum.video,
                false,
                [
                    new(new VideoFormat(VideoCodecsEnum.H264, (int)CodecId.H264)),
                    //new SDPAudioVideoMediaFormat(new VideoFormat(VideoCodecsEnum.H265, (int)VideoCodecType.H265)),                    
                ],
                MediaStreamStatusEnum.SendOnly);


            MediaStreamTrack audioTrack = new(
                SDPMediaTypesEnum.audio,
                false,
                [
                    new(new AudioFormat(AudioCodecsEnum.PCMU, (int)CodecId.PCMU)),
                ],
                MediaStreamStatusEnum.SendOnly);

            SipsorceryPeerConnection pc = new();
            pc.addTrack(videoTrack);
            pc.addTrack(audioTrack);

            //pc.createDataChannel("DataChannel");

            //pc.ondatachannel += (dataChannel) =>
            //{
            //    dataChannel.onmessage += (dc, protocol, data) =>
            //    {
            //        OnDataChannelReceivedData(Guid.Parse(pc.SessionID), dc.label, data);
            //    };
            //};

            //pc.addIceCandidate(new RTCIceCandidateInit()
            //{

            //});

            //pc.onconnectionstatechange += (state) =>
            //{
            //    Console.WriteLine($"Peer connection state changed to {state}.");

            //    if (state == RTCPeerConnectionState.connected)
            //    {
            //        Console.WriteLine("Creating RTP session to receive ffmpeg stream.");
            //    }
            //};

            //pc.onicecandidateerror += (candidate, error) =>
            //{
            //    Console.WriteLine($"Error adding remote ICE candidate. {error} {candidate}");
            //};
            //pc.oniceconnectionstatechange += (state) =>
            //{
            //    Console.WriteLine($"ICE connection state change to {state}.");
            //};
            //pc.OnReceiveReport += (endpoint, type, rtcp) =>
            //{
            //    Console.WriteLine($"WebRTC - RTCP {type} report received.");
            //};

            //pc.OnRtpPacketReceived += (IipEndPoint, sdpMediaTYpe, rtpPacket) =>
            //{

            //};
            //pc.OnRtcpBye += (reason) =>
            //{
            //    Console.WriteLine($"RTCP BYE receive, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");
            //};

            //pc.OnRtpClosed += (reason) =>
            //{
            //    Console.WriteLine($"Peer connection closed, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");
            //};

            //pc.onicecandidate += (candidate) =>
            //{
            //    //switch (pc.signalingState)
            //    //{
            //    //    case RTCSignalingState.have_local_offer:
            //    //    case RTCSignalingState.have_remote_offer:
            //    //        {
            //    //            context.WebSocket.Send($"candidate:{candidate}");
            //    //        }
            //    //        break;
            //    //}

            //    if (candidate?.candidate != null)
            //    {
            //        // ICE Candidate를 JSON 문자열로 변환
            //        var candidateJson = JsonSerializer.Serialize(candidate.candidate);

            //        // 콘솔에 출력 (디버깅용)
            //        Console.WriteLine($"[ICE Candidate] {candidateJson}");

            //        // WebSocket을 통해 ICE Candidate 전송
            //        context.WebSocket.Send($"candidate:{candidateJson}");
            //    }
            //    else
            //    {
            //        Console.WriteLine("[ICE Candidate] No more candidates.");
            //    }
            //};

            return pc;
        }
#pragma warning restore IDE0060

        #endregion

        #region Public Methods   

        public void SendRtpData(SDPMediaTypesEnum mediaType, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit)
        {
            byte[] payloadArray = payload.ToArray();

            foreach (var kvp in connections)
            {
                if (kvp.Value.connectionState == RTCPeerConnectionState.connected)
                {
                    kvp.Value.SendRtpRaw(mediaType, payloadArray, timeStamp, markerBit, payloadType);
                }
            }
        }

        public void SendRtpData(Guid connectionID, SDPMediaTypesEnum mediaType, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit)
        {
            if (connections.TryGetValue(connectionID, out var connection) && connection.connectionState == RTCPeerConnectionState.connected)
            {
                connection.SendRtpRaw(mediaType, payload.ToArray(), timeStamp, markerBit, payloadType);
            }
        }

        public void SendRtpVideoData(int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit) =>
            SendRtpData(SDPMediaTypesEnum.video, payloadType, payload, timeStamp, markerBit);

        public void SendRtpAudioData(int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit) =>
            SendRtpData(SDPMediaTypesEnum.audio, payloadType, payload, timeStamp, markerBit);

        public void SendRtpVideoData(Guid connectionID, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit) =>
            SendRtpData(connectionID, SDPMediaTypesEnum.video, payloadType, payload, timeStamp, markerBit);

        public void SendRtpAudioData(Guid connectionID, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit) =>
            SendRtpData(connectionID, SDPMediaTypesEnum.audio, payloadType, payload, timeStamp, markerBit);

        //public void SendUserData(string label, Guid connectionID, string data)
        //{
        //    var findConnection = connections.FirstOrDefault(x => Guid.Parse(x.Value.SessionID) == connectionID);
        //    if (findConnection.Value == null || findConnection.Value.connectionState != RTCPeerConnectionState.connected)
        //        return;

        //    var findDatachannel = findConnection.Value.DataChannels.FirstOrDefault(x => x.label == label);
        //    if (findDatachannel != null)
        //    {
        //        findDatachannel.send(data);
        //    }
        //}

        public void CloseConnection(Guid connectionID)
        {
            if (connections.TryRemove(connectionID, out var removeItem))
            {
                if (removeItem.connectionState == RTCPeerConnectionState.connected)
                    removeItem.close();
                removeItem.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (var item in connections.Values)
            {
                item.Dispose();
            }
            connections.Clear();

            webSocketServer?.Stop();

            GC.SuppressFinalize(this);
        }
        #endregion
    }
}





