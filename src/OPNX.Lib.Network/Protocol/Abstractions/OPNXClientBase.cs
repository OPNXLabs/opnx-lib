using OPNX.Lib.Common.Buffers;
using OPNX.Lib.Common.Compression;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Common.Serialization;
using OPNX.Lib.Network.Abstractions;
using OPNX.Lib.Network.Abstractions.Events;
using OPNX.Lib.Network.Protocol.Framing;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Threading.Channels;

namespace OPNX.Lib.Network.Protocol.Abstractions
{
    public class OPNXClientBase : DisposableBase
    {
        #region Fields
        protected readonly IConnection _connection;

        private static readonly ZstdCompressionProvider _zstd = new();

        private Channel<Packet>? _outboundChannel;
        private Channel<Packet>? _inboundChannel;
        private CancellationTokenSource? _workCts;

        private Task? _readLoopTask = null;
        private Task? _sendLoopTask = null;
        private Task? _receiveLoopTask = null;

        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        private readonly ProtocolOptions _options;
        #endregion

        #region Constructors
        public OPNXClientBase(IConnection connection, ProtocolOptions? options = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _connection.Connected += Connection_Connected;
            _connection.Disconnected += Connection_Disconnected;

            _options = options ?? ProtocolOptions.Default;
        }
        #endregion

        #region Events
        public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        #endregion

        #region Properties
        public bool IsConnected => _connection?.IsConnected ?? false;
        public Guid SessionID { get; } = Guid.NewGuid();
        public PipeReader? Reader => _connection?.Reader;
        public PipeWriter? Writer => _connection?.Writer;
        #endregion

        #region Public Methods
        public async ValueTask<bool> SendDataAsync<T>(PacketHeader header, T data, CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
                return false;

            var outbound = _outboundChannel;
            if (outbound is null || !IsConnected)
                return false;

            Packet? packet = null;
            IMemoryOwner<byte>? owner = null;
            int written;

            try
            {
                // 1) payload 생성 (owner 기반)
                if (data is string s)
                {
                    (owner, written) = Utf8Encode.EncodePooled(s);
                }
                else
                {
                    (owner, written) = JsonSerialize.SerializeToUtf8Pooled(data);
                }

                if (written <= 0)
                {
                    owner?.Dispose();
                    return true;
                }

                if (written > _options.CompressThresholdBytes)
                {
                    var span = owner!.Memory.Span[..written];
                    (var compOwner, int compWritten) = _zstd.Compress(span);

                    owner.Dispose();
                    owner = null;

                    var newHeader = new PacketHeader(
                        header.Flags | PacketFlags.Compressed,
                        header.PacketType,
                        header.PayloadType,
                        (uint)compWritten,
                        header.Version,
                        header.Reserved);

                    packet = new Packet(newHeader, compOwner, compWritten);
                }
                else
                {
                    packet = new Packet(header, owner!, written);
                    owner = null; // 소유권 Packet으로 이동
                }

                if (outbound.Writer.TryWrite(packet))
                    return true;

                await outbound.Writer.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (ChannelClosedException)
            {
                packet?.Dispose();
                owner?.Dispose();
                return false;
            }
            catch (OperationCanceledException)
            {
                packet?.Dispose();
                owner?.Dispose();
                return false;
            }
            catch (Exception ex)
            {
                packet?.Dispose();
                owner?.Dispose();
                LogManager.Error(ex);
                return false;
            }
        }
        public bool SendData<T>(PacketHeader header, T data)
        {
            if (IsDisposed)
                return false;

            var outbound = _outboundChannel;
            if (outbound is null || !IsConnected)
                return false;

            Packet? packet = null;
            IMemoryOwner<byte>? owner = null;
            int written;

            try
            {
                if (data is string s)
                {
                    (owner, written) = Utf8Encode.EncodePooled(s);
                }
                else
                {
                    (owner, written) = JsonSerialize.SerializeToUtf8Pooled(data);
                }

                if (written <= 0)
                {
                    owner?.Dispose();
                    return true;
                }

                if (written > _options.CompressThresholdBytes)
                {
                    var span = owner!.Memory.Span[..written];
                    (var compOwner, int compWritten) = _zstd.Compress(span);

                    owner.Dispose();
                    owner = null;

                    var newHeader = new PacketHeader(
                        header.Flags | PacketFlags.Compressed,
                        header.PacketType,
                        header.PayloadType,
                        (uint)compWritten,
                        header.Version,
                        header.Reserved);

                    packet = new Packet(newHeader, compOwner, compWritten);
                }
                else
                {
                    packet = new Packet(header, owner!, written);
                    owner = null;
                }

                if (outbound.Writer.TryWrite(packet))
                    return true;

                packet.Dispose();
                LogManager.Error("SendData: Channel full or closed.");
                return false;
            }
            catch (Exception ex)
            {
                packet?.Dispose();
                owner?.Dispose();
                LogManager.Error(ex);
                return false;
            }
        }

        public void Connect(EndPoint endPoint)
        {
            if (IsDisposed)
                return;

            _connection?.Connect(endPoint);
        }

        public async Task ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
        {
            if (IsDisposed || _connection is null)
                return;

            await _connection.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        }

        public void Disconnect(DisconnectReason reason = DisconnectReason.Requested)
        {
            if (IsDisposed)
                return;

            _connection?.Disconnect(reason);
        }

        public async Task DisconnectAsync(DisconnectReason reason = DisconnectReason.Requested, CancellationToken cancellationToken = default)
        {
            if (IsDisposed || _connection is null)
                return;

            await _connection.DisconnectAsync(reason, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Private / Protected Methods
        protected override async void OnDispose()
        {
            await OnDisposeAsync();
        }

        protected override async ValueTask OnDisposeAsync()
        {
            try
            {
                _workCts?.Cancel();

                if (_connection != null)
                {
                    _connection.Connected -= Connection_Connected;
                    _connection.Disconnected -= Connection_Disconnected;
                    await _connection.DisposeAsync();
                }

                _inboundChannel?.Writer.Complete();
                _outboundChannel?.Writer.Complete();

                await Task.WhenAll(_readLoopTask ?? Task.CompletedTask,
                                   _receiveLoopTask ?? Task.CompletedTask,
                                   _sendLoopTask ?? Task.CompletedTask
                                   ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogManager.Error("Error during disposal: " + ex);
            }
            finally
            {
                _workCts?.Dispose();
            }

            await base.OnDisposeAsync();
        }

        private static Packet ProcessPacket(PacketHeader header, ReadOnlySequence<byte> payload)
        {
            try
            {
                if (header.IsCompressed)
                {
                    //var (packetData, packetDataSize) = await Task.Run(() =>
                    //_zstd.Decompress(payload), cencelToken).ConfigureAwait(false);
                    var (owner, size) = _zstd.Decompress(payload);
                    return new Packet(header, owner, size);
                }
                else
                {
                    if (payload.IsSingleSegment)
                        return new Packet(header, payload.First);

                    int size2 = checked((int)payload.Length);
                    var owner2 = MemoryPool<byte>.Shared.Rent(size2);
                    payload.CopyTo(owner2.Memory.Span);
                    return new Packet(header, owner2, size2);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Error processing packet data: {ex}");
                throw;
            }
        }

        protected virtual bool ShouldTerminate(CancellationToken token) => token.IsCancellationRequested || IsDisposed || !IsConnected;

        private async void Connection_Disconnected(object? sender, DisconnectedEventArgs e)
        {
            Task? readTask, recvTask, sendTask;
            CancellationTokenSource? cts;
            Channel<Packet>? outbound, inbound;

            await _connectionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _workCts?.Cancel();

                // 스냅샷
                readTask = _readLoopTask;
                recvTask = _receiveLoopTask;
                sendTask = _sendLoopTask;
                cts = _workCts;
                outbound = _outboundChannel;
                inbound = _inboundChannel;

                // 참조 끊기
                _workCts = null;
                _readLoopTask = null;
                _receiveLoopTask = null;
                _sendLoopTask = null;
                _outboundChannel = null;
                _inboundChannel = null;
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                // 스냅샷 변수가 null일 수 있으니 아래 정리에서 방어됨
                readTask = recvTask = sendTask = null;
                cts = null;
                outbound = inbound = null;
            }
            finally
            {
                _connectionLock.Release();
            }

            // lock 밖에서 종료 대기 + 드레인 + dispose
            try
            {
                await Task.WhenAll(
                    readTask ?? Task.CompletedTask,
                    recvTask ?? Task.CompletedTask,
                    sendTask ?? Task.CompletedTask
                ).ConfigureAwait(false);

                if (outbound != null)
                    while (outbound.Reader.TryRead(out var p)) p.Dispose();

                if (inbound != null)
                    while (inbound.Reader.TryRead(out var p)) p.Dispose();

                cts?.Dispose();
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }

            Disconnected?.Invoke(this, new DisconnectedEventArgs(SessionID, e.Reason));
        }

        private async void Connection_Connected(object? sender, ConnectedEventArgs e)
        {
            await _connectionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _outboundChannel = Channel.CreateUnbounded<Packet>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });

                _inboundChannel = Channel.CreateUnbounded<Packet>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });

                _workCts = new CancellationTokenSource();
                var token = _workCts.Token;

                _readLoopTask = ReadPacketProcessorAsync(token);
                _sendLoopTask = SendPacketProcessorAsync(token);
                _receiveLoopTask = ReceivePacketProcessorAsync(token);
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return;
            }
            finally
            {
                _connectionLock.Release();
            }

            Connected?.Invoke(this, new ConnectedEventArgs(SessionID));
        }

        protected virtual async Task ReadPacketProcessorAsync(CancellationToken cancelToken)
        {
            DisconnectReason disconnectReason = DisconnectReason.Stopped;

            var reader = Reader;
            if (reader is null)
            {
                Disconnect(disconnectReason);
                return;
            }

            try
            {
                while (!ShouldTerminate(cancelToken))
                {
                    ReadResult result;
                    try
                    {
                        result = await reader.ReadAsync(cancelToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    ReadOnlySequence<byte> buffer = result.Buffer;
                    SequencePosition consumed = buffer.Start;

                    try
                    {
                        if (result.IsCompleted && buffer.IsEmpty)
                            break;

                        var original = result.Buffer;
                        var remaining = original;

                        var inbound = _inboundChannel;
                        if (inbound is null)
                            break;

                        while (PacketFramer.TryReadFrame(ref remaining, out var header, out var payload))
                        {
                            Packet? packet = null;
                            try
                            {
                                packet = ProcessPacket(header, payload);

                                if (inbound.Writer.TryWrite(packet))
                                {
                                    packet = null; // 소유권 채널로 이동
                                    continue;
                                }

                                await inbound.Writer.WriteAsync(packet, cancelToken).ConfigureAwait(false);
                                packet = null; // 소유권 채널로 이동
                            }
                            catch (OperationCanceledException)
                            {
                                packet?.Dispose();
                                throw;
                            }
                            catch (ChannelClosedException)
                            {
                                packet?.Dispose();
                                // 채널이 닫힌 상태면 더 처리할 의미가 없음
                                break;
                            }
                            catch (Exception ex)
                            {
                                packet?.Dispose();
                                LogManager.Error($"Error processing packet: {ex}");
                                // 다음 프레임 계속
                            }
                        }

                        consumed = original.GetPosition(original.Length - remaining.Length);
                    }
                    catch (Exception ex)
                    {
                        LogManager.Error($"Error processing buffer: {ex}");
                    }
                    finally
                    {
                        reader.AdvanceTo(consumed, buffer.End);
                    }

                    if (result.IsCompleted)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                LogManager.Error("Packet processing operation cancelled");
            }
            catch (IOException)
            {
                disconnectReason = DisconnectReason.Broken;
            }
            catch (ObjectDisposedException ex)
            {
                LogManager.Error($"Reader object disposed: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogManager.Error($"Unexpected error in packet processor: {ex}");
            }
            finally
            {
                try
                {
                    await reader.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogManager.Error($"Error completing reader: {ex}");
                }

                Disconnect(disconnectReason);
            }
        }

        protected virtual async Task SendPacketProcessorAsync(CancellationToken cancelToken)
        {
            var outbound = _outboundChannel;
            if (outbound is null)
                return;

            try
            {
                await foreach (var packet in outbound.Reader.ReadAllAsync(cancelToken))
                {
                    if (IsDisposed || !IsConnected)
                    {
                        try
                        {
                            var inbound = _inboundChannel;
                            if (inbound != null)
                            {
                                var header = (PacketHeader)packet.Header;
                                var packetType = header.PacketType == PacketType.Request ? PacketType.Response : header.PacketType;
                                var newHeader = new PacketHeader(header.Flags, packetType, header.PayloadType, header.PayloadLength);

                                Packet? newPacket = null;
                                try
                                {
                                    newPacket = new Packet(newHeader, packet.Payload);

                                    if (inbound.Writer.TryWrite(newPacket))
                                    {
                                        newPacket = null; // 소유권 이동
                                    }
                                    else
                                    {
                                        await inbound.Writer.WriteAsync(newPacket, cancelToken).ConfigureAwait(false);
                                        newPacket = null; // 소유권 이동
                                    }
                                }
                                finally
                                {
                                    newPacket?.Dispose(); // enqueue 실패시에만 dispose
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogManager.Warning($"Packet ignored due to disconnection: {ex.Message}");
                        }
                        finally
                        {
                            packet.Dispose();
                        }
                        break;
                    }

                    var writer = Writer;
                    if (writer is null)
                    {
                        packet.Dispose();
                        break;
                    }

                    using (packet)
                    {
                        packet.WriteTo(writer);
                    }

                    FlushResult result = await writer.FlushAsync(cancelToken).ConfigureAwait(false);
                    if (result.IsCanceled)
                        throw new OperationCanceledException("FlushAsync was canceled.");
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                // 다른 예외 처리
                LogManager.Error(ex);
            }
            finally
            {
                try
                {
                    var writer = Writer;
                    if (writer != null)
                        await writer.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogManager.Error(ex);
                }
            }
        }

        protected virtual async Task ReceivePacketProcessorAsync(CancellationToken cancellationToken)
        {
            var inbound = _inboundChannel;
            if (inbound is null)
                return;

            try
            {
                await foreach (var packet in inbound.Reader.ReadAllAsync(cancellationToken))
                {
                    if (IsDisposed || !IsConnected)
                    {
                        packet.Dispose();
                        break;
                    }

                    using (packet)
                    {
                        try
                        {
                            PacketReceived?.Invoke(this, new PacketReceivedEventArgs(_connection.SessionID, packet.Header, packet.Payload));
                        }
                        catch (Exception ex)
                        {
                            LogManager.Error(ex);
                        }
                    }
                }

                //while (!ShouldTerminate(cancellationToken))
                //{
                //    //if (this.receivePackets.IsEmpty)
                //    //{
                //    //    await Task.Run(() => receivePacketWaitEventHandler.Wait(cancellationToken), cancellationToken);
                //    //    //await Task.Delay(10); // 임의의 시간 지연 후 다시 시도
                //    //    //continue;
                //    //}

                //    while (receivePackets.TryDequeue(out SimplePacket packet))
                //    {
                //        OnPacketReceived(new SimplePacketReceivedEventArgs(clientConnection.SessionID, packet));
                //    }

                //    await Task.Run(() => receivePacketWaitEventHandler.Wait(cancellationToken), cancellationToken);
                //    receivePacketWaitEventHandler.Reset();
                //}
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // 다른 예외 처리
                LogManager.Error(ex);
            }
        }
        #endregion
    }
}
