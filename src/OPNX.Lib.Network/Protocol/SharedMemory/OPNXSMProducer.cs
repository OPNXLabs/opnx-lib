using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Common.Buffers;
using OPNX.Lib.Common.Compression;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Serialization;
using OPNX.Lib.Network.Protocol.Abstractions;
using OPNX.Lib.Network.Protocol.Framing;
using OPNX.Lib.Network.Transport.SharedMemory;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace OPNX.Lib.Network.Protocol.SharedMemory
{
    public class OPNXSMProducer : DisposableObject
    {
        #region Fields
        private readonly SharedMemoryEndPoint _endPoint;

        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;

        private readonly Mutex _dataMutex;
        private readonly EventWaitHandle _producerEvent;
        private readonly EventWaitHandle _consumerEvent;

        private static readonly ZstdCompressionProvider _zstd = new();
        private readonly ILogger _logger;

        private readonly CancellationTokenSource _sendDataCTS = new();
        private readonly Channel<Packet> _outboundChannel;

        private readonly Task _sendProcessTask;
        private int _completionRequested;

        private readonly ProtocolOptions _options;
        #endregion

        #region Constructors
        public OPNXSMProducer(SharedMemoryEndPoint endPoint, ProtocolOptions? options = null, ILogger? logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
            ArgumentNullException.ThrowIfNull(endPoint, nameof(endPoint));

            _options = options ?? ProtocolOptions.Default;
            _endPoint = endPoint;
            ValidateEndPoint(_endPoint);
            _outboundChannel = Channel.CreateBounded<Packet>(
                new BoundedChannelOptions(Math.Max(1, _options.OutboundChannelCapacity))
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                });

            string mapName = _endPoint.MapName;
            long bufferSize = _endPoint.BufferCapacity;
            SharedMemoryLayout layout = _endPoint.Layout;


            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _mmf = MemoryMappedFile.CreateOrOpen(
                    mapName,
                    layout.HeaderSize + bufferSize,
                    MemoryMappedFileAccess.ReadWrite);

                _accessor = _mmf.CreateViewAccessor(0, layout.HeaderSize + bufferSize, MemoryMappedFileAccess.ReadWrite);

                _dataMutex = new Mutex(false, $"{mapName}_DATA_MUTEX");
                _producerEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"{mapName}_PRODUCER_EVENT");
                _consumerEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"{mapName}_CONSUMER_EVENT");
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
            //else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            //{
            //    string path = "/dev/shm/" + mapName;

            //    if (!File.Exists(path))
            //    {
            //        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
            //        fs.SetLength(layout.HeaderSize + bufferSize);
            //    }

            //    var fsOpen = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            //    _mmf = MemoryMappedFile.CreateFromFile(
            //        fsOpen,
            //        mapName,
            //        layout.HeaderSize + bufferSize,
            //        MemoryMappedFileAccess.ReadWrite,
            //        HandleInheritability.None,
            //        leaveOpen: false);

            //    _accessor = _mmf.CreateViewAccessor(0, layout.HeaderSize + bufferSize, MemoryMappedFileAccess.ReadWrite);

            //    _dataMutex = null;
            //    _producerEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            //    _consumerEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            //}

            _sendProcessTask = Task.Run(() => SendPacketProcessorAsync(_sendDataCTS.Token));
        }
        #endregion

        #region Properties
        private SharedMemoryLayout Layout => _endPoint.Layout;
        #endregion

        #region Public Methods
        public async ValueTask<bool> SendDataAsync<T>(PacketHeader header, T data, CancellationToken cancellationToken = default)
        {
            if (IsDisposed || Volatile.Read(ref _completionRequested) != 0)
                return false;

            var outbound = _outboundChannel;
            if (outbound is null)
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
            if (IsDisposed || Volatile.Read(ref _completionRequested) != 0)
                return false;

            var outbound = _outboundChannel;
            if (outbound is null)
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

        public async Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _completionRequested, 1) == 0)
                _outboundChannel.Writer.TryComplete();

            await _sendProcessTask.WaitAsync(cancellationToken).ConfigureAwait(false);
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

        private async Task SendPacketProcessorAsync(CancellationToken token)
        {
            byte[] hdr = new byte[PacketHeader.Size];

            try
            {
                await foreach (var packet in _outboundChannel.Reader.ReadAllAsync(token))
                {
                    if (IsDisposed || token.IsCancellationRequested)
                    {
                        packet.Dispose();
                        break;
                    }

                    packet.Header.WriteTo(hdr);

                    using (packet)
                    {
                        DateTime waitStartedUtc = default;
                        DateTime lastWarningUtc = default;
                        while (!TryWriteToSharedMemory(hdr, packet.Payload.Span))
                        {
                            if (waitStartedUtc == default)
                                waitStartedUtc = DateTime.UtcNow;

                            DateTime nowUtc = DateTime.UtcNow;
                            if (nowUtc - waitStartedUtc >= TimeSpan.FromSeconds(10) &&
                                nowUtc - lastWarningUtc >= TimeSpan.FromSeconds(10))
                            {
                                lastWarningUtc = nowUtc;
                                _logger.LogWarning(
                                    "SharedMemory full. Waiting for consumer without dropping data. MapName={MapName}, WaitSeconds={WaitSeconds:F1}.",
                                    _endPoint.MapName,
                                    (nowUtc - waitStartedUtc).TotalSeconds);
                            }

                            int signaled = WaitHandle.WaitAny([_consumerEvent, token.WaitHandle], 100);
                            if (signaled == 1 || token.IsCancellationRequested)
                                throw new OperationCanceledException(token);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on disposal
            }
            catch (Exception ex)
            {
                _logger.LogError($"SendPacketProcessorAsync: {ex}");
            }
        }


        private unsafe bool TryWriteToSharedMemory(ReadOnlySpan<byte> headerBytes, ReadOnlySpan<byte> payloadBytes)
        {
            int payloadLen = headerBytes.Length + payloadBytes.Length;
            if (payloadLen <= 0 || payloadLen > _endPoint.MaxMessageLength)
                throw new ArgumentException($"Invalid data size: {payloadLen}");

            _dataMutex.WaitOne();
            try
            {
                _accessor.Read(Layout.HeadOffset, out long head);
                _accessor.Read(Layout.TailOffset, out long tail);

                long dataBufferSize = _endPoint.BufferCapacity;

                long used = (head >= tail) ? (head - tail) : (dataBufferSize - tail + head);
                long free = dataBufferSize - used;

                int totalLength = Layout.MessageHeaderSize + payloadLen; // (len prefix) + (packet bytes)
                if (totalLength > free)
                    return false;

                long writePos = head % dataBufferSize;

                var handle = _accessor.SafeMemoryMappedViewHandle;
                byte* ptr = null;
                handle.AcquirePointer(ref ptr);

                try
                {
                    if (ptr == null)
                        throw new InvalidOperationException("Failed to acquire the pointer.");

                    byte* basePtr = ptr + Layout.HeaderSize;

                    // 1) 길이(int32) 쓰기 (할당 없이)
                    Span<byte> lenBytes = stackalloc byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(lenBytes, payloadLen);

                    WriteRing(basePtr, dataBufferSize, ref writePos, lenBytes);

                    // 2) header bytes 쓰기
                    if (!headerBytes.IsEmpty)
                        WriteRing(basePtr, dataBufferSize, ref writePos, headerBytes);

                    // 3) payload bytes 쓰기
                    if (!payloadBytes.IsEmpty)
                        WriteRing(basePtr, dataBufferSize, ref writePos, payloadBytes);

                    // 커밋: head는 마지막에
                    Thread.MemoryBarrier();
                    head += totalLength;
                    _accessor.Write(Layout.HeadOffset, head);
                    Thread.MemoryBarrier();
                }
                finally
                {
                    handle.ReleasePointer();
                }

                _producerEvent.Set();
                return true;
            }
            finally
            {
                _dataMutex.ReleaseMutex();
            }
        }

        private static unsafe void WriteRing(byte* basePtr, long dataBufferSize, ref long writePos, ReadOnlySpan<byte> src)
        {
            int firstPart = Math.Min(src.Length, (int)(dataBufferSize - writePos));
            src[..firstPart].CopyTo(new Span<byte>(basePtr + writePos, firstPart));

            if (firstPart < src.Length)
            {
                int secondPart = src.Length - firstPart;
                src[firstPart..].CopyTo(new Span<byte>(basePtr, secondPart));
            }

            writePos = (writePos + src.Length) % dataBufferSize;
        }

        private static void ValidateEndPoint(SharedMemoryEndPoint endPoint)
        {
            if (endPoint.MaxMessageLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(endPoint), "MaxMessageLength must be greater than zero.");

            if (endPoint.BufferCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(endPoint), "BufferCapacity must be greater than zero.");

            int minimumCapacity = checked(endPoint.Layout.MessageHeaderSize + PacketHeader.Size + endPoint.MaxMessageLength);
            if (endPoint.BufferCapacity < minimumCapacity)
            {
                throw new ArgumentException(
                    $"BufferCapacity must be at least {minimumCapacity} bytes to hold one framed message.",
                    nameof(endPoint));
            }
        }

        protected override void OnDispose()
        {
            OnDisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        protected override async ValueTask OnDisposeAsync()
        {
            try
            {
                if (Interlocked.Exchange(ref _completionRequested, 1) == 0)
                {
                    _outboundChannel.Writer.TryComplete();
                }

                if (!_sendProcessTask.IsCompleted)
                    _sendDataCTS.Cancel();

                await Task.WhenAll(_sendProcessTask ?? Task.CompletedTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore expected cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while shutting down the producer. Error={ex}.");
            }
            finally
            {
                _dataMutex.Dispose();
                _producerEvent.Dispose();
                _consumerEvent.Dispose();
                _accessor.Dispose();
                _mmf.Dispose();
                _sendDataCTS.Dispose();
            }

            await base.OnDisposeAsync();
        }
        #endregion
    }
}






