using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace OPNX.Lib.Network.Protocol.HttpProxy
{
    // HTTP requests use one downstream connection each. CONNECT preserves the opaque tunnel.
    internal sealed class HttpProxySession : IDisposable
    {
        #region Fields
        private readonly TcpClient _client;
        private readonly HttpProxyOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cancellation;
        private bool _responseStarted;
        #endregion

        #region Constructors
        internal HttpProxySession(TcpClient client, HttpProxyOptions options, HttpClient httpClient,
            ILogger logger, CancellationToken cancellationToken)
        {
            _client = client;
            _options = options;
            _httpClient = httpClient;
            _logger = logger;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }
        #endregion

        #region Properties
        internal Task Completion { get; set; } = Task.CompletedTask;
        #endregion

        #region Public Methods
        public void Abort() => _client.Dispose();

        public void Dispose()
        {
            _client.Dispose();
            _cancellation.Dispose();
        }

        internal async Task RunAsync()
        {
            ProxyInputStream? input = null;
            try
            {
                input = new ProxyInputStream(_client.GetStream());
                _cancellation.CancelAfter(_options.HeaderTimeout);
                string header = await ReadHeaderAsync(input).ConfigureAwait(false);
                string[] lines = header.Split("\r\n", StringSplitOptions.None);
                string[] requestLine = lines[0].Split(' ');
                if (requestLine.Length != 3 || !IsToken(requestLine[0]) ||
                    requestLine[2] is not ("HTTP/1.0" or "HTTP/1.1"))
                    throw new ProxyRequestException(400);

                var headers = ParseHeaders(lines.Skip(1).Where(line => line.Length > 0));
                if (headers.Count(header => header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) > 1 ||
                    requestLine[2] == "HTTP/1.1" && !headers.Any(header => header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)))
                    throw new ProxyRequestException(400);
                if (headers.Count(header => header.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)) > 1)
                    throw new ProxyRequestException(400);

                Uri destination = GetDestination(requestLine[0], requestLine[1]);
                var context = new HttpProxyRequestContext((IPEndPoint)_client.Client.RemoteEndPoint!,
                    requestLine[0], destination, headers.FirstOrDefault(header => header.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)).Value);
                if (_options.AuthenticateAsync != null &&
                    !await _options.AuthenticateAsync(context, _cancellation.Token).ConfigureAwait(false))
                    throw new ProxyRequestException(407);
                if (_options.AuthorizeRequestAsync == null ||
                    !await _options.AuthorizeRequestAsync(context, _cancellation.Token).ConfigureAwait(false))
                    throw new ProxyRequestException(403);

                _cancellation.CancelAfter(_options.IdleTimeout);
                if (requestLine[0] == "CONNECT")
                {
                    if (headers.Any(header => header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && header.Value != "0"))
                        throw new ProxyRequestException(400);
                    await TunnelAsync(input, destination).ConfigureAwait(false);
                }
                else
                    await ForwardAsync(input, context, headers).ConfigureAwait(false);
            }
            catch (Exception ex) when (_cancellation.IsCancellationRequested && ex is (ObjectDisposedException or InvalidOperationException)) { }
            catch (ProxyRequestException ex) { await SendErrorAsync(ex.StatusCode).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                await SendErrorAsync(504).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or HttpRequestException)
            {
                _logger.LogDebug(ex, "HTTP proxy connection ended with a transport error.");
                await SendErrorAsync(502).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HTTP proxy request processing failed.");
                await SendErrorAsync(500).ConfigureAwait(false);
            }
            finally { input?.Dispose(); }
        }
        #endregion

        #region Private / Protected Methods
        private async Task<string> ReadHeaderAsync(Stream input)
        {
            using var header = new MemoryStream();
            byte[] single = new byte[1];
            int tail = 0;
            while (header.Length < _options.MaximumHeaderBytes)
            {
                if (await input.ReadAsync(single, _cancellation.Token).ConfigureAwait(false) == 0)
                    throw new IOException("The client closed before completing its headers.");
                byte value = single[0];
                if (value != '\r' && value != '\n' && value != '\t' && (value < 32 || value > 126))
                    throw new ProxyRequestException(400);
                header.WriteByte(value);
                tail = (tail << 8) | value;
                if (tail == 0x0d0a0d0a)
                    return Encoding.ASCII.GetString(header.GetBuffer(), 0, (int)header.Length - 4);
            }
            throw new ProxyRequestException(431);
        }

        private static List<KeyValuePair<string, string>> ParseHeaders(IEnumerable<string> lines)
        {
            List<KeyValuePair<string, string>> headers = [];
            foreach (string line in lines)
            {
                int colon = line.IndexOf(':');
                if (colon <= 0 || !IsToken(line[..colon]))
                    throw new ProxyRequestException(400);
                string value = line[(colon + 1)..].Trim(' ', '\t');
                if (value.Any(character => character < 32 && character != '\t' || character > 126))
                    throw new ProxyRequestException(400);
                headers.Add(new(line[..colon], value));
            }
            return headers;
        }

        private static bool IsToken(string value) => value.Length > 0 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character));

        private static Uri GetDestination(string method, string target)
        {
            if (target.Any(character => character <= 32 || character == '\\'))
                throw new ProxyRequestException(400);
            if (method == "CONNECT")
            {
                int colon = target.LastIndexOf(':');
                if (colon <= 0 || !int.TryParse(target[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
                    throw new ProxyRequestException(400);
                if (!Uri.TryCreate("https://" + target, UriKind.Absolute, out var tunnel) ||
                    tunnel.UserInfo.Length != 0 || tunnel.AbsolutePath != "/" || tunnel.Query.Length != 0 || tunnel.Fragment.Length != 0)
                    throw new ProxyRequestException(400);
                return tunnel;
            }
            if (!Uri.TryCreate(target, UriKind.Absolute, out var destination) || destination.Scheme != "http" ||
                destination.UserInfo.Length != 0 || destination.Fragment.Length != 0 || destination.Port <= 0)
                throw new ProxyRequestException(400);
            return destination;
        }

        private async Task ForwardAsync(Stream input, HttpProxyRequestContext context, List<KeyValuePair<string, string>> headers)
        {
            if (headers.Any(header => header.Key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)))
                throw new ProxyRequestException(501);
            var lengths = headers.Where(header => header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)).ToArray();
            var encodings = headers.Where(header => header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (lengths.Length > 1 || encodings.Length > 1 || lengths.Length > 0 && encodings.Length > 0)
                throw new ProxyRequestException(400);
            if (encodings.Length > 0 && !encodings[0].Value.Equals("chunked", StringComparison.OrdinalIgnoreCase))
                throw new ProxyRequestException(501);
            int length = 0;
            if (lengths.Length > 0 && (!int.TryParse(lengths[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out length) || length < 0))
                throw new ProxyRequestException(400);
            if (length > _options.MaximumRequestBodyBytes)
                throw new ProxyRequestException(413);

            string[] expectations = [.. headers.Where(header => header.Key.Equals("Expect", StringComparison.OrdinalIgnoreCase)).Select(header => header.Value)];
            if (expectations.Any(value => !value.Equals("100-continue", StringComparison.OrdinalIgnoreCase)))
                throw new ProxyRequestException(417);
            if (expectations.Length > 0)
                await WriteAsync(_client.GetStream(), "HTTP/1.1 100 Continue\r\n\r\n").ConfigureAwait(false);

            byte[] body;
            if (encodings.Length > 0)
                body = await ReadChunkedBodyAsync(input).ConfigureAwait(false);
            else
            {
                body = new byte[length];
                await ReadExactlyAsync(input, body).ConfigureAwait(false);
            }

            using var request = new HttpRequestMessage(new HttpMethod(context.Method), context.Destination)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = new ByteArrayContent(body)
            };
            string[] connectionHeaders = GetConnectionHeaders(headers);
            foreach (var header in headers)
            {
                if (IsHopByHop(header.Key, connectionHeaders) || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Expect", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            request.Headers.Via.Add(new ViaHeaderValue("1.1", "OPNX-Proxy"));
            _cancellation.CancelAfter(_options.IdleTimeout);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancellation.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.SwitchingProtocols)
                throw new ProxyRequestException(501);

            var responseHeaders = response.Headers.Concat(response.Content.Headers).SelectMany(header =>
                header.Value.Select(value => new KeyValuePair<string, string>(header.Key, value))).ToList();
            connectionHeaders = GetConnectionHeaders(responseHeaders);
            var text = new StringBuilder($"HTTP/1.1 {(int)response.StatusCode} Response\r\n");
            foreach (var header in responseHeaders)
            {
                if (!IsHopByHop(header.Key, connectionHeaders))
                    text.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }
            text.Append("Connection: close\r\nVia: 1.1 OPNX-Proxy\r\n\r\n");
            _responseStarted = true;
            await WriteAsync(_client.GetStream(), text.ToString()).ConfigureAwait(false);
            if (context.Method != "HEAD")
            {
                using var content = await response.Content.ReadAsStreamAsync(_cancellation.Token).ConfigureAwait(false);
                await CopyAsync(content, _client.GetStream()).ConfigureAwait(false);
            }
        }

        private static string[] GetConnectionHeaders(IEnumerable<KeyValuePair<string, string>> headers) =>
            [.. headers.Where(header => header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                .SelectMany(header => header.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))];

        private static bool IsHopByHop(string name, string[] connectionHeaders) =>
            name.Equals("Connection", StringComparison.OrdinalIgnoreCase) || name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("TE", StringComparison.OrdinalIgnoreCase) || name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) || connectionHeaders.Contains(name, StringComparer.OrdinalIgnoreCase);

        private async Task<byte[]> ReadChunkedBodyAsync(Stream input)
        {
            using var body = new MemoryStream();
            int trailerBytes = 0;
            while (true)
            {
                string line = await ReadLineAsync(input).ConfigureAwait(false);
                if (!int.TryParse(line.Split(';')[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int size) || size < 0)
                    throw new ProxyRequestException(400);
                if (size == 0)
                {
                    while ((line = await ReadLineAsync(input).ConfigureAwait(false)).Length > 0)
                    {
                        trailerBytes += line.Length + 2;
                        if (trailerBytes > _options.MaximumHeaderBytes)
                            throw new ProxyRequestException(431);
                        ParseHeaders([line]);
                    }
                    return body.ToArray();
                }
                if (size > _options.MaximumRequestBodyBytes - body.Length)
                    throw new ProxyRequestException(413);
                byte[] chunk = new byte[size];
                await ReadExactlyAsync(input, chunk).ConfigureAwait(false);
                body.Write(chunk);
                if (await ReadLineAsync(input).ConfigureAwait(false) != string.Empty)
                    throw new ProxyRequestException(400);
            }
        }

        private async Task<string> ReadLineAsync(Stream input)
        {
            var line = new StringBuilder();
            byte[] single = new byte[1];
            while (line.Length < _options.MaximumHeaderBytes)
            {
                await ReadExactlyAsync(input, single).ConfigureAwait(false);
                if (single[0] == '\n')
                {
                    if (line.Length == 0 || line[^1] != '\r')
                        throw new ProxyRequestException(400);
                    return line.ToString(0, line.Length - 1);
                }
                line.Append((char)single[0]);
            }
            throw new ProxyRequestException(431);
        }

        private async Task ReadExactlyAsync(Stream input, Memory<byte> buffer)
        {
            while (buffer.Length > 0)
            {
                _cancellation.CancelAfter(_options.IdleTimeout);
                int read = await input.ReadAsync(buffer, _cancellation.Token).ConfigureAwait(false);
                if (read == 0)
                    throw new ProxyRequestException(400);
                buffer = buffer[read..];
            }
        }

        private async Task TunnelAsync(Stream input, Uri destination)
        {
            using var upstream = new TcpClient();
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token))
            {
                connect.CancelAfter(_options.ConnectTimeout);
                await upstream.ConnectAsync(destination.IdnHost, destination.Port, connect.Token).ConfigureAwait(false);
            }
            var clientStream = _client.GetStream();
            var upstreamStream = upstream.GetStream();
            _responseStarted = true;
            await WriteAsync(clientStream, "HTTP/1.1 200 Connection Established\r\n\r\n").ConfigureAwait(false);
            await Task.WhenAll(
                CopyTunnelAsync(input, upstreamStream, upstream),
                CopyTunnelAsync(upstreamStream, clientStream, _client)).ConfigureAwait(false);
        }

        private async Task CopyTunnelAsync(Stream source, Stream destination, TcpClient destinationClient)
        {
            try
            {
                await CopyAsync(source, destination).ConfigureAwait(false);
                destinationClient.Client.Shutdown(SocketShutdown.Send);
            }
            catch
            {
                _cancellation.Cancel();
                throw;
            }
        }

        private async Task CopyAsync(Stream source, Stream destination)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                while (true)
                {
                    _cancellation.CancelAfter(_options.IdleTimeout);
                    int read = await source.ReadAsync(buffer, _cancellation.Token).ConfigureAwait(false);
                    if (read == 0)
                        return;
                    _cancellation.CancelAfter(_options.IdleTimeout);
                    await destination.WriteAsync(buffer.AsMemory(0, read), _cancellation.Token).ConfigureAwait(false);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private async Task WriteAsync(Stream destination, string text)
        {
            _cancellation.CancelAfter(_options.IdleTimeout);
            await destination.WriteAsync(Encoding.ASCII.GetBytes(text), _cancellation.Token).ConfigureAwait(false);
        }

        private async Task SendErrorAsync(int statusCode)
        {
            if (_responseStarted)
                return;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                string challenge = statusCode == 407 ? $"Proxy-Authenticate: {_options.AuthenticationChallenge}\r\n" : string.Empty;
                string response = $"HTTP/1.1 {statusCode} {(HttpStatusCode)statusCode}\r\n{challenge}Content-Length: 0\r\nConnection: close\r\n\r\n";
                await _client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(response), timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException or InvalidOperationException) { }
        }

        // Return prefetched bytes immediately; BufferedStream can wait for additional
        // input while draining a partial buffer, which would stall a CONNECT handshake.
        private sealed class ProxyInputStream(Stream source) : Stream
        {
            private readonly byte[] _buffer = new byte[16 * 1024];
            private int _offset;
            private int _length;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (destination.Length == 0)
                    return 0;
                if (_offset == _length)
                {
                    if (destination.Length >= _buffer.Length)
                        return await source.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
                    _length = await source.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
                    _offset = 0;
                }
                int count = Math.Min(destination.Length, _length - _offset);
                _buffer.AsMemory(_offset, count).CopyTo(destination);
                _offset += count;
                return count;
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private sealed class ProxyRequestException(int statusCode) : Exception
        {
            public int StatusCode { get; } = statusCode;
        }
        #endregion
    }
}
