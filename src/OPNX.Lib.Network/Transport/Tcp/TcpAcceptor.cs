using OPNX.Lib.Common.Logging;
using OPNX.Lib.Network.Abstractions.Events;
using System.Net;
using System.Net.Sockets;

namespace OPNX.Lib.Network.Transport.Tcp
{
    public class TcpAcceptor(string address, int port) : IDisposable
    {
        #region Fields        
        private int _started = 0;
        private readonly string _address = address;
        private readonly int _port = port;
        private readonly TcpListener _listener = new(string.IsNullOrEmpty(address) ? IPAddress.Any : IPAddress.Parse(address), port);
        private readonly CancellationTokenSource _listenerCancelTokenSource = new();
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
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                return;

            try
            {
                _listener.Start();
                _listenTask = Task.Run(() => ListenAsync(_listenerCancelTokenSource.Token));
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                Interlocked.Exchange(ref _started, 0);
            }
        }

        public void Stop()
        {
            if (Interlocked.CompareExchange(ref _started, 0, 1) != 1)
                return;

            _listenerCancelTokenSource.Cancel();

            try
            {
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }

            // ListenAsync가 완료될 때까지 기다립니다.
            if (_listenTask != null)
            {
                try
                {
                    _listenTask.Wait(); // 비동기 listen 작업을 동기적으로 종료 대기
                }
                catch (Exception ex) when (ex is OperationCanceledException ||
                                           ex.InnerException is OperationCanceledException ||
                                           ex.InnerException is ObjectDisposedException)
                {
                    // 정상적인 취소 또는 종료 → 무시
                }
                catch (Exception ex)
                {
                    LogManager.Error($"listenTask 처리 중 예외 발생. {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            Stop();

            _listenerCancelTokenSource.Dispose();

            GC.SuppressFinalize(this);
        }
        #endregion

        #region Private / Protected Methods        
        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            if (_listener?.Server?.IsBound != true)
            {
                LogManager.Warning("TCP listener is not properly initialized or bound");
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
                                LogManager.Error(ex);
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
                            //        LogManager.Error($"ClientConnected event handler error: {ex}");
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
                        LogManager.Warning($"A socket error occurred while accepting the client. Error={sockEx.Message}.");

                        // 심각한 소켓 오류 시 잠시 대기 후 재시도
                        if (IsUnrecoverableSocketError(sockEx.SocketErrorCode))
                        {
                            LogManager.Error($"Unrecoverable socket error: {sockEx.SocketErrorCode}");
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
                        LogManager.Error($"An unexpected error occurred in the listener. Error={ex}.");

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
