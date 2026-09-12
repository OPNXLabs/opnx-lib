using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Network.Abstractions.Events;
using System.Net;
using System.Net.Sockets;

namespace OPNX.Lib.Network.Transport.Tcp
{
    public class TcpAcceptor(string address, int port, ILogger? logger = null) : IDisposable
    {
        private readonly ILogger _logger = logger ?? NullLogger.Instance;

        #region Fields        
        private readonly object _lifecycleLock = new();
        private int _started;
        private readonly string _address = address;
        private readonly int _port = port;
        private readonly TcpListener _listener = new(string.IsNullOrEmpty(address) ? IPAddress.Any : IPAddress.Parse(address), port);
        private CancellationTokenSource? _listenerCancelTokenSource;
        private Task? _listenTask;
        #endregion        

        #region Properties
        public string Address => _address;

        public int Port => _port;
        #endregion

        #region Events
        public event EventHandler<ClientAcceptedEventArgs>? ClientAccepted;
        #endregion

        #region Public Methods
        public void Start()
        {
            lock (_lifecycleLock)
            {
                if (_started != 0)
                    return;

                var cancellation = new CancellationTokenSource();
                try
                {
                    _listener.Start();
                    _listenerCancelTokenSource = cancellation;
                    _listenTask = Task.Run(() => ListenAsync(cancellation.Token));
                    _started = 1;
                }
                catch (Exception ex)
                {
                    cancellation.Dispose();
                    _logger.LogError(ex, "{Message}", ex.Message);
                }
            }
        }

        public void Stop()
        {
            lock (_lifecycleLock)
            {
                if (_started == 0)
                    return;

                _started = 0;
                CancellationTokenSource? cancellation = _listenerCancelTokenSource;
                Task? listenTask = _listenTask;

                cancellation?.Cancel();

                try
                {
                    _listener.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Message}", ex.Message);
                }

                if (listenTask != null)
                {
                    try
                    {
                        listenTask.Wait();
                    }
                    catch (Exception ex) when (ex is OperationCanceledException ||
                                               ex.InnerException is OperationCanceledException ||
                                               ex.InnerException is ObjectDisposedException)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "listenTask 처리 중 예외 발생. {Message}", ex.Message);
                    }
                }

                _listenTask = null;
                _listenerCancelTokenSource = null;
                cancellation?.Dispose();
            }
        }

        public void Dispose()
        {
            Stop();

            GC.SuppressFinalize(this);
        }
        #endregion

        #region Private / Protected Methods        
        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            if (_listener?.Server?.IsBound != true)
            {
                _logger.LogWarning("TCP listener is not properly initialized or bound");
                return;
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        // CancellationToken을 지원하는 AcceptTcpClientAsync 사용
                        client = await AcceptTcpClientWithCancellationAsync(cancellationToken).ConfigureAwait(false);

                        if (client?.Connected == true)
                        {
                            try
                            {
                                ClientAccepted?.Invoke(this, new(client));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "{Message}", ex.Message);
                                try { client?.Close(); } catch { }
                            }

                            //// 이벤트 비동기 처리로 메인 루프 블로킹 방지
                            //_ = Task.Run(() =>
                            //{
                            //    try
                            //    {
                            //        ClientConnected?.Invoke(this, new ClientConnectedEventArgs(client));
                            //    }
                            //    catch (Exception ex)
                            //    {
                            //        _logger.LogError(ex, "ClientConnected event handler error: {Message}", ex.Message);
                            //        // 이벤트 처리 실패 시 클라이언트 정리
                            //        try { client?.Close(); } catch { }
                            //    }
                            //}, cancellationToken);
                        }
                        else
                        {
                            // 연결되지 않은 클라이언트 정리
                            client?.Close();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 취소 요청 시 루프 종료
                        break;
                    }
                    catch (SocketException sockEx)
                    {
                        // 네트워크 관련 오류
                        _logger.LogWarning(sockEx, "A socket error occurred while accepting the client. Error={Message}.", sockEx.Message);

                        // 심각한 소켓 오류 시 잠시 대기 후 재시도
                        if (IsUnrecoverableSocketError(sockEx.SocketErrorCode))
                        {
                            _logger.LogError(sockEx, "Unrecoverable socket error: {SocketErrorCode}", sockEx.SocketErrorCode);
                            break;
                        }

                        // 일시적 오류는 잠시 대기 후 재시도
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        // 리스너가 종료됨
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An unexpected error occurred in the listener. Error={Message}.", ex.Message);

                        // 예상치 못한 오류 시 잠시 대기 후 재시도
                        try
                        {
                            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {

            }
        }

        //private async Task<TcpClient> AcceptTcpClientWithCancellationAsync(CancellationToken cancellationToken)
        //{
        //    // .NET 5.0에서는 AcceptTcpClientAsync가 CancellationToken을 직접 지원하지 않으므로
        //    // Task.Run과 cancellationToken.Register를 사용하여 구현
        //    var tcs = new TaskCompletionSource<TcpClient>();

        //    using (cancellationToken.Register(() => tcs.TrySetCanceled()))
        //    {
        //        var acceptTask = listener.AcceptTcpClientAsync();
        //        var completedTask = await Task.WhenAny(acceptTask, tcs.Task).ConfigureAwait(false);

        //        if (completedTask == tcs.Task)
        //        {
        //            // 취소됨
        //            throw new OperationCanceledException();
        //        }

        //        return await acceptTask.ConfigureAwait(false);
        //    }
        //}

        private async Task<TcpClient> AcceptTcpClientWithCancellationAsync(CancellationToken cancellationToken)
        {
            return await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        }

        private static bool IsUnrecoverableSocketError(SocketError errorCode)
        {
            return errorCode switch
            {
                SocketError.AddressNotAvailable => true,
                SocketError.AddressAlreadyInUse => true,
                SocketError.AccessDenied => true,
                SocketError.InvalidArgument => true,
                _ => false
            };
        }
        #endregion
    }
}

