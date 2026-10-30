using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace OPNX.Lib.Streaming.WebRTC.DataChannel
{
    public sealed class DataChannelClientSession(WebSocket socket, Uri requestUri) : IDisposable
    {
        #region Fields
        private readonly WebSocket _socket = socket;
        private readonly Lock _sessionLock = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource _initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _socketClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task _socketCloseTask = Task.CompletedTask;
        private bool _started;
        private int _closed;
        private int _disposed;
        #endregion

        #region Properties
        public Guid ConnectionID { get; } = Guid.NewGuid();
        public Uri RequestUri { get; } = requestUri;
        public bool IsClosed => Volatile.Read(ref _closed) != 0;
        internal DataChannelPeerConnection? PeerConnection { get; set; }
        internal Task Initialization => _initialization.Task;
        internal Task SocketCloseTask => _socketCloseTask;
        #endregion

        #region Events
        public event Func<DataChannelClientSession, Task>? SocketOpened;
        public event Action<DataChannelClientSession, string>? MessageReceived;
        public event Action<DataChannelClientSession>? SocketClosed;
        public event Action<DataChannelClientSession, Exception>? SocketFailed;
        #endregion

        #region Private / Protected Methods
        private async Task OpenAsync()
        {
            try
            {
                if (!IsClosed && SocketOpened is { } handler)
                    await handler(this).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (IsClosed)
            {
            }
            catch (Exception ex)
            {
                SocketFailed?.Invoke(this, ex);
                Close();
            }
            finally
            {
                _initialization.TrySetResult();
            }
        }

        private async Task ReceiveAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                while (_socket.State is WebSocketState.Open or WebSocketState.CloseSent)
                {
                    using var message = new MemoryStream();
                    ValueWebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return;
                        if (message.Length + result.Count > 256 * 1024)
                            throw new InvalidDataException("WebRTC signaling message is too large.");
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text || IsClosed)
                        continue;
                    await Initialization.ConfigureAwait(false);
                    if (!IsClosed)
                        MessageReceived?.Invoke(this, Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task CloseSocketAsync()
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed", timeout.Token).ConfigureAwait(false);
                await _socketClosed.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _socket.Abort();
                SocketFailed?.Invoke(this, ex);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        #endregion

        #region Public Methods
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            Task opening;
            lock (_sessionLock)
            {
                if (IsClosed)
                    return;
                _started = true;
                // 초기화 완료 신호를 보장하기 위해 작업 시작 자체는 취소하지 않습니다.
                opening = Task.Run(OpenAsync, CancellationToken.None);
            }
            try
            {
                await ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || IsClosed)
            {
            }
            catch (Exception ex)
            {
                if (!IsClosed)
                    SocketFailed?.Invoke(this, ex);
            }
            finally
            {
                _socketClosed.TrySetResult();
                Close();
                await opening.ConfigureAwait(false);
                await SocketCloseTask.ConfigureAwait(false);
            }
        }

        public async Task SendAsync(string message)
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            await _sendLock.WaitAsync(_cancellation.Token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(IsClosed, this);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _socket.SendAsync(Encoding.UTF8.GetBytes(message).AsMemory(), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Close()
        {
            lock (_sessionLock)
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0)
                    return;
                _cancellation.Cancel();
                if (!_started)
                {
                    _initialization.TrySetResult();
                    _socketClosed.TrySetResult();
                }
                _socketCloseTask = CloseSocketAsync();
            }
            SocketClosed?.Invoke(this);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            {
                Close();
            }
            finally
            {
                PeerConnection?.Dispose();
                _socket.Dispose();
                _cancellation.Dispose();
            }
        }
        #endregion
    }
}
