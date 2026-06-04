using OPNX.Lib.Common.Compression;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Network.Abstractions.Events;
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
    public class OPNXSMConsumer : DisposableObject
    {
        #region Fields
        private readonly SharedMemoryEndPoint _endPoint;

        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;

        private readonly Mutex _dataMutex;
        private readonly EventWaitHandle _producerEvent;
        private readonly EventWaitHandle _consumerEvent;

        private readonly Channel<Packet> _readChannel;

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processingTask;
        private readonly Task _consumeTask;

        private readonly ProtocolOptions _options;

        private static readonly ZstdCompressionProvider _zstd = new();
        #endregion

        #region Constructors
        public OPNXSMConsumer(SharedMemoryEndPoint endPoint, ProtocolOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(endPoint, nameof(endPoint));

            _options = options ?? ProtocolOptions.Default;
            _endPoint = endPoint;
            ValidateEndPoint(_endPoint);

            string mapName = _endPoint.MapName;
            long bufferSize = _endPoint.BufferCapacity;
            SharedMemoryLayout layout = _endPoint.Layout;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _mmf = MemoryMappedFile.CreateOrOpen(mapName, layout.HeaderSize + bufferSize, MemoryMappedFileAccess.ReadWrite);
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
            //        fs.SetLength(SharedMemoryCommon.HeaderSize + bufferSize);
            //    }

            //    var fileStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            //    mmf = MemoryMappedFile.CreateFromFile(
            //        fileStream,
            //        mapName,
            //        SharedMemoryCommon.HeaderSize + bufferSize,
            //        MemoryMappedFileAccess.ReadWrite,
            //        HandleInheritability.None,
            //        leaveOpen: false);
            //}

            _accessor = _mmf.CreateViewAccessor(0, layout.HeaderSize + bufferSize, MemoryMappedFileAccess.ReadWrite);

            _dataMutex = new Mutex(false, $"{mapName}_DATA_MUTEX");
            _producerEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"{mapName}_PRODUCER_EVENT");
            _consumerEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"{mapName}_CONSUMER_EVENT");

            _readChannel = Channel.CreateUnbounded<Packet>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false, // Allow multiple producers if needed
            });

            _processingTask = Task.Run(() => ProcessAsync(_cts.Token));
            _consumeTask = Task.Run(() => ConsumeAsync(_cts.Token));
        }
        #endregion

        #region Properties
        private SharedMemoryLayout Layout => _endPoint.Layout;

        public Guid SessionID { get; } = Guid.NewGuid();
        #endregion

        #region Events
        public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
        #endregion

        #region Private / Protected Methods
        private async Task ConsumeAsync(CancellationToken token)
        {
            try
            {
                await foreach (var packet in _readChannel.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    using (packet)
                    {
                        if (IsDisposed)
                            break;

                        try
                        {
                            PacketReceived?.Invoke(this, new PacketReceivedEventArgs(SessionID, packet.Header, packet.Payload));
                        }
                        catch (Exception ex)
                        {
                            LogManager.Error(ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }

        private async Task ProcessAsync(CancellationToken token)
        {
            var buffer = new byte[_endPoint.MaxMessageLength];

            while (!token.IsCancellationRequested)
            {
                //await WaitForEventAsync(_producerEvent, 10, token);

                if (!WaitProducerOrCancel(_producerEvent, 10, token))
                    continue;

                if (token.IsCancellationRequested)
                    break;

                if (ReadFromSharedMemory(buffer, out int length) && length > 0)
                {
                    byte[] rented = ArrayPool<byte>.Shared.Rent(length);
                    buffer.AsSpan(0, length).CopyTo(rented);

                    try
                    {
                        var seq = new ReadOnlySequence<byte>(rented, 0, length);


                        if (!PacketFramer.TryReadFrame(ref seq, out PacketHeader header, out ReadOnlySequence<byte> payload))
                            continue;

                        Packet? packet = null;
                        try
                        {
                            packet = ProcessPacket(header, payload);

                            if (_readChannel.Writer.TryWrite(packet))
                            {
                                packet = null; // 소유권 채널로 이동
                                continue;
                            }
                            else
                            {
                                await _readChannel.Writer.WriteAsync(packet, token).ConfigureAwait(false);
                                packet = null; // 소유권 채널로 이동
                            }

                            _consumerEvent.Set();
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
                            LogManager.Error($"Failed to process the packet. Error={ex}.");
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }
            }
        }

        private static Packet ProcessPacket(PacketHeader header, ReadOnlySequence<byte> payload)
        {
            try
            {
                if (header.IsCompressed)
                {
                    // 압축 해제는 MemoryPool owner로 반환 (rented 참조 없음)
                    var (owner, size) = _zstd.Decompress(payload);
                    return new Packet(header, owner, size);
                }

                // 비압축도 항상 MemoryPool로 복사 (rented 참조 없음)
                int size2 = checked((int)payload.Length);
                var owner2 = MemoryPool<byte>.Shared.Rent(size2);
                payload.CopyTo(owner2.Memory.Span);
                return new Packet(header, owner2, size2);
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to process packet data. Error={ex}.");
                throw;
            }
        }

        private unsafe bool ReadFromSharedMemory(byte[] buffer, out int readLength)
        {
            readLength = 0;

            bool lockTaken = false;

            try
            {
                try
                {
                    // 무한대기 방지 (IPC에서는 필수)
                    lockTaken = _dataMutex.WaitOne(100); // 필요하면 값 조정
                }
                catch (AbandonedMutexException ex)
                {
                    // Producer가 Mutex 잡은 채로 죽은 경우
                    // 이 경우에도 Mutex는 획득된 상태로 간주할 수 있음
                    lockTaken = true;
                    LogManager.Warning($"[SharedMemory] Abandoned mutex detected. {ex.Message}");
                }

                if (!lockTaken)
                    return false;

                // 메모리 장벽 - 최신 헤드/테일 값 보장
                Thread.MemoryBarrier();

                _accessor.Read(Layout.HeadOffset, out long head);
                _accessor.Read(Layout.TailOffset, out long tail);
                long dataBufferSize = _endPoint.BufferCapacity;

                // 수정된 사용 공간 계산
                long used = (head >= tail) ? (head - tail) : (dataBufferSize - tail + head);

                if (used < Layout.MessageHeaderSize)
                    return false;

                long readPos = tail % dataBufferSize;
                var handle = _accessor.SafeMemoryMappedViewHandle;
                byte* ptr = null;
                handle.AcquirePointer(ref ptr);

                try
                {
                    if (ptr == null)
                        throw new InvalidOperationException("Failed to acquire pointer.");

                    byte* basePtr = ptr + Layout.HeaderSize;

                    // 1. 메시지 헤더 읽기
                    Span<byte> lengthBuffer = stackalloc byte[Layout.MessageHeaderSize];
                    int lengthFirstPart = Math.Min(Layout.MessageHeaderSize, (int)(dataBufferSize - readPos));
                    new Span<byte>(basePtr + readPos, lengthFirstPart).CopyTo(lengthBuffer);

                    if (lengthFirstPart < Layout.MessageHeaderSize)
                    {
                        int lengthSecondPart = Layout.MessageHeaderSize - lengthFirstPart;
                        new Span<byte>(basePtr, lengthSecondPart).CopyTo(lengthBuffer[lengthFirstPart..]);
                    }

                    readPos = (readPos + Layout.MessageHeaderSize) % dataBufferSize;
                    int messageLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

                    // 강화된 검증
                    if (messageLength <= 0 ||
                        messageLength > buffer.Length ||
                        messageLength > _endPoint.MaxMessageLength ||
                        used < Layout.MessageHeaderSize + messageLength)
                    {
                        return false;
                    }

                    // 2. 실제 데이터 읽기
                    int dataFirstPart = Math.Min(messageLength, (int)(dataBufferSize - readPos));
                    new Span<byte>(basePtr + readPos, dataFirstPart).CopyTo(buffer.AsSpan(0, dataFirstPart));

                    if (dataFirstPart < messageLength)
                    {
                        int dataSecondPart = messageLength - dataFirstPart;
                        new Span<byte>(basePtr, dataSecondPart).CopyTo(buffer.AsSpan(dataFirstPart, dataSecondPart));
                    }

                    // 메모리 장벽 - 모든 읽기가 완료되었음을 보장
                    Thread.MemoryBarrier();

                    // 3. 테일 업데이트는 모든 읽기 완료 후
                    tail += Layout.MessageHeaderSize + messageLength;
                    _accessor.Write(Layout.TailOffset, tail);

                    // 테일 업데이트 후 메모리 장벽
                    Thread.MemoryBarrier();

                    readLength = messageLength;

                    return true;
                }
                finally
                {
                    handle.ReleasePointer();
                }
            }
            finally
            {
                if (lockTaken)
                {
                    try
                    {
                        _dataMutex.ReleaseMutex();
                    }
                    catch { }
                }
            }
        }

        private static async Task<bool> WaitForEventAsync(EventWaitHandle handle, int timeoutMs, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RegisteredWaitHandle? registeredHandle = null;

            registeredHandle = ThreadPool.RegisterWaitForSingleObject(handle,
                                                                     (state, timedOut) =>
                                                                     {
                                                                         var localTcs = (TaskCompletionSource<bool>)state!;
                                                                         localTcs.TrySetResult(!timedOut);  // true = signaled, false = timed out
                                                                     },
                                                                     tcs,
                                                                     timeoutMs,
                                                                     executeOnlyOnce: true);

            using (token.Register(() =>
            {
                registeredHandle?.Unregister(null); // 리소스 정리
                tcs.TrySetCanceled(token);
            }))
            {
                try
                {
                    return await tcs.Task;
                }
                finally
                {
                    registeredHandle?.Unregister(null);
                }
            }
        }

        private static bool WaitProducerOrCancel(EventWaitHandle producer, int timeoutMs, CancellationToken token)
        {
            int idx = WaitHandle.WaitAny([producer, token.WaitHandle], timeoutMs);
            if (idx == 1) throw new OperationCanceledException(token);
            return idx == 0; // true면 producer signaled
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

        protected override async ValueTask OnDisposeAsync()
        {
            try
            {
                _cts.Cancel();

                _readChannel.Writer.TryComplete();

                await Task.WhenAll(_processingTask ?? Task.CompletedTask,
                                    _consumeTask ?? Task.CompletedTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore expected cancellation
            }
            catch (Exception ex)
            {
                LogManager.Error($"An error occurred while shutting down the producer. Error={ex}.");
            }
            finally
            {
                _dataMutex?.Dispose();
                _producerEvent?.Dispose();
                _consumerEvent?.Dispose();
                _accessor?.Dispose();
                _mmf?.Dispose();

                _cts.Dispose();
            }

            await base.OnDisposeAsync();
        }
        #endregion
    }
}

