using DataChannelDotnet.Bindings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Streaming.WebRTC.Abstractions;
using OPNX.Lib.Streaming.WebRTC.Events;
using System.Collections.Concurrent;
using System.Text.Json;

namespace OPNX.Lib.Streaming.WebRTC.DataChannel
{
    public class DataChannelSignalServer : IWebRtcSignalServer
    {
        #region Fields
        private readonly WebApplication _application;
        private readonly ConcurrentDictionary<Guid, DataChannelClientSession> _sessions = new();
        private readonly ConcurrentDictionary<Guid, Task> _closingTasks = new();
        private readonly Lock _connectionLock = new();
        private readonly ILogger _logger;
        private bool _disposed;
        #endregion

        #region Constructors
        public DataChannelSignalServer(int port, ILogger? logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(DataChannelSignalServer).Assembly.GetName().Name
            });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));
            _application = builder.Build();
            _application.UseWebSockets();
            _application.Run(HandleRequestAsync);
            try
            {
                _application.StartAsync().GetAwaiter().GetResult();
                Port = new Uri(_application.Urls.First()).Port;
            }
            catch
            {
                _application.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw;
            }
        }
        #endregion

        #region Events
        public event EventHandler<WebRtcClientOpenedEventArgs>? ClientOpened;
        public event EventHandler<WebRtcClientClosedEventArgs>? ClientClosed;
        #endregion

        #region Properties
        public int Port { get; }
        #endregion

        #region Private / Protected Methods
        private async Task HandleRequestAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            var request = context.Request;
            Uri requestUri = new($"{(request.IsHttps ? "wss" : "ws")}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}");
            DataChannelClientSession session = new(socket, requestUri);
            lock (_connectionLock)
            {
                if (_disposed)
                {
                    socket.Abort();
                    socket.Dispose();
                    return;
                }
                session.SocketOpened += OnSocketOpened;
                session.MessageReceived += OnMessageReceived;
                session.SocketClosed += client => CloseConnection(client.ConnectionID);
                session.SocketFailed += (client, error) =>
                {
                    if (!client.IsClosed)
                        _logger.LogWarning(error, "WebRTC signaling failed. ConnectionID={ConnectionID}.", client.ConnectionID);
                };
                _sessions.TryAdd(session.ConnectionID, session);
            }

            try
            {
                await session.RunAsync(context.RequestAborted).ConfigureAwait(false);
            }
            finally
            {
                CloseConnection(session.ConnectionID);
                if (_closingTasks.TryGetValue(session.ConnectionID, out var closing))
                    await closing.ConfigureAwait(false);
            }
        }

        private async Task OnSocketOpened(DataChannelClientSession session)
        {
            DataChannelPeerConnection peer;
            lock (_connectionLock)
            {
                if (_disposed || session.IsClosed)
                    return;
                peer = new DataChannelPeerConnection(sessionID: session.ConnectionID);
                session.PeerConnection = peer;
            }
            Lock candidateLock = new();
            List<string> candidates = [];
            bool offerSent = false;
            peer.LocalCandidateCreated += (sender, candidate) =>
            {
                string message = JsonSerializer.Serialize(new { candidate = candidate.Content, sdpMid = candidate.Mid });
                lock (candidateLock)
                {
                    if (!offerSent)
                        candidates.Add(message);
                    else
                        _ = SendCandidateAsync(session, message);
                }
            };
            peer.ConnectionStateChanged += (sender, state) =>
            {
                if (state is rtcState.RTC_FAILED or rtcState.RTC_CLOSED or rtcState.RTC_DISCONNECTED)
                    CloseConnection(session.ConnectionID);
            };
            WebRtcClientOpenedEventArgs args = new(session.RequestUri, session.ConnectionID, int.MinValue);
            ClientOpened?.Invoke(this, args);
            peer.VideoSourceID = args.VideoSourceID;
            if (peer.VideoSourceID == int.MinValue || session.IsClosed)
            {
                CloseConnection(session.ConnectionID);
                return;
            }
            string sdp = (await peer.CreateOfferAsync().ConfigureAwait(false)).Sdp;
            await session.SendAsync(sdp).ConfigureAwait(false);
            lock (candidateLock)
            {
                offerSent = true;
                foreach (string candidate in candidates)
                    _ = SendCandidateAsync(session, candidate);
                candidates.Clear();
            }
        }

        private async Task SendCandidateAsync(DataChannelClientSession session, string message)
        {
            try
            {
                if (!session.IsClosed)
                    await session.SendAsync(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!session.IsClosed)
                    _logger.LogWarning(ex, "Failed to send an ICE candidate. ConnectionID={ConnectionID}.", session.ConnectionID);
                CloseConnection(session.ConnectionID);
            }
        }

        private void OnMessageReceived(DataChannelClientSession session, string message)
        {
            var connection = session.PeerConnection;
            if (connection == null || string.IsNullOrWhiteSpace(message))
                return;
            if (message.StartsWith("v=", StringComparison.Ordinal))
            {
                connection.SetRemoteAnswer(message);
                return;
            }
            if (message.Equals("a=end-of-candidates", StringComparison.OrdinalIgnoreCase) ||
                message.Equals("end-of-candidates", StringComparison.OrdinalIgnoreCase))
                return;
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.TryGetProperty("candidate", out var candidate) && root.TryGetProperty("sdpMid", out var mid) &&
                candidate.ValueKind == JsonValueKind.String && mid.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(candidate.GetString()))
                connection.AddRemoteCandidate(candidate.GetString()!, mid.GetString()!);
        }

        private void SendRtpData(Guid connectionID, bool video, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit)
        {
            if (!_sessions.TryGetValue(connectionID, out var session) || session.PeerConnection is not { IsConnected: true } connection)
                return;
            try
            {
                if (video)
                    connection.SendRtpVideoData(payloadType, payload, timeStamp, markerBit);
                else
                    connection.SendRtpAudioData(payloadType, payload, timeStamp, markerBit);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send WebRTC RTP data. ConnectionID={ConnectionID}.", connectionID);
                CloseConnection(connectionID);
            }
        }

        private async Task CloseSessionAsync(DataChannelClientSession session, TaskCompletionSource completion)
        {
            try
            {
                await session.Initialization.ConfigureAwait(false);
                await session.SocketCloseTask.ConfigureAwait(false);
                session.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close a WebRTC connection. ConnectionID={ConnectionID}.", session.ConnectionID);
            }
            finally
            {
                try
                {
                    if (session.PeerConnection is { } peer)
                        ClientClosed?.Invoke(this, new WebRtcClientClosedEventArgs(session.RequestUri, session.ConnectionID, peer.VideoSourceID));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to notify WebRTC connection close. ConnectionID={ConnectionID}.", session.ConnectionID);
                }
                completion.TrySetResult();
                _closingTasks.TryRemove(session.ConnectionID, out _);
            }
        }
        #endregion

        #region Public Methods
        public void SendRtpVideoData(int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit)
        {
            foreach (var id in _sessions.Keys)
                SendRtpVideoData(id, payloadType, payload, timeStamp, markerBit);
        }
        public void SendRtpAudioData(int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit)
        {
            foreach (var id in _sessions.Keys)
                SendRtpAudioData(id, payloadType, payload, timeStamp, markerBit);
        }
        public void SendRtpVideoData(Guid connectionID, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit) =>
            SendRtpData(connectionID, true, payloadType, payload, timeStamp, markerBit);
        public void SendRtpAudioData(Guid connectionID, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit) =>
            SendRtpData(connectionID, false, payloadType, payload, timeStamp, markerBit);

        public void CloseConnection(Guid connectionID)
        {
            DataChannelClientSession? session;
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_connectionLock)
            {
                if (!_sessions.TryRemove(connectionID, out session))
                    return;
                _closingTasks.TryAdd(connectionID, completion.Task);
            }
            try
            {
                session.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close a signaling socket. ConnectionID={ConnectionID}.", connectionID);
            }
            _ = Task.Run(() => CloseSessionAsync(session, completion));
        }

        public void Dispose()
        {
            lock (_connectionLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }
            foreach (var id in _sessions.Keys)
                CloseConnection(id);
            Task.WhenAll(_closingTasks.Values).GetAwaiter().GetResult();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                _application.StopAsync(timeout.Token).GetAwaiter().GetResult();
            }
            finally
            {
                _application.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
