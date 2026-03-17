using OPNX.Lib.Common.Logging;
using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OPNX.Lib.Streaming.RTSP
{
    public class RTSPHttpServerTransport : IRtspTransport, IDisposable
    {
        private class HttpTransportStream(RTSPHttpServerTransport parent, Stream getChannelStream) : Stream
        {
            private readonly Stream _outStream = getChannelStream;
            private readonly RTSPHttpServerTransport _parent = parent;

            public override bool CanRead => _outStream.CanRead;

            public override bool CanSeek => false;

            public override bool CanWrite => _outStream.CanWrite;

            public override long Length => throw new NotSupportedException("Not supported in network");

            public override long Position
            {
                get => throw new NotSupportedException("Not supported in network");
                set => throw new NotSupportedException("Not supported in network");
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("Not supported in network");

            public override void SetLength(long value) => throw new NotSupportedException("Not supported in network");

            public override void Flush() => _outStream.Flush();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var reader = _parent._decodedDataPipe.Reader;
                var result = await reader.ReadAtLeastAsync(buffer.Length, cancellationToken).ConfigureAwait(false);

                result.Buffer.Slice(0, buffer.Length).CopyTo(buffer.Span);
                var handlePart = result.Buffer.GetPosition(buffer.Length);
                reader.AdvanceTo(handlePart, handlePart);

                return buffer.Length;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
            {
                var reader = _parent._decodedDataPipe.Reader;
                var result = await reader.ReadAtLeastAsync(count, cancellationToken).ConfigureAwait(false);

                result.Buffer.Slice(0, count).CopyTo(buffer.AsSpan(offset, count));
                var handlePart = result.Buffer.GetPosition(count);
                reader.AdvanceTo(handlePart, handlePart);

                return count;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadAsync(buffer, offset, count, default).Result;
            }

            public override void Write(byte[] buffer, int offset, int count) => _outStream.Write(buffer, offset, count);

            public override void Write(ReadOnlySpan<byte> buffer) => _outStream.Write(buffer);
        }

        //private readonly ILogger _logger;
        private TcpClient _postChannelClient;
        private TcpClient _getChannelClient;
        private Stream _stream;
        private uint _commandCounter;
        private bool _disposedValue;
        private readonly Pipe _decodedDataPipe = new();
        private readonly CancellationTokenSource _stop = new();
        public readonly DateTime creationTime = DateTime.UtcNow;

        internal enum UpdateState
        {
            Ok,
            NewSession,
            Error,
        }

        public IPEndPoint RemoteEndPoint { get; private set; } = null!;

        public IPEndPoint LocalEndPoint { get; private set; } = null!;

        public bool Connected => _getChannelClient?.Connected == true;

        public bool IsObsolete
        {
            get
            {
                // Not fully initialized, it can live 5 minutes
                if (_getChannelClient is null || _postChannelClient is null)
                {
                    return creationTime.AddMinutes(5) < DateTime.UtcNow;
                }
                return !_getChannelClient.Connected;
            }
        }

        //internal RTSPHttpServerTransport(ILogger<RTSPHttpServerTransport> logger)
        //{
        //    _logger = logger as ILogger ?? NullLogger.Instance;
        //}

        public void Close()
        {
            _stop.Cancel();
            _postChannelClient?.Close();
            _getChannelClient?.Close();
        }

        public Stream GetStream() => _stream ?? throw new InvalidOperationException("Invalid internal state");

        public uint NextCommandIndex() => ++_commandCounter;

        public void Reconnect() => throw new InvalidOperationException("Server can not reconnect to client");

        internal UpdateState UpdatePostChannel(TcpClient client, Stream stream)
        {
            LogManager.Debug("New post channel detected");
            var wasPresent = _postChannelClient != null;
            _postChannelClient?.Close();

            _postChannelClient = client;
            _ = Task.Factory.StartNew(async () =>
            {
                await DecodePostChannel(stream, _stop.Token).ConfigureAwait(false);
                client.Close();
            },
                _stop.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Current);

            if (!wasPresent && _getChannelClient != null)
            {
                return UpdateState.NewSession;
            }
            return UpdateState.Ok;
        }

        private async Task DecodePostChannel(Stream postChannelStream, CancellationToken token)
        {
            try
            {
                var pipeSource = PipeReader.Create(postChannelStream);
                var pipeDest = _decodedDataPipe.Writer;
                while (!token.IsCancellationRequested)
                {

                    var sourceReadResult = await pipeSource.ReadAsync(token).ConfigureAwait(false);
                    var sourceBuffer = sourceReadResult.Buffer;

                    var roundLength = (int)(sourceBuffer.Length / 4 * 4);

                    var destMemory = pipeDest.GetMemory((int)sourceBuffer.Length);
                    sourceBuffer.Slice(0, roundLength).CopyTo(destMemory.Span);
                    //var decodeResult = Base64.DecodeFromUtf8InPlace(destMemory.Span.Slice(0, roundLength), out int written);
                    var decodeResult = Base64.DecodeFromUtf8InPlace(destMemory.Span[..roundLength], out int written);

                    if (decodeResult == OperationStatus.Done)
                    {
                        pipeDest.Advance(written);
                        var sourcePosition = sourceBuffer.GetPosition(roundLength);
                        pipeSource.AdvanceTo(sourcePosition, sourcePosition);
                        FlushResult flushResult = await pipeDest.FlushAsync(token).ConfigureAwait(false);
                        if (flushResult.IsCompleted)
                        {
                            // reader is closed
                            LogManager.Debug("Dest channel close");
                            break;
                        }
                        if (sourceReadResult.IsCompleted)
                        {
                            LogManager.Debug("Post Channel close");
                            // source tcp is closed
                            break;
                        }

                    }
                    else if (decodeResult != OperationStatus.NeedMoreData)
                    {
                        LogManager.Warning("Invalid data receive for base64, fail to decode post channel, data ={data}",
                            Encoding.UTF8.GetString(sourceBuffer.Slice(0, roundLength).ToArray()));
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogManager.Debug("Decode post channel canceled");
            }
            catch (IOException ex)
            {
                LogManager.Warning(ex, "Error during post channel decode");
            }
        }

        internal UpdateState UpdateGetChannel(TcpClient client, Stream stream)
        {
            LogManager.Debug("New get channel");
            if (_getChannelClient != null)
            {
                LogManager.Warning("Get channel already present, fail");
                return UpdateState.Error;
            }
            _getChannelClient = client;
            _stream = new HttpTransportStream(this, stream);
            RemoteEndPoint = _getChannelClient?.Client?.RemoteEndPoint as IPEndPoint ?? throw new InvalidOperationException("The local endpoint can not be determined.");
            LocalEndPoint = _getChannelClient?.Client?.LocalEndPoint as IPEndPoint ?? throw new InvalidOperationException("The local endpoint can not be determined.");


            if (_postChannelClient != null)
            {
                return UpdateState.NewSession;
            }
            return UpdateState.Ok;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Close();
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
