using OPNX.Lib.Common.Buffers;
using OPNX.Lib.Common.Compression;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Network.Protocol.Abstractions;
using OPNX.Lib.Network.Protocol.Framing;
using OPNX.Lib.Network.Transport.SharedMemory;
using OPNX.Lib.Common.Serialization;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace OPNX.Lib.Network.Protocol.SharedMemory
{
    public class OPNXSMProducer : DisposableBase
    {
        #region Fields
        private readonly SharedMemoryEndPoint _endPoint;

        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;

        private readonly Mutex _dataMutex;
        private readonly EventWaitHandle _producerEvent;
        private readonly EventWaitHandle _consumerEvent;

        private static readonly ZstdCompressionProvider _zstd = new();

        private readonly CancellationTokenSource _sendDataCTS = new();
        private readonly Channel<Packet> _outboundChannel = Channel.CreateUnbounded<Packet>(
            new UnboundedChannelOptions
            {
                SingleReader = true,   // 기본값 false지만 실제로는 1 reader만 사용하므로 true
                SingleWriter = false
            });

        private readonly Task _sendProcessTask;

        private readonly ProtocolOptions _options;
        #endregion

        #region Constructors
        public OPNXSMProducer(SharedMemoryEndPoint endPoint, ProtocolOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(endPoint, nameof(endPoint));

            _options = options ?? ProtocolOptions.Default;
            _endPoint = endPoint;

            string mapName = _endPoint.MapName;
            long bufferSize = _endPoint.MaxMessageLength;
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
            if (IsDisposed)
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
        #endregion

        #region Private / Protected Methods
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
                        if (!TryWriteToSharedMemory(hdr, packet.Payload.Span))
                        {
                            LogManager.Warning("SharedMemory full - drop");
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
                LogManager.Error($"SendPacketProcessorAsync: {ex}");
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
                        throw new InvalidOperationException("Pointer acquisition failed");

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

        protected override async void OnDispose()
        {
            await OnDisposeAsync();
        }

        protected override async ValueTask OnDisposeAsync()
        {
            try
            {
                _sendDataCTS.Cancel();

                _outboundChannel?.Writer.Complete();

                await Task.WhenAll(_sendProcessTask ?? Task.CompletedTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore expected cancellation
            }
            catch (Exception ex)
            {
                LogManager.Error($"Error during producer shutdown: {ex}");
            }
            finally
            {
                _sendDataCTS.Dispose();
            }

            await base.OnDisposeAsync();
        }
        #endregion
    }
}
