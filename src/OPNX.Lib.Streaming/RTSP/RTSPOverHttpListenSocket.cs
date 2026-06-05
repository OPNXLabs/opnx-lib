using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Streaming.RTSP.Messages;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace OPNX.Lib.Streaming.RTSP
{
    public class RtspOverHttpListenSocket(TcpListener tcpListener, ILogger? logger = null) : IRtspListenSocket
    {
        private readonly TcpListener _tcpListener = tcpListener;
        private readonly ILogger _logger = logger ?? NullLogger.Instance;
        //private readonly ILoggerFactory _loggerFactory;
        private CancellationTokenSource? _stop;

        private readonly BlockingCollection<RTSPHttpServerTransport> _newConnections = new(100);
        private readonly ConcurrentDictionary<string, RTSPHttpServerTransport> _activesSessions = new(StringComparer.Ordinal);

        private readonly byte[] getResponse = Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\n"
                        + "Server: SharpRTSP\r\n"
                        + "Connection: close\r\n"
                        + "Date: Thu, 19 Aug 1982 18:30:00 GMT\r\n"
                        + "Cache-Control: no-store\r\n"
                        + "Pragma: no-cache\r\n"
                        + "Content-Type: application/x-rtsp-tunnelled\r\n"
                        + "\r\n");

        //public RtspOverHttpListenSocket(TcpListener tcpListener, ILoggerFactory loggerFactory = null)
        //public RtspOverHttpListenSocke
        //{
        //    _tcpListener = tcpListener;
        //    //_logger = loggerFactory?.CreateLogger<RtspOverHttpListenSocket>() as ILogger ?? NullLogger.Instance;
        //    //_loggerFactory = loggerFactory;
        //}

        //public IRtspTransport Accept() => _newConnections.Take(_stop.Token);

        public async Task<IRtspTransport> AcceptAsync(CancellationToken cancellationToken)
        {
            if (_stop?.IsCancellationRequested != false)
            {
                throw new InvalidOperationException("Not listening. You must call the Start() method before calling this method.");
            }

            return await Task.Run(() => _newConnections.Take(cancellationToken) as IRtspTransport).ConfigureAwait(false);
        }

        public void Start()
        {
            if (_stop?.IsCancellationRequested == false)
                return;

            _stop = new();
            _tcpListener.Start();
            _ = Task.Factory.StartNew(async () => await AcceptConnections(_stop.Token).ConfigureAwait(false),
                _stop.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Current);
        }

        public void Stop()
        {
            _stop?.Cancel();
            _tcpListener.Stop();
        }

        private async Task AcceptConnections(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
#if NET8_0_OR_GREATER
                    var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
#else
                    var client = await _tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
#endif
                    _logger.LogDebug("Connection from {remoteEndPoint}", client.Client.RemoteEndPoint?.ToString() ?? "unknown");
                    await HandleHeaderAndAddToSessions(client, cancellationToken).ConfigureAwait(false);

                    // remove old session
                    var sessionToRemove = _activesSessions
                        .Where(kv => kv.Value.IsObsolete)
                        .Select(kv => kv.Key)
                        .ToList();
                    foreach (var session in sessionToRemove)
                    {
                        _activesSessions.TryRemove(session, out _);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Accept connections canceled");
            }
        }

        private async Task HandleHeaderAndAddToSessions(TcpClient client, CancellationToken cancellationToken)
        {
            // prevent bad client to totally block the system
            client.ReceiveTimeout = 5000;
            try
            {
                var clientStream = GetStream(client);
                var firstLine = await ReadOneLine(clientStream, cancellationToken).ConfigureAwait(false);

                bool isPostChannel;
                var parts = firstLine?.Split(' ') ?? [];
                switch (parts.Length)
                {
                    case 3 when string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase):
                        isPostChannel = false;
                        break;
                    case 3 when string.Equals(parts[0], "POST", StringComparison.OrdinalIgnoreCase):
                        isPostChannel = true;
                        break;
                    default:
                        _logger.LogWarning("Invalid message receive {message}", firstLine?.ToString() ?? "unknown");
                        client.Dispose();
                        return;
                }
                Dictionary<string, string> headers = await ReadHeaders(clientStream, cancellationToken).ConfigureAwait(false);
                client.ReceiveTimeout = 0;

                if (!headers.TryGetValue("x-sessioncookie", out var sessionCookie))
                {
                    _logger.LogWarning("No session cookie find");
                    client.Dispose();
                    return;
                }

                if (!_activesSessions.TryGetValue(sessionCookie, out var session))
                {
                    _logger.LogDebug("Create session {sessionCookie}", sessionCookie);
                    //session = new(_loggerFactory?.CreateLogger<RTSPHttpServerTransport>());
                    session = new(logger);
                    _activesSessions[sessionCookie] = session;
                }

                if (isPostChannel)
                {
                    if (!headers.TryGetValue(RtspHeaderNames.ContentType, out var value)
                        || string.Equals(value, "application/x-rtsp-tunnelled", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _logger.LogWarning("Invalid content-type header");
                        client.Dispose();
                        return;
                    }

                    var inError = session.UpdatePostChannel(client, clientStream) switch
                    {
                        RTSPHttpServerTransport.UpdateState.Ok => false,
                        RTSPHttpServerTransport.UpdateState.NewSession => !_newConnections.TryAdd(session),
                        _ => true,
                    };

                    if (inError)
                    {
                        _logger.LogWarning("Removing session {sessionCookie} due to error", sessionCookie);
                        session.Close();
                        _activesSessions.TryRemove(sessionCookie, out session);
                    }
                }
                else
                {
                    if (!headers.TryGetValue("Accept", out var value)
                        || string.Equals(value, "application/x-rtsp-tunnelled", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _logger.LogWarning("Invalid accept header");
                        client.Dispose();
                        return;
                    }

                    var inError = session.UpdateGetChannel(client, clientStream) switch
                    {
                        RTSPHttpServerTransport.UpdateState.Ok => false,
                        RTSPHttpServerTransport.UpdateState.NewSession => !_newConnections.TryAdd(session),
                        _ => true,
                    };

                    await clientStream.WriteAsync(getResponse, cancellationToken).ConfigureAwait(false);

                    if (inError)
                    {
                        _logger.LogWarning("Removing session {sessionCookie} due to error", sessionCookie);
                        session.Close();
                        _activesSessions.TryRemove(sessionCookie, out session);
                    }
                }

            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Invalid data from client");
                client.Dispose();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Operation canceled");
                client.Dispose();
            }
            catch (IOException)
            {
                _logger.LogWarning("Error during read");
                client.Dispose();
            }
        }

        /// <summary>
        /// Manual read stream for a full line,
        /// </summary>
        /// <param name="stream">The stream to read</param>
        /// <param name="cancellationToken">the cancelation token</param>
        /// <returns>A line (without /r and /n)</returns>
        /// <exception cref="InvalidDataException">Raise when data is too large</exception>
        /// <remarks>
        /// Exist ecause streamreader read too much data in the buffer
        /// So slowly read one by one
        /// </remarks>
        private static async Task<string> ReadOneLine(Stream stream, CancellationToken cancellationToken)
        {
            // 
            // So slowly read one by one
            // 2048 is arbitrary, if a line of the http request is greater than 2048 
            // the client is doing something stange.
            byte[] buffer = ArrayPool<byte>.Shared.Rent(2048);

            for (int i = 0; i < buffer.Length; i++)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(i, 1), cancellationToken).ConfigureAwait(false);
                if (n != 1 || buffer[i] == '\n')
                {
                    var result = Encoding.UTF8.GetString(buffer, 0, i);
                    ArrayPool<byte>.Shared.Return(buffer);
                    return result;
                }
                if (buffer[i] == '\r')
                {
                    // skip \r
                    i--;
                }
            }
            throw new InvalidDataException("Line too long, invalid message");

        }

        private static async Task<Dictionary<string, string>> ReadHeaders(Stream stream, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var headerLine = await ReadOneLine(stream, cancellationToken).ConfigureAwait(false);
            while (!string.IsNullOrEmpty(headerLine))
            {
                var headerParts = headerLine.Split(':', 2);
                if (headerParts.Length > 1) headers.Add(headerParts[0], headerParts[1].Trim());
                headerLine = await ReadOneLine(stream, cancellationToken).ConfigureAwait(false);
            }

            return headers;
        }

        protected virtual Stream GetStream(TcpClient client)
        {
            return client.GetStream();
        }
    }
}


