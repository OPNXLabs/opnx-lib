using DataChannelDotnet.Bindings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Streaming.WebRTC.Abstractions;
using OPNX.Lib.Streaming.WebRTC.Events;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace OPNX.Lib.Streaming.WebRTC.DataChannel
{
    public class DataChannelSignalServer : IWebRtcSignalServer
    {
        private readonly WebSocketServer webSocketServer;
        private readonly ConcurrentDictionary<Guid, DataChannelPeerConnection> connections = new();
        private readonly ILogger _logger;

        public DataChannelSignalServer(int port, ILogger? logger = null)
        {
            _logger = logger ?? NullLogger.Instance;

            webSocketServer = new WebSocketServer(IPAddress.Any, port);

            webSocketServer.AddWebSocketService<DataChannelClientSession>("/", client =>
            {
                client.SocketOpened += OnSocketOpened;
                client.SocketClosed += OnSocketClosed;
                client.MessageReceived += OnMessageReceived;
            });

            webSocketServer.Start();
        }

        public delegate void WebSocketOpenedEventHandler(object sender, Uri requestUri, ref DataChannelPeerConnection pc);
        public event WebSocketOpenedEventHandler? WebSocketOpened;

        public delegate void WebSocketClosedEventHandler(object sender, Uri requestUri, int videosourceID, Guid connectionID);
        public event WebSocketClosedEventHandler? WebSocketClosed;

        public event EventHandler<WebRtcClientOpenedEventArgs>? ClientOpened;
        public event EventHandler<WebRtcClientClosedEventArgs>? ClientClosed;

        public int Port => webSocketServer.Port;

        private void OnWebSocketOpened(WebSocketContext context, ref DataChannelPeerConnection pc)
        {
            WebSocketOpened?.Invoke(this, context.RequestUri, ref pc);

            WebRtcClientOpenedEventArgs args = new(context.RequestUri, pc.SessionID, pc.VideoSourceID);
            ClientOpened?.Invoke(this, args);
            pc.VideoSourceID = args.VideoSourceID;
        }

        private void OnWebSocketClosed(WebSocketContext context, DataChannelPeerConnection? pc)
        {
            int videoSourceID = pc?.VideoSourceID ?? int.MinValue;
            Guid connectionID = pc?.SessionID ?? Guid.Empty;

            WebSocketClosed?.Invoke(this, context.RequestUri, videoSourceID, connectionID);
            ClientClosed?.Invoke(this, new WebRtcClientClosedEventArgs(context.RequestUri, connectionID, videoSourceID));
        }

        private void OnMessageReceived(WebSocketContext context, DataChannelPeerConnection? pc, string message)
        {
            if (pc == null || string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                if (pc.RemoteDescription == null)
                {
                    pc.SetRemoteAnswer(message);
                    return;
                }

                if (IsEndOfCandidates(message))
                    return;

                if (TryParseIceCandidate(message, out string? candidate, out string? mid))
                    pc.AddRemoteCandidate(candidate, mid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
            }
        }

        private async Task<DataChannelPeerConnection?> OnSocketOpened(WebSocketContext context)
        {
            try
            {
                DataChannelPeerConnection resultPC = CreatePeerConnection(context);
                resultPC.LocalCandidateCreated += (_, candidate) =>
                {
                    if (context.WebSocket.ReadyState == WebSocketSharp.WebSocketState.Open)
                    {
                        string json = JsonSerializer.Serialize(new
                        {
                            candidate = candidate.Content,
                            sdpMid = candidate.Mid
                        });
                        context.WebSocket.Send(json);
                    }
                };

                string sdpMessage = (await resultPC.CreateOfferAsync().ConfigureAwait(false)).Sdp;

                if (!string.Equals(context.RequestUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                    sdpMessage = SdpModifier.ModifySdpCandidateIP(sdpMessage, context.RequestUri.Host);

                context.WebSocket.Send(sdpMessage);

                OnWebSocketOpened(context, ref resultPC);

                if (resultPC.VideoSourceID == int.MinValue)
                {
                    resultPC.Dispose();
                    return null;
                }

                connections.TryAdd(resultPC.SessionID, resultPC);
                return resultPC;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception in OnSocketOpened: {ex.Message}");
                return null;
            }
        }

        private void OnSocketClosed(WebSocketContext context, DataChannelPeerConnection? peerConnection)
        {
            try
            {
                if (peerConnection != null && connections.TryRemove(peerConnection.SessionID, out var removeItem))
                    removeItem.Dispose();
            }
            catch
            {
            }

            OnWebSocketClosed(context, peerConnection);
        }

#pragma warning disable IDE0060
        private static DataChannelPeerConnection CreatePeerConnection(WebSocketContext context)
        {
            return new DataChannelPeerConnection();
        }
#pragma warning restore IDE0060

        public void SendRtpData(string mediaType, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit)
        {
            foreach (var kvp in connections)
            {
                if (kvp.Value.ConnectionState == rtcState.RTC_CONNECTED)
                    SendRtpData(kvp.Value, mediaType, payloadType, payload, timeStamp, markerBit);
            }
        }

        public void SendRtpData(Guid connectionID, string mediaType, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit)
        {
            if (connections.TryGetValue(connectionID, out var connection) && connection.ConnectionState == rtcState.RTC_CONNECTED)
                SendRtpData(connection, mediaType, payloadType, payload, timeStamp, markerBit);
        }

        public void SendRtpVideoData(int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit) =>
            SendRtpData("video", payloadType, payload, timeStamp, markerBit);

        public void SendRtpAudioData(int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit) =>
            SendRtpData("audio", payloadType, payload, timeStamp, markerBit);

        public void SendRtpVideoData(Guid connectionID, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit) =>
            SendRtpData(connectionID, "video", payloadType, payload, timeStamp, markerBit);

        public void SendRtpAudioData(Guid connectionID, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit) =>
            SendRtpData(connectionID, "audio", payloadType, payload, timeStamp, markerBit);

        public void CloseConnection(Guid connectionID)
        {
            if (connections.TryRemove(connectionID, out var removeItem))
                removeItem.Dispose();
        }

        private static void SendRtpData(DataChannelPeerConnection connection, string mediaType, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit)
        {
            if (string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase))
            {
                connection.SendRtpVideoData(payloadType, payload, timeStamp, markerBit);
                return;
            }

            if (string.Equals(mediaType, "audio", StringComparison.OrdinalIgnoreCase))
                connection.SendRtpAudioData(payloadType, payload, timeStamp, markerBit);
        }

        private static bool IsEndOfCandidates(string message)
        {
            return message.Equals("a=end-of-candidates", StringComparison.OrdinalIgnoreCase) ||
                   message.Equals("end-of-candidates", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseIceCandidate(string message, out string candidate, out string mid)
        {
            candidate = string.Empty;
            mid = string.Empty;

            try
            {
                using JsonDocument document = JsonDocument.Parse(message);
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("candidate", out JsonElement candidateElement))
                    candidate = candidateElement.GetString() ?? string.Empty;

                if (root.TryGetProperty("sdpMid", out JsonElement midElement))
                    mid = midElement.GetString() ?? string.Empty;

                return !string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(mid);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            foreach (var item in connections.Values)
                item.Dispose();

            connections.Clear();
            webSocketServer.Stop();
            GC.SuppressFinalize(this);
        }
    }
}









