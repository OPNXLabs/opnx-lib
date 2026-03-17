using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Network.Abstractions;
using OPNX.Lib.Network.Abstractions.Events;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;

namespace OPNX.Lib.Network.Transport.Tcp
{

    /// <summary>
    /// OPNX.Common Transport: TCP 연결 + PipeReader/PipeWriter 제공 + 자동 재접속 옵션
    /// </summary>
    public class TcpConnection : DisposableBase, IConnection
    {
        #region Fields
        private readonly SemaphoreSlim _gate = new(1, 1);

        private TcpClient? _tcpClient;

        private PipeReader? _reader;
        private PipeWriter? _writer;

        private IPEndPoint? _ipEndPoint;

        private readonly TcpConnectionOptions _options;

        private volatile bool _reconnectEnabled = false;
        private readonly CancellationTokenSource _reconnectCts = new();
        private Task? _reconnectTask;
        #endregion

        #region Properties
        public Guid SessionID { get; } = Guid.NewGuid();

        public bool IsConnected => _tcpClient?.Connected ?? false;

        public Socket? Socket => _tcpClient?.Client;

        public PipeReader? Reader => _reader;
        public PipeWriter? Writer => _writer;

        public IPEndPoint? IPEndPoint => _ipEndPoint;
        #endregion

        #region Events
        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        #endregion

        #region Constructors
        public TcpConnection(TcpConnectionOptions? options = null)
        {
            _options = options ?? TcpConnectionOptions.Default;

            _reconnectEnabled = _options.Common.EnableReconnect;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// 외부에서 이미 accept된 TcpClient를 연결로 래핑할 때
        /// </summary>
        public bool Attach(TcpClient tcpClient)
        {
            if (tcpClient == null) return false;
            if (IsDisposed) return false;

            _gate.Wait();
            try
            {
                CleanupTransport();

                _tcpClient = tcpClient;
                ConfigureTcpClient(_tcpClient, _options);

                return InnerConnect();
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// 동기 Connect: Timeout 적용(내부적으로 async 기반)
        /// </summary>
        public bool Connect(EndPoint endPoint)
        {
            if (IsDisposed) return false;

            try
            {
                return ConnectAsync(endPoint).GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 비동기 Connect: DNS/hostname 지원 + Timeout 적용
        /// </summary>
        public async Task<bool> ConnectAsync(EndPoint? endPoint, CancellationToken cancellationToken = default)
        {
            if (IsDisposed) return false;

            if (endPoint is not IPEndPoint ipEndPoint)
                throw new ArgumentException("IPEndPoint required", nameof(endPoint));

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected) return true;

                CleanupTransport();

                _ipEndPoint = ipEndPoint;

                string address = NormalizeLoopback(_ipEndPoint.Address.ToString());
                int port = _ipEndPoint.Port;

                if (_reconnectEnabled)
                    EnsureReconnectTaskStarted();

                var tcp = new TcpClient();
                ConfigureTcpClient(tcp, _options);

                int connectTimeout = _options.Common.ConnectTimeoutMs;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(connectTimeout);

                try
                {
                    // hostname/IP 모두 지원
                    await tcp.ConnectAsync(address, port, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    SafeDispose(tcp);
                    return false;
                }
                catch
                {
                    SafeDispose(tcp);
                    return false;
                }

                if (!tcp.Connected)
                {
                    SafeDispose(tcp);
                    return false;
                }

                _tcpClient = tcp;
                return InnerConnect();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DisconnectAsync(DisconnectReason reason = DisconnectReason.Requested, CancellationToken cancellationToken = default)
        {
            if (IsDisposed) return;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            bool shouldReconnect = false;

            try
            {
                // 소켓 종료
                try
                {
                    if (_tcpClient?.Client != null)
                    {
                        try { _tcpClient.Client.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
                        try { _tcpClient.Client.Close(); } catch { /* ignore */ }
                    }
                }
                finally
                {
                    CleanupTransport();
                }

                // reconnect 여부는 락 안에서 결정만 해두고,
                // 실제 시작은 락 밖에서(이벤트 처리 중 데드락/경합 방지)
                shouldReconnect = _reconnectEnabled && !_reconnectCts.IsCancellationRequested && reason != DisconnectReason.Stopped;
            }
            finally
            {
                _gate.Release();
            }

            Disconnected?.Invoke(this, new DisconnectedEventArgs(SessionID, reason));

            if (shouldReconnect)
                EnsureReconnectTaskStarted();
        }
        public void Disconnect(DisconnectReason reason = DisconnectReason.Requested)
        {
            if (IsDisposed) return;

            _gate.Wait();

            bool shouldReconnect = false;

            try
            {
                // 소켓 종료
                try
                {
                    if (_tcpClient?.Client != null)
                    {
                        try { _tcpClient.Client.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
                        try { _tcpClient.Client.Close(); } catch { /* ignore */ }
                    }
                }
                finally
                {
                    CleanupTransport();
                }


                // reconnect 여부는 락 안에서 결정만 해두고,
                // 실제 시작은 락 밖에서(이벤트 처리 중 데드락/경합 방지)
                shouldReconnect = _reconnectEnabled && !_reconnectCts.IsCancellationRequested && reason != DisconnectReason.Stopped;
            }
            finally
            {
                _gate.Release();
            }

            Disconnected?.Invoke(this, new DisconnectedEventArgs(SessionID, reason));

            if (shouldReconnect)
                EnsureReconnectTaskStarted();
        }
        #endregion

        #region Private / Protected Methods
        protected override void OnDispose()
        {
            OnDisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        protected override async ValueTask OnDisposeAsync()
        {
            _reconnectEnabled = false;

            try
            {
                await DisconnectAsync();
            }
            catch { /* ignore */ }

            _reconnectCts.Cancel();

            try
            {
                var t = _reconnectTask;
                if (t != null)
                    await t.ConfigureAwait(false);
            }
            catch { /* ignore */ }
            finally
            {
                _reconnectCts.Dispose();
                _gate.Dispose();
            }
        }

        // ----------------- Internals -----------------

        private bool InnerConnect()
        {
            if (_tcpClient == null) return false;

            try
            {
                // Stream 설정
                var stream = _tcpClient.GetStream();
                stream.ReadTimeout = _options.StreamReadTimeoutMs;
                stream.WriteTimeout = _options.StreamWriteTimeoutMs;

                // Pipe 구성
                _reader = PipeReader.Create(stream);
                _writer = PipeWriter.Create(stream);

                Connected?.Invoke(this, new ConnectedEventArgs(SessionID));

                return true;
            }
            catch
            {
                CleanupTransport();
                return false;
            }
        }

        private void EnsureReconnectTaskStarted()
        {
            // 중복 생성 방지
            if (_reconnectTask is { IsCompleted: false })
                return;

            // 완료된 태스크면 교체
            _reconnectTask = Task.Run(() => ReconnectLoopAsync(_reconnectCts.Token));
        }

        private async Task ReconnectLoopAsync(CancellationToken token)
        {
            // reconnect가 꺼지면 즉시 종료
            while (!IsDisposed && !token.IsCancellationRequested)
            {
                if (!_reconnectEnabled)
                    return;

                // 이미 연결됐으면 종료
                if (IsConnected)
                    return;

                try
                {
                    // 연결 시도 (reconnect를 다시 켜지 않도록 enableReconnect=false)
                    bool isConnected = await ConnectAsync(_ipEndPoint, token).ConfigureAwait(false);

                    if (isConnected)
                        return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // ignore, delay 후 재시도
                }

                try
                {
                    await Task.Delay(_options.Common.ReconnectDelayMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private void CleanupTransport()
        {
            // Pipe 정리
            try { _reader?.Complete(); } catch { /* ignore */ }
            try { _writer?.Complete(); } catch { /* ignore */ }

            _reader = null;
            _writer = null;

            // TcpClient 정리
            if (_tcpClient != null)
            {
                SafeDispose(_tcpClient);
                _tcpClient = null;
            }
        }

        private static void SafeDispose(TcpClient? tcpClient)
        {
            try { tcpClient?.Dispose(); } catch { /* ignore */ }
        }

        private static string NormalizeLoopback(string hostOrIp)
        {
            if (string.Equals(hostOrIp, "localhost", StringComparison.OrdinalIgnoreCase))
                return "127.0.0.1";
            return hostOrIp;
        }

        private static void ConfigureTcpClient(TcpClient tcpClient, TcpConnectionOptions opt)
        {
            if (tcpClient.Client == null) return;

            tcpClient.NoDelay = opt.NoDelay;
            tcpClient.ReceiveBufferSize = opt.SocketBufferSize;
            tcpClient.SendBufferSize = opt.SocketBufferSize;
            tcpClient.LingerState = new LingerOption(opt.LingerEnabled, opt.LingerTimeSec);

            var socket = tcpClient.Client;

            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, opt.ReuseAddress);
            }
            catch { /* ignore */ }

            try
            {
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, opt.NoDelay);
            }
            catch { /* ignore */ }

            try
            {
                socket.ReceiveTimeout = opt.Common.ReceiveTimeoutMs;
                socket.SendTimeout = opt.Common.SendTimeoutMs;
            }
            catch { /* ignore */ }

            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, opt.SocketBufferSize);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, opt.SocketBufferSize);
            }
            catch { /* ignore */ }

            if (opt.KeepAlive.Enabled)
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }
                catch { /* ignore */ }

                ConfigureKeepAlive(socket, opt.KeepAlive);
            }
        }

        private static void ConfigureKeepAlive(Socket socket, TcpKeepAliveOptions ka)
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            catch
            {
                // ignore (일부 환경에서 실패해도 치명적 아님)
            }

            // Windows 전용 세부 설정
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, Math.Max(1, ka.TimeMs / 1000));
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, Math.Max(1, ka.IntervalMs / 1000));
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, Math.Max(1, ka.RetryCount));
            }
            catch
            {
                // Legacy Windows fallback (IOControl, ms 단위)
                byte[] keepAliveValues = new byte[12];

                try
                {
                    BitConverter.GetBytes(1).CopyTo(keepAliveValues.AsSpan(0, 4));          // on/off
                    BitConverter.GetBytes(ka.TimeMs).CopyTo(keepAliveValues.AsSpan(4, 4)); // time (ms)
                    BitConverter.GetBytes(ka.IntervalMs).CopyTo(keepAliveValues.AsSpan(8, 4)); // interval (ms)

                    socket.IOControl(IOControlCode.KeepAliveValues, [.. keepAliveValues], null);
                }
                catch
                {
                    // ignore
                }
            }
        }
        #endregion
    }
}
