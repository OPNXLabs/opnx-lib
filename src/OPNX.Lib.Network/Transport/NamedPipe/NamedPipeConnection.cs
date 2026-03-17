using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Network.Abstractions;
using OPNX.Lib.Network.Abstractions.Events;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Net;

namespace OPNX.Lib.Network.Transport.NamedPipe
{
    public class NamedPipeConnection(NamedPipeConnectionOptions? options = null) : DisposableBase, IConnection
    {
        #region Fields
        // 상태 전이(Connect/Disconnect/Dispose)를 "한 번에 하나만" 수행하기 위한 게이트
        private readonly SemaphoreSlim _gate = new(1, 1);

        private NamedPipeClientStream? _pipeClient;

        private PipeReader? _reader;
        private PipeWriter? _writer;

        private NamedPipeEndPoint? _nPipeEndPoint;

        private readonly NamedPipeConnectionOptions _options = options ?? NamedPipeConnectionOptions.Default;
        #endregion

        #region Properties
        public bool IsConnected => _pipeClient?.IsConnected ?? false;
        public Guid SessionID { get; } = Guid.NewGuid();

        // 요청대로 public 유지 (파생 클래스에서 Send/ReadLoop 구현 가능)
        public PipeReader? Reader => _reader;
        public PipeWriter? Writer => _writer;
        #endregion

        #region Events 
        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        #endregion

        #region Public Methods
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

        public async Task<bool> ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
        {
            if (IsDisposed) return false;

            if (endPoint is not NamedPipeEndPoint nPipeEndPoint)
                throw new ArgumentException("IPEndPoint required", nameof(endPoint));

            if (IsConnected)
                return true;

            EventHandler<ConnectedEventArgs>? connectedHandlers = null;
            ConnectedEventArgs? connectedArgs = null;
            bool connected = false;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsDisposed) return false;
                if (IsConnected) return true;

                _nPipeEndPoint = nPipeEndPoint;
                string pipeName = _nPipeEndPoint.PipeName;

                NamedPipeClientStream? newPipeClient = null;

                try
                {
                    newPipeClient = new NamedPipeClientStream(
                        serverName: ".",
                        pipeName: pipeName,
                        direction: PipeDirection.InOut,
                        options: System.IO.Pipes.PipeOptions.Asynchronous);

                    await newPipeClient.ConnectAsync(_options.Common.ConnectTimeoutMs, cancellationToken).ConfigureAwait(false);

                    if (!newPipeClient.IsConnected)
                        return false;

                    // 연결 성공 → 현재 인스턴스에 바인딩(소유권 이전)
                    await ReplacePipeLockedAsync(newPipeClient).ConfigureAwait(false);
                    newPipeClient = null;

                    connectedHandlers = Connected;
                    connectedArgs = new ConnectedEventArgs(SessionID);
                    connected = true;
                }
                catch (OperationCanceledException)
                {
                    connected = false;
                }
                catch (TimeoutException)
                {
                    connected = false;
                }
                catch (IOException ioEx)
                {
                    LogManager.Error($"I/O error occurred while connecting to pipe '{pipeName}': {ioEx.Message}");
                    connected = false;
                }
                catch (Exception ex)
                {
                    LogManager.Error($"Unexpected error while connecting to pipe '{pipeName}': {ex}");
                    connected = false;
                }
                finally
                {
                    if (newPipeClient is not null)
                    {
                        try { await newPipeClient.DisposeAsync().ConfigureAwait(false); }
                        catch { /* ignore */ }
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            if (connected && connectedHandlers is not null && connectedArgs is not null)
            {
                try { connectedHandlers.Invoke(this, connectedArgs); }
                catch (Exception ex) { LogManager.Error(ex); }
            }

            return connected;
        }

        public void Disconnect(DisconnectReason reason = DisconnectReason.Requested)
        {
            if (IsDisposed)
                return;

            try
            {
                DisconnectAsync(reason, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }

        public async Task DisconnectAsync(DisconnectReason reason, CancellationToken cancellationToken)
        {
            EventHandler<DisconnectedEventArgs>? disconnectedHandlers = null;
            DisconnectedEventArgs? disconnectedArgs = null;
            bool shouldRaise = false;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsDisposed)
                    return;

                if (_pipeClient is null && _reader is null && _writer is null)
                    return;


                await CleanupLockedAsync().ConfigureAwait(false);

                disconnectedHandlers = Disconnected;
                disconnectedArgs = new DisconnectedEventArgs(SessionID, reason);
                shouldRaise = true;
            }
            finally
            {
                _gate.Release();
            }

            if (shouldRaise && disconnectedHandlers is not null && disconnectedArgs is not null)
            {
                try { disconnectedHandlers.Invoke(this, disconnectedArgs); }
                catch (Exception ex) { LogManager.Error(ex); }
            }
        }
        #endregion

        #region Private / Protected Methods
        private async ValueTask ReplacePipeLockedAsync(NamedPipeClientStream newClient)
        {
            await CleanupLockedAsync().ConfigureAwait(false);

            _pipeClient = newClient;

            // leaveOpen:true로 stream 소유권은 _pipeClient 유지
            _reader = PipeReader.Create(_pipeClient, new StreamPipeReaderOptions(leaveOpen: true));
            _writer = PipeWriter.Create(_pipeClient, new StreamPipeWriterOptions(leaveOpen: true));
        }

        private async ValueTask CleanupLockedAsync()
        {
            // 파이프라인 종료
            if (_writer is not null)
            {
                try { await _writer.CompleteAsync().ConfigureAwait(false); }
                catch (Exception ex) { LogManager.Error(ex); }
                _writer = null;
            }

            if (_reader is not null)
            {
                try { await _reader.CompleteAsync().ConfigureAwait(false); }
                catch (Exception ex) { LogManager.Error(ex); }
                _reader = null;
            }

            // 스트림 종료
            if (_pipeClient is not null)
            {
                try { await _pipeClient.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { LogManager.Error(ex); }
                _pipeClient = null;
            }
        }


        protected override void OnDispose()
        {
            try
            {
                OnDisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }

        protected override async ValueTask OnDisposeAsync()
        {
            if (IsDisposed)
                return;

            try
            {
                await DisconnectAsync(DisconnectReason.Requested, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
            finally
            {
                await base.OnDisposeAsync().ConfigureAwait(false);
            }
        }
        #endregion
    }
}
