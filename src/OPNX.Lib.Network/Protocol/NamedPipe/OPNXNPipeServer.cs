using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Common.Buffers;
using OPNX.Lib.Common.Compression;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Serialization;
using OPNX.Lib.Network.Abstractions;
using OPNX.Lib.Network.Abstractions.Events;
using OPNX.Lib.Network.Protocol.Abstractions;
using OPNX.Lib.Network.Protocol.Framing;
using OPNX.Lib.Network.Transport.NamedPipe;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace OPNX.Lib.Network.Protocol.NamedPipe
{
    public class OPNXNPipeServer : DisposableObject
    {
        #region Fields
        private readonly NamedPipeAcceptor _npAcceptor;

        private static readonly ZstdCompressionProvider _zstd = new();

        private Channel<Packet>? _outboundChannel;
        private Channel<Packet>? _inboundChannel;
        private CancellationTokenSource? _workCts = new();

        private Task? _readLoopTask = null;
        private Task? _sendLoopTask = null;
        private Task? _receiveLoopTask = null;

        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        private int _isStarted;
        private CancellationTokenSource? _acceptLoopCts;
        private Task? _acceptLoopTask;
        private TaskCompletionSource<bool>? _connectionClosedSignal;

        private readonly ProtocolOptions _options;
        private readonly ILogger _logger;
        #endregion

        #region Constructors
        public OPNXNPipeServer(NamedPipeEndPoint nPipeEndpoint, ProtocolOptions? options = null, ILogger? logger = null)
        {
            _logger = logger ?? NullLogger.Instance;

            _npAcceptor = new NamedPipeAcceptor(nPipeEndpoint, logger: logger);
            _npAcceptor.Connected += NPAcceptor_Connected;
            _npAcceptor.Disconnected += NPAcceptor_Disconnected;

            _options = options ?? ProtocolOptions.Default;
        }
        #endregion

        #region Properties
        public bool IsConnected => _npAcceptor?.IsConnected ?? false;

        public Guid SessionID => _npAcceptor?.SessionID ?? Guid.Empty;
        // 요청대로 public 유지 (파생 클래스에서 Send/ReadLoop 구현 가능)
        public PipeReader? Reader => _npAcceptor?.Reader ?? null;
        public PipeWriter? Writer => _npAcceptor?.Writer ?? null;

        protected bool IsStarted => Volatile.Read(ref _isStarted) == 1;
        #endregion

        #region Events
        public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        #endregion

        #region Public Methods
        public void Start()
        {
            _ = StartAsync();
        }


        public async Task StartAsync()
        {
            if (Interlocked.Exchange(ref _isStarted, 1) != 0)
                return;

            _acceptLoopCts = new CancellationTokenSource();
            _acceptLoopTask = RunAcceptLoopAsync(_acceptLoopCts.Token);
            await Task.CompletedTask;
        }

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

                packet = CreateOutboundPacket(header, ref owner, written);

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
                _logger.LogError(ex, "{Message}", ex.Message);
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

                packet = CreateOutboundPacket(header, ref owner, written);

                if (outbound.Writer.TryWrite(packet))
                    return true;

                packet.Dispose();
                _logger.LogError("SendData: Channel full or closed.");
                return false;
            }
            catch (Exception ex)
            {
                packet?.Dispose();
                owner?.Dispose();
                _logger.LogError(ex, "{Message}", ex.Message);
                return false;
            }
        }
        #endregion

        #region Private / Protected Methods
        private Packet CreateOutboundPacket(PacketHeader header, ref IMemoryOwner<byte>? owner, int payloadSize)
        {
            ArgumentNullException.ThrowIfNull(owner);

            if (payloadSize > _options.CompressThresholdBytes)
            {
                var span = owner.Memory.Span[..payloadSize];
                (var compressedOwner, int compressedSize) = _zstd.Compress(span);

                owner.Dispose();
                owner = null;

                var compressedHeader = CreatePayloadHeader(
                    header,
                    header.Flags | PacketFlags.Compressed,
                    compressedSize);

                return new Packet(compressedHeader, compressedOwner, compressedSize);
            }

            var payloadHeader = CreatePayloadHeader(header, header.Flags, payloadSize);
            var packet = new Packet(payloadHeader, owner, payloadSize);
            owner = null;
            return packet;
        }

        private static PacketHeader CreatePayloadHeader(PacketHeader header, PacketFlags flags, int payloadSize)
        {
            return new PacketHeader(
                flags,
                header.PacketType,
                header.PayloadType,
                checked((uint)payloadSize),
                header.Version,
                header.Reserved);
        }

        private Channel<Packet> CreateInboundChannel() => CreatePacketChannel(_options.InboundChannelCapacity);

        private Channel<Packet> CreateOutboundChannel() => CreatePacketChannel(_options.OutboundChannelCapacity);

        private Channel<Packet> CreatePacketChannel(int capacity)
        {
            if (capacity <= 0)
            {
                return Channel.CreateUnbounded<Packet>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            }

            return Channel.CreateBounded<Packet>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = _options.ChannelFullMode
            });
        }

        private async void NPAcceptor_Disconnected(object? sender, DisconnectedEventArgs e)
        {
            Task? readTask, recvTask, sendTask;
            CancellationTokenSource? cts;
            Channel<Packet>? outbound, inbound;

            await _connectionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _workCts?.Cancel();
                _connectionClosedSignal?.TrySetResult(true);

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
                _logger.LogError(ex, "{Message}", ex.Message);
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
                _logger.LogError(ex, "{Message}", ex.Message);
            }

            _connectionClosedSignal?.TrySetResult(true);
            Disconnected?.Invoke(this, new DisconnectedEventArgs(SessionID, e.Reason));
        }

        private async void NPAcceptor_Connected(object? sender, ConnectedEventArgs e)
        {
            await _connectionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _outboundChannel = CreateOutboundChannel();
                _inboundChannel = CreateInboundChannel();

                _workCts = new CancellationTokenSource();
                var token = _workCts.Token;
                _connectionClosedSignal = CreateSignal();

                _readLoopTask = ReadPacketProcessorAsync(token);
                _sendLoopTask = SendPacketProcessorAsync(token);
                _receiveLoopTask = ReceivePacketProcessorAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                return;
            }
            finally
            {
                _connectionLock.Release();
            }

            Connected?.Invoke(this, new ConnectedEventArgs(SessionID));
        }

        private async Task RunAcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && !IsDisposed)
            {
                try
                {
                    await _npAcceptor.WaitForConnectionAsync(token).ConfigureAwait(false);

                    Task waitForDisconnect = (_connectionClosedSignal ?? CreateSignal()).Task;
                    await waitForDisconnect.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"The named pipe accept loop failed. Error={ex}.");

                    try
                    {
                        await Task.Delay(250, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private static TaskCompletionSource<bool> CreateSignal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool ShouldTerminate(CancellationToken token) => token.IsCancellationRequested || IsDisposed || !IsConnected;

        private async Task ReadPacketProcessorAsync(CancellationToken cancelToken)
        {
            DisconnectReason disconnectReason = DisconnectReason.Stopped;

            var reader = Reader;
            if (reader is null)
            {
                _npAcceptor?.HandleDisconnectedAsync(disconnectReason);
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
                                packet = PacketProcessor.Process(header, payload);

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
                                _logger.LogError($"Failed to process the packet. Error={ex}.");
                                // 다음 프레임 계속
                            }
                        }

                        consumed = original.GetPosition(original.Length - remaining.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to process the buffer. Error={ex}.");
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
                _logger.LogError("Packet processing operation cancelled");
            }
            catch (IOException)
            {
                disconnectReason = DisconnectReason.Broken;
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogError($"Reader object disposed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"An unexpected error occurred in the packet processor. Error={ex}.");
            }
            finally
            {
                try
                {
                    await reader.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to complete the reader. Error={ex}.");
                }

                await _npAcceptor.HandleDisconnectedAsync(disconnectReason);
            }
        }

        private async Task SendPacketProcessorAsync(CancellationToken cancelToken)
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
                            _logger.LogWarning($"Packet ignored due to disconnection: {ex.Message}");
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
                _logger.LogError(ex, "{Message}", ex.Message);
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
                    _logger.LogError(ex, "{Message}", ex.Message);
                }
            }
        }

        private async Task ReceivePacketProcessorAsync(CancellationToken cancellationToken)
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
                            PacketReceived?.Invoke(this, new PacketReceivedEventArgs(SessionID, packet.Header, packet.Payload));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "{Message}", ex.Message);
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
                _logger.LogError(ex, "{Message}", ex.Message);
            }
        }

        protected override async void OnDispose()
        {
            await OnDisposeAsync();
        }

        protected override async ValueTask OnDisposeAsync()
        {
            try
            {
                _workCts?.Cancel();
                _connectionClosedSignal?.TrySetResult(true);
                _acceptLoopCts?.Cancel();

                if (_npAcceptor != null)
                {
                    _npAcceptor.Connected -= NPAcceptor_Connected;
                    _npAcceptor.Disconnected -= NPAcceptor_Disconnected;
                    await _npAcceptor.DisposeAsync();
                }

                _inboundChannel?.Writer.Complete();
                _outboundChannel?.Writer.Complete();

                await Task.WhenAll(_readLoopTask ?? Task.CompletedTask,
                                   _receiveLoopTask ?? Task.CompletedTask,
                                   _sendLoopTask ?? Task.CompletedTask,
                                   _acceptLoopTask ?? Task.CompletedTask
                                   ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while disposing the named pipe server. Error={ex}.");
            }
            finally
            {
                _acceptLoopCts?.Dispose();
                _workCts?.Dispose();
            }

            await base.OnDisposeAsync();
        }
        #endregion
    }
}







