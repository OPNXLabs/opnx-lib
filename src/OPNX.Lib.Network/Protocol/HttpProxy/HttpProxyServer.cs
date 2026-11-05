using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;

namespace OPNX.Lib.Network.Protocol.HttpProxy
{
    /// <summary>Explicit HTTP/1.x forward proxy and CONNECT tunnel. No TLS interception or caching.</summary>
    public sealed class HttpProxyServer : IDisposable, IAsyncDisposable
    {
        #region Fields
        private readonly TcpListener _listener;
        private readonly int _port;
        private readonly HttpProxyOptions _options;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<HttpProxyServer> _logger;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly Lock _sessionLock = new();
        private readonly HashSet<HttpProxySession> _sessions = [];
        private CancellationTokenSource? _cancellation;
        private Task? _acceptTask;
        private bool _disposed;
        #endregion

        #region Constructors
        public HttpProxyServer(int port, HttpProxyOptions? options = null, ILoggerFactory? loggerFactory = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(port);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
            _options = options ?? new HttpProxyOptions();
            ValidateOptions(_options);
            _port = port;
            _listener = new TcpListener(_options.ListenAddress, port);
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<HttpProxyServer>();
            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                UseProxy = false,
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = _options.ConnectTimeout,
                MaxResponseHeadersLength = (_options.MaximumHeaderBytes + 1023) / 1024,
                ActivityHeadersPropagator = null
            }) { Timeout = Timeout.InfiniteTimeSpan };
        }
        #endregion

        #region Properties
        public int Port => _listener.LocalEndpoint is IPEndPoint endPoint ? endPoint.Port : _port;
        public int ConnectionCount
        {
            get { lock (_sessionLock) return _sessions.Count; }
        }
        #endregion

        #region Public Methods
        public void Start()
        {
            _lifecycleGate.Wait();
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_acceptTask != null)
                    return;

                _listener.Start();
                _cancellation = new CancellationTokenSource();
                var cancellationToken = _cancellation.Token;
                _acceptTask = Task.Run(() => AcceptAsync(cancellationToken));
            }
            finally { _lifecycleGate.Release(); }
        }

        public void Stop() => StopAsync().GetAwaiter().GetResult();

        public async Task StopAsync()
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try { await StopCoreAsync().ConfigureAwait(false); }
            finally { _lifecycleGate.Release(); }
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                    return;
                _disposed = true;
                await StopCoreAsync().ConfigureAwait(false);
                _httpClient.Dispose();
            }
            finally { _lifecycleGate.Release(); }
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Private / Protected Methods
        private static void ValidateOptions(HttpProxyOptions options)
        {
            ArgumentNullException.ThrowIfNull(options.ListenAddress);
            ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumConcurrentConnections, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumHeaderBytes, 1024);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaximumHeaderBytes, 1024 * 1024);
            ArgumentOutOfRangeException.ThrowIfNegative(options.MaximumRequestBodyBytes);
            foreach (var timeout in new[] { options.HeaderTimeout, options.ConnectTimeout, options.IdleTimeout })
            {
                if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
                    throw new ArgumentOutOfRangeException(nameof(options), "Timeouts must be finite positive durations.");
            }
            if (string.IsNullOrWhiteSpace(options.AuthenticationChallenge) ||
                options.AuthenticationChallenge.Any(character => character < ' ' || character > '~'))
                throw new ArgumentException("AuthenticationChallenge must be a single printable ASCII header value.", nameof(options));
        }

        private async Task AcceptAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    lock (_sessionLock)
                    {
                        if (cancellationToken.IsCancellationRequested || _sessions.Count >= _options.MaximumConcurrentConnections)
                        {
                            client.Dispose();
                            continue;
                        }

                        var session = new HttpProxySession(client, _options, _httpClient,
                            _loggerFactory.CreateLogger<HttpProxySession>(), cancellationToken);
                        _sessions.Add(session);
                        session.Completion = Task.Run(() => RunSessionAsync(session), CancellationToken.None);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HTTP proxy listener stopped unexpectedly.");
            }
        }

        private async Task RunSessionAsync(HttpProxySession session)
        {
            try { await session.RunAsync().ConfigureAwait(false); }
            finally
            {
                session.Dispose();
                lock (_sessionLock) _sessions.Remove(session);
            }
        }

        private async Task StopCoreAsync()
        {
            if (_acceptTask == null)
                return;

            _cancellation!.Cancel();
            _listener.Stop();
            await _acceptTask.ConfigureAwait(false);

            HttpProxySession[] sessions;
            lock (_sessionLock) sessions = [.. _sessions];
            foreach (var session in sessions)
                session.Abort();
            await Task.WhenAll(sessions.Select(session => session.Completion)).ConfigureAwait(false);

            _acceptTask = null;
            _cancellation.Dispose();
            _cancellation = null;
        }
        #endregion
    }
}
