using OPNX.Lib.Common.Logging;
using OPNX.Lib.Streaming.RTSP.Messages;
using OPNX.Lib.Streaming.RTSP.Utils;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.Contracts;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace OPNX.Lib.Streaming.RTSP
{
    /// <summary>
    /// Rtsp lister
    /// </summary>
    public class RTSPListener : IDisposable
    {
        private const int DefaultReadTimeoutMilliseconds = 5000;   // 5초
        private const int DefaultWriteTimeoutMilliseconds = 3000;  // 3초

        private enum ReadingMessage
        {
            NotEnoughtData,
            PartialMessage,
            MessageFinish,
        }

        [Serializable]
        private enum ReadingState
        {
            NewCommand,
            Headers,
            Data,
            End,
            InterleavedData,
            MoreInterleavedData,
        }

        //private readonly ILogger _logger;
        private readonly MemoryPool<byte> _memoryPool;
        private readonly IRtspTransport _transport;
        private readonly SentMessageList _sentMessage = new();

        private CancellationTokenSource? _cancelationTokenSource;
        private Task? _mainTask;
        private Stream _stream;
        private readonly SemaphoreSlim writeSemaphoreSlim = new(1, 1);

        private int _sequenceNumber;


        /// <summary>
        /// Initializes a new instance of the <see cref="RtspListener"/> class from a TCP connection.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="logger">Logger</param>
        //public RTSPListener(
        //    IRtspTransport connection,
        //    ILogger<RTSPListener> logger = null,
        //    MemoryPool<byte> memoryPool = null)
        public RTSPListener(
            IRtspTransport connection,
            MemoryPool<byte>? memoryPool = null)
        {
            //_logger = logger as ILogger ?? NullLogger.Instance;
            _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;

            _transport = connection ?? throw new ArgumentNullException(nameof(connection));
            _stream = connection.GetStream();
            _stream.ReadTimeout = DefaultReadTimeoutMilliseconds;
            _stream.WriteTimeout = DefaultWriteTimeoutMilliseconds;
        }

        public Guid ConnectionID { get; set; }

        /// <summary>
        /// Gets the remote address.
        /// <value>The remote address.</value>
        /// <remarks>In addition to being misspelled, this property actually returns an IP:port pair.</remarks>
        public string RemoteAddress => RemoteEndPoint.Address.ToString();

        /// <summary>
        /// Gets the remote endpoint.
        /// </summary>
        /// <value>The remote endpoint.</value>
        public IPEndPoint RemoteEndPoint => _transport.RemoteEndPoint;

        /// <summary>
        /// Gets the local enpoint.
        /// </summary>
        /// <value>The local endpoint.</value>
        public IPEndPoint LocalEndPoint => _transport.LocalEndPoint;

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public void Start()
        {
            _cancelationTokenSource = new();
            _mainTask = Task.Factory.StartNew(async () => await DoJobAsync(_cancelationTokenSource.Token).ConfigureAwait(false),
                _cancelationTokenSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Current);
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public void Stop()
        {
            // brutally  close the TCP socket....
            // I hope the teardown was sent elsewhere            
            _cancelationTokenSource?.Cancel();
            _transport.Close();
        }

        /// <summary>
        /// Enable auto reconnect.
        /// </summary>
        public bool AutoReconnect { get; set; }

        /// <summary>
        /// Occurs when message is received.
        /// </summary>
        public event EventHandler<RTSPChunkEventArgs>? MessageReceived;

        /// <summary>
        /// Raises the <see cref="E:MessageReceived"/> event.
        /// </summary>
        /// <param name="e">The <see cref="Rtsp.RtspChunkEventArgs"/> instance containing the event data.</param>
        protected void OnMessageReceived(RTSPChunkEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        /// <summary>
        /// Occurs when Data is received.
        /// </summary>
        public event EventHandler<RTSPChunkEventArgs>? DataReceived;

        /// <summary>
        /// Raises the <see cref="E:DataReceived"/> event.
        /// </summary>
        /// <param name="rtspChunkEventArgs">The <see cref="Rtsp.RtspChunkEventArgs"/> instance containing the event data.</param>
        protected void OnDataReceived(RTSPChunkEventArgs rtspChunkEventArgs)
        {
            DataReceived?.Invoke(this, rtspChunkEventArgs);
        }

        public event EventHandler? Opened;
        public event EventHandler? Closed;

        /// <summary>
        /// Does the reading job.
        /// </summary>
        /// <remarks>
        /// This method read one message from TCP connection.
        /// If it a response it add the associate question.
        /// The stopping is made by the closing of the TCP connection.
        /// </remarks>
        private async Task DoJobAsync(CancellationToken token)
        {
            try
            {
                Opened?.Invoke(this, EventArgs.Empty); //cutom add

                LogManager.Debug("Connection Open");
                //var pipe = PipeReader.Create(_stream);
                while (_transport.Connected && !token.IsCancellationRequested)
                {
                    // La lectuer est blocking sauf si la connection est coupé                    
                    RtspChunk? currentMessage = await ReadOneMessageAsync(_stream, token).ConfigureAwait(false);

                    if (currentMessage is null)
                    {
                        break;
                    }


                    //if (_logger.IsEnabled(LogLevel.Debug) && currentMessage is not RtspData)
                    //{
                    //    // on logue le tout
                    //    if (currentMessage.SourcePort != null)
                    //        _logger.LogDebug("Receive from {remoteAdress}", currentMessage.SourcePort.RemoteEndPoint);
                    //    _logger.LogDebug("{message}", currentMessage);
                    //}

                    if (currentMessage is not RtspData)
                    {
                        // on logue le tout
                        if (currentMessage.SourcePort != null)
                            LogManager.Debug("Receive from {remoteAdress}", currentMessage.SourcePort.RemoteEndPoint);
                        LogManager.Debug("{message}", currentMessage);
                    }

                    switch (currentMessage)
                    {
                        case RtspResponse response:
                            // add the original question to the response.
                            if (_sentMessage.TryPopValue(response.CSeq, out var originalRequest))
                            {
                                response.OriginalRequest = originalRequest;
                            }
                            else
                            {
                                LogManager.Warning("Receive response not asked {cseq}", response.CSeq);
                            }
                            OnMessageReceived(new RTSPChunkEventArgs(response));
                            break;

                        case RtspRequest:
                            OnMessageReceived(new RTSPChunkEventArgs(currentMessage));
                            break;
                        case RtspData:
                            OnDataReceived(new RTSPChunkEventArgs(currentMessage));
                            break;
                            //default:
                            //    await Task.Run(() => _logger.LogWarning("Unexpected message type: {messageType}", currentMessage.GetType())).ConfigureAwait(false);
                            //    break;
                    }
                }
            }
            catch (IOException error)
            {
                LogManager.Warning(error, "IO Error");
            }
            catch (SocketException error)
            {
                LogManager.Warning(error, "Socket Error");
            }
            catch (ObjectDisposedException error)
            {
                LogManager.Warning(error, "Object Disposed");
            }
            catch (Exception error)
            {
                LogManager.Warning(error, "Unknow Error");
            }
            finally
            {
                _stream.Close();
                _transport.Close();
            }

            LogManager.Debug("Connection Close");
            Closed?.Invoke(this, EventArgs.Empty); //custom add
        }

        private static async Task<int> ReadByteAsync(Stream stream, CancellationToken token)
        {
            //var buffer = new byte[1];
            //int bytesRead = await stream.ReadAsync(buffer, 0, 1, token).ConfigureAwait(false);
            //return bytesRead > 0 ? buffer[0] : -1;
            byte[] buffer = new byte[1];
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            return bytesRead == 1 ? buffer[0] : -1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> SendMessageAsync(RtspMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            if (!_transport.Connected)
            {
                if (!AutoReconnect)
                    return false;

                LogManager.Warning("Reconnect to a client, strange !!");
                try
                {
                    Reconnect(); // 이 부분도 비동기화가 가능하면 await ReconnectAsync()로 변경 가능
                }
                catch (SocketException)
                {
                    return false;
                }
            }

            if (message is RtspRequest originalMessage)
            {
                message = (RtspMessage)message.Clone();
                _sequenceNumber++;
                message.CSeq = _sequenceNumber;
                _sentMessage.Add(message.CSeq, originalMessage);
            }

            LogManager.Debug("Send Message\n {message}", message);

            await message.SendToAsync(_stream, cancellationToken).ConfigureAwait(false);
            return true;
        }


        /// <summary>
        /// Sends the message.
        /// </summary>
        /// <param name="message">A message.</param>
        /// <returns><see cref="true"/> if it is Ok, otherwise <see cref="false"/></returns>
        public bool SendMessage(RtspMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            //if (message == null)
            //    throw new ArgumentNullException(nameof(message));

            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                if (!AutoReconnect)
                    return false;

                LogManager.Warning("Reconnect to a client, strange !!");
                try
                {
                    Reconnect();
                }
                catch (SocketException)
                {
                    // on a pas put se connecter on dit au manager de plus compter sur nous
                    return false;
                }
            }

            // if it it a request  we store the original message
            // and we renumber it.
            if (message is RtspRequest originalMessage)
            {
                // Do not modify original message
                message = (RtspMessage)message.Clone();
                _sequenceNumber++;
                message.CSeq = _sequenceNumber;
                _sentMessage.Add(message.CSeq, originalMessage);
            }

            LogManager.Debug("Send Message\n {message}", message);
            message.SendTo(_stream);
            return true;
        }

        /// <summary>
        /// Reconnect this instance of RtspListener.
        /// </summary>
        /// <exception cref="SocketException">Error during socket </exception>
        public void Reconnect()
        {
            //if it is already connected do not reconnect
            if (_transport.Connected)
                return;

            // If it is not connected listenthread should have die.
            _mainTask?.Wait();

            _stream?.Dispose();

            // reconnect 
            _transport.Reconnect();
            _stream = _transport.GetStream();

            // If listen thread exist restart it
            if (_mainTask != null)
                Start();
        }

        //private readonly List<byte> readOneMessageBuffer = new(256);

        /// <summary>
        /// Reads one message.
        /// </summary>
        /// <param name="reader">The Rtsp stream pipereader.</param>
        /// <returns>Message read</returns>
        public async ValueTask<RtspChunk?> ReadOneMessageAsync(PipeReader reader, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            //if (reader == null)
            //    throw new ArgumentNullException(nameof(reader));

            ReadingMessage currentReadingState = ReadingMessage.NotEnoughtData;
            RtspChunk? currentMessage = null;

            while (currentReadingState != ReadingMessage.MessageFinish)
            {
                var result = await reader.ReadAsync(token).ConfigureAwait(false);
                var buffer = result.Buffer;
                try
                {
                    currentReadingState = TryReadMessage(ref buffer, ref currentMessage);
                    if (result.IsCompleted)
                        break;
                }
                finally
                {
                    if (currentReadingState != ReadingMessage.NotEnoughtData)
                    {
                        reader.AdvanceTo(buffer.Start, buffer.Start);
                    }
                    else
                    {
                        reader.AdvanceTo(buffer.Start, buffer.End);
                    }
                }
            }

            currentMessage?.SourcePort = this;
            return currentMessage;
        }

        private ReadingMessage TryReadMessage(ref ReadOnlySequence<byte> buffer, ref RtspChunk? currentMessage)
        {
            if (currentMessage is null && buffer.First.Length > 0 && buffer.First.Span[0] == '$')
            {
                if (buffer.Length < 4) return ReadingMessage.NotEnoughtData;

                //var channel = buffer.First.Span[1];
                //var size = BinaryPrimitives.ReadUInt16BigEndian(buffer.First.Span[2..]);
                Span<byte> header = stackalloc byte[3];
                buffer.Slice(1, 3).CopyTo(header);

                var channel = header[0];
                var size = BinaryPrimitives.ReadUInt16BigEndian(header[1..]);
                if (buffer.Length < size + 4)
                    return ReadingMessage.NotEnoughtData;

                var reservedData = _memoryPool.Rent(size);
                currentMessage = new RtspData(reservedData, size)
                {
                    Channel = channel,
                };
                buffer.Slice(4, size).CopyTo(reservedData.Memory.Span);
                buffer = buffer.Slice(size + 4);
                return ReadingMessage.MessageFinish;
            }

            if (currentMessage?.Data.IsEmpty == false)
            {
                if (buffer.Length >= currentMessage.Data.Length)
                {
                    buffer.Slice(0, currentMessage.Data.Length).CopyTo(currentMessage.Data.Span);
                    buffer = buffer.Slice(currentMessage.Data.Length);
                    return ReadingMessage.MessageFinish;
                }
                return ReadingMessage.NotEnoughtData;
            }

            var pos = buffer.FindEndOfLine();
            if (!pos.HasValue)
            {
                return ReadingMessage.NotEnoughtData;
            }
            var (endOfLinePos, startOfNextLinePos) = pos.Value;

            // convert to line
            var bufferLine = buffer.Slice(0, endOfLinePos);
#if NET5_0_OR_GREATER
            var line = Encoding.UTF8.GetString(bufferLine);
#else
            // not optimal, need to add the correct version to polyfill
            var line = bufferLine.IsSingleSegment ? Encoding.UTF8.GetString(bufferLine.First.Span) : Encoding.UTF8.GetString(bufferLine.ToArray());
#endif
            bool messageIsFinished = false;
            if (currentMessage is null)
            {
                currentMessage = RtspMessage.GetRtspMessage(line);
            }
            else
            {
                if (string.IsNullOrEmpty(line))
                {
                    ((RtspMessage)currentMessage).InitialiseDataFromContentLength();
                    messageIsFinished = currentMessage.Data.Length == 0;
                }
                else
                {
                    ((RtspMessage)currentMessage).AddHeader(line);
                }
            }
            buffer = buffer.Slice(startOfNextLinePos);
            return messageIsFinished ? ReadingMessage.MessageFinish : ReadingMessage.PartialMessage;
        }

        /// <summary>
        /// Reads one message.
        /// </summary>
        /// <param name="commandStream">The Rtsp stream.</param>
        /// <returns>Message readen</returns>
        public async ValueTask<RtspChunk?> ReadOneMessageAsync(Stream commandStream, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(commandStream);
            //if (commandStream == null)
            //    throw new ArgumentNullException(nameof(commandStream));
            Contract.EndContractBlock();

            ReadingState currentReadingState = ReadingState.NewCommand;
            RtspChunk? currentMessage = null;

            int size = 0;
            int byteReaden = 0;
            var byteList = new List<byte>();
            StringBuilder oneLineBuilder = new();
            bool needMoreChar;

            while (currentReadingState != ReadingState.End)
            {
                if (currentReadingState != ReadingState.Data && currentReadingState != ReadingState.MoreInterleavedData)
                {
                    oneLineBuilder.Clear();
                    needMoreChar = true;

                    while (needMoreChar)
                    {
                        int currentByte = commandStream.ReadByte();

                        switch (currentByte)
                        {
                            case -1:
                                currentReadingState = ReadingState.End;
                                needMoreChar = false;
                                break;
                            case '\n':
                                //oneLineBuilder.Append(Encoding.UTF8.GetString(byteList.ToArray()));
                                oneLineBuilder.Append(Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(byteList)));
                                byteList.Clear();// Clear the memory stream
                                needMoreChar = false;
                                break;
                            case '\r':
                                break;
                            case '$' when currentReadingState == ReadingState.NewCommand && byteList.Count == 0:
                                currentReadingState = ReadingState.InterleavedData;
                                needMoreChar = false;
                                break;
                            default:
                                byteList.Add((byte)currentByte);
                                break;
                        }
                    }
                }

                switch (currentReadingState)
                {
                    case ReadingState.NewCommand:
                        currentMessage = RtspMessage.GetRtspMessage(oneLineBuilder.ToString());
                        currentReadingState = ReadingState.Headers;
                        break;
                    case ReadingState.Headers:
                        if (string.IsNullOrEmpty(oneLineBuilder.ToString()))
                        {
                            currentReadingState = ReadingState.Data;
                            ((RtspMessage)currentMessage!).InitialiseDataFromContentLength();
                        }
                        else
                        {
                            ((RtspMessage)currentMessage!).AddHeader(oneLineBuilder.ToString());
                        }
                        break;
                    case ReadingState.Data when currentMessage is not null:
                        if (!currentMessage.Data.IsEmpty)
                        {
                            int byteCount = await commandStream.ReadAsync(currentMessage.Data[byteReaden..], token).ConfigureAwait(false);
                            if (byteCount <= 0)
                            {
                                currentReadingState = ReadingState.End;
                                break;
                            }
                            byteReaden += byteCount;
                            LogManager.Debug("Readen {byteReaden} byte of data", byteReaden);
                        }

                        if (byteReaden >= currentMessage.Data.Length)
                            currentReadingState = ReadingState.End;
                        break;
                    case ReadingState.InterleavedData:
                        int channelByte = await ReadByteAsync(commandStream, token).ConfigureAwait(false);
                        if (channelByte == -1)
                        {
                            currentReadingState = ReadingState.End;
                            break;
                        }
                        int sizeByte1 = await ReadByteAsync(commandStream, token).ConfigureAwait(false);
                        if (sizeByte1 == -1)
                        {
                            currentReadingState = ReadingState.End;
                            break;
                        }
                        int sizeByte2 = await ReadByteAsync(commandStream, token).ConfigureAwait(false);
                        if (sizeByte2 == -1)
                        {
                            currentReadingState = ReadingState.End;
                            break;
                        }
                        size = (sizeByte1 << 8) + sizeByte2;

                        var reservedData = _memoryPool.Rent(size);
                        currentMessage = new RtspData(reservedData, size)
                        {
                            Channel = channelByte,
                        };
                        currentReadingState = ReadingState.MoreInterleavedData;
                        break;
                    case ReadingState.MoreInterleavedData when currentMessage is not null:
                        {
                            int byteCount = await commandStream.ReadAsync(currentMessage.Data[byteReaden..], token).ConfigureAwait(false);
                            if (byteCount <= 0)
                            {
                                currentReadingState = ReadingState.End;
                                break;
                            }
                            byteReaden += byteCount;
                            if (byteReaden < size)
                                currentReadingState = ReadingState.MoreInterleavedData;
                            else
                                currentReadingState = ReadingState.End;
                            break;
                        }
                    default:
                        break;
                }
            }

            currentMessage?.SourcePort = this;
            return currentMessage;
        }

        public Task SendDataAsync(RtspData data) => SendDataAsync(data.Channel, data.Data);

        /// <summary>
        /// Send data (Synchronous)
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="frame">The frame.</param>
        public async Task SendDataAsync(int channel, ReadOnlyMemory<byte> frame)
        {
            if (frame.Length > 0xFFFF)
                throw new ArgumentException("frame too large", nameof(frame));

            if (_cancelationTokenSource is null)
                throw new InvalidOperationException("Listener is not started");
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                if (!AutoReconnect)
                    throw new Exception("Connection is lost");

                LogManager.Warning("Reconnect to a client, strange.");
                Reconnect();
            }

            // add 4 bytes for the header
            var packetLength = 4 + frame.Length;
            var data = ArrayPool<byte>.Shared.Rent(packetLength);
            try
            {
                data[0] = 36; // '$' character
                data[1] = (byte)channel;
                data[2] = (byte)((frame.Length & 0xFF00) >> 8);
                data[3] = (byte)(frame.Length & 0x00FF);
                frame.CopyTo(data.AsMemory(4));
                //await _stream.WriteAsync(data.AsMemory(0, packetLength), _cancelationTokenSource.Token).ConfigureAwait(false);
                await writeSemaphoreSlim.WaitAsync(_cancelationTokenSource.Token).ConfigureAwait(false);
                try
                {
                    await _stream.WriteAsync(data.AsMemory(0, packetLength), _cancelationTokenSource.Token).ConfigureAwait(false);
                }
                finally
                {
                    writeSemaphoreSlim.Release();
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(data);
            }
        }

        /// <summary>
        /// Send data (Synchronous)
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="frame">The frame.</param>
        public void SendData(int channel, ReadOnlySpan<byte> frame)
        {
            if (frame.Length > 0xFFFF)
                throw new ArgumentException("frame too large", nameof(frame));
            Contract.EndContractBlock();

            if (!_transport.Connected)
            {
                if (!AutoReconnect)
                    throw new Exception("Connection is lost");

                LogManager.Warning("Reconnect to a client, strange.");
                Reconnect();
            }

            // add 4 bytes for the header
            var packetLength = 4 + frame.Length;
            var data = ArrayPool<byte>.Shared.Rent(packetLength);
            try
            {
                data[0] = 36; // '$' character
                data[1] = (byte)channel;
                data[2] = (byte)((frame.Length & 0xFF00) >> 8);
                data[3] = (byte)(frame.Length & 0x00FF);
                frame.CopyTo(data.AsSpan(4));
                //_stream.Write(data, 0, packetLength);
                writeSemaphoreSlim.Wait();
                try
                {
                    _stream.Write(data, 0, packetLength);
                }
                finally
                {
                    writeSemaphoreSlim.Release();
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(data);
            }
        }

        /// <summary>
        /// Send data (Synchronous)
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="frame">The frame.</param>
        public void SendData(int channel, ReadOnlyMemory<byte> frame) => SendData(channel, frame.Span);

        #region IDisposable Membres

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
                _stream?.Dispose();
            }
        }

        #endregion
    }
}
