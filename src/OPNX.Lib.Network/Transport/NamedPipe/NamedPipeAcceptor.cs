using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Network.Abstractions;
using OPNX.Lib.Network.Abstractions.Events;
using System.IO.Pipelines;
using System.IO.Pipes;

namespace OPNX.Lib.Network.Transport.NamedPipe
{
    public class NamedPipeAcceptor(NamedPipeEndPoint nPipeEndPoint, NamedPipeConnectionOptions? options = null, ILogger? logger = null) : DisposableObject
    {
        private readonly ILogger _logger = logger ?? NullLogger.Instance;

        #region Fields
        private readonly NamedPipeEndPoint _nPipeEndPoint = nPipeEndPoint;
        private readonly NamedPipeConnectionOptions _options = options ?? NamedPipeConnectionOptions.Default;

        private NamedPipeServerStream? _pipeServer;
        private PipeReader? _reader;
        private PipeWriter? _writer;

        // Wait/Disconnect/Dispose 상태 전이를 직렬화하기 위한 게이트
        private readonly SemaphoreSlim _gate = new(1, 1);
        #endregion

        #region Properties
        public Guid SessionID { get; } = Guid.NewGuid();
        public bool IsConnected => _pipeServer?.IsConnected ?? false;

        public PipeReader? Reader => _reader;
        public PipeWriter? Writer => _writer;

        public NamedPipeConnectionOptions Options => _options;
        #endregion

        #region Events 
        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        #endregion

        #region Public Methods
        public async Task HandleDisconnectedAsync(DisconnectReason reason)
        {
            EventHandler<DisconnectedEventArgs>? disconnectedHandlers = null;
            DisconnectedEventArgs? disconnectedArgs = null;
            bool shouldRaise = false;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsDisposed)
                    return;

                await CleanupLockedAsync().ConfigureAwait(false);

                InitializePipeResources();

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
                catch (Exception ex) { _logger.LogError(ex, "{Message}", ex.Message); }
            }
        }

        public async Task WaitForConnectionAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, GetType());

            NamedPipeServerStream? localServer = null;

            EventHandler<ConnectedEventArgs>? connectedHandlers = null;
            ConnectedEventArgs? connectedArgs = null;
            bool connected = false;

            try
            {
                await _gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    ObjectDisposedException.ThrowIf(IsDisposed, GetType());

                    InitializePipeResources();

                    if (_pipeServer!.IsConnected)
                        return;

                    localServer = _pipeServer;
                }
                finally
                {
                    _gate.Release();
                }

                await localServer!.WaitForConnectionAsync(token).ConfigureAwait(false);


                await _gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (IsDisposed)
                        return;

                    if (_pipeServer is null || !_pipeServer.IsConnected)
                        return;

                    await ResetPipelinesLockedAsync().ConfigureAwait(false);

                    _writer = PipeWriter.Create(_pipeServer, new StreamPipeWriterOptions(leaveOpen: true));
                    _reader = PipeReader.Create(_pipeServer, new StreamPipeReaderOptions(leaveOpen: true));

                    connectedHandlers = Connected;
                    connectedArgs = new ConnectedEventArgs(SessionID);
                    connected = true;
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NamedPipeAcceptor] Failed to wait for a connection. Error={Message}.", ex.Message);
                throw;
            }


            if (connected && connectedHandlers is not null && connectedArgs is not null)
            {
                try { connectedHandlers.Invoke(this, connectedArgs); }
                catch (Exception ex) { _logger.LogError(ex, "{Message}", ex.Message); }
            }
        }
        #endregion

        #region Private / Protected Methods
        private void InitializePipeResources()
        {
            if (_pipeServer is not null)
                return;

            string pipeName = _nPipeEndPoint.PipeName;

            // 1:1 설계이므로 1로 고정
            _pipeServer = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                System.IO.Pipes.PipeOptions.Asynchronous);
        }

        private async ValueTask ResetPipelinesLockedAsync()
        {
            if (_writer is not null)
            {
                try { await _writer.CompleteAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogError(ex, "{Message}", ex.Message); }
                _writer = null;
            }

            if (_reader is not null)
            {
                try { await _reader.CompleteAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogError(ex, "{Message}", ex.Message); }
                _reader = null;
            }
        }

        private async ValueTask CleanupLockedAsync()
        {
            await ResetPipelinesLockedAsync().ConfigureAwait(false);

            if (_pipeServer is not null)
            {
                try { await _pipeServer.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogError(ex, "{Message}", ex.Message); }
                _pipeServer = null;
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
                _logger.LogError(ex, "{Message}", ex.Message);
            }
        }

        protected override async ValueTask OnDisposeAsync()
        {
            if (IsDisposed)
                return;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await CleanupLockedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NamedPipeAcceptor] An error occurred during disposal. Error={Message}.", ex.Message);
            }
            finally
            {
                _gate.Release();
            }

            await base.OnDisposeAsync().ConfigureAwait(false);
        }
        #endregion
    }
}


