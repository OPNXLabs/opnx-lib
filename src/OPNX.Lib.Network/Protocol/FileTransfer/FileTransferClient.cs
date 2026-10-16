using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Network.Abstractions.Events;
using OPNX.Lib.Network.Protocol.Framing;
using OPNX.Lib.Network.Protocol.Tcp;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace OPNX.Lib.Network.Protocol.FileTransfer
{
    public sealed class FileTransferClient : IDisposable, IAsyncDisposable
    {
        private readonly FileTransferClientSession _session;

        public FileTransferClient(FileTransferClientOptions? options = null, ILoggerFactory? loggerFactory = null)
        {
            _session = new FileTransferClientSession(options, loggerFactory);
        }

        public Guid SessionID => _session.SessionID;
        public bool IsConnected => _session.IsConnected;

        public Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
            => _session.ConnectAsync(host, port, cancellationToken);

        public Task<bool> ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
            => _session.ConnectAsync(endPoint, cancellationToken);

        public Task<FileTransferPrepareResult> PrepareAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
            => _session.PrepareAsync(filePaths, cancellationToken);

        public Task<FileTransferPrepareResult> PrepareAsync(IEnumerable<FileTransferRequestItem> files, CancellationToken cancellationToken = default)
            => _session.PrepareAsync(files, cancellationToken);

        public Task<FileTransferResult> DownloadAsync(FileTransferPrepareResult prepared, string destinationDirectory, FileTransferFlowMode flowMode = FileTransferFlowMode.Automatic, IProgress<FileTransferProgress>? progress = null, CancellationToken cancellationToken = default)
            => _session.DownloadAsync(prepared, destinationDirectory, flowMode, progress, cancellationToken);

        public Task<bool> CancelAsync(Guid transferID, CancellationToken cancellationToken = default)
            => _session.CancelAsync(transferID, cancellationToken);

        public void Dispose()
            => _session.Dispose();

        public ValueTask DisposeAsync()
            => _session.DisposeAsync();
    }

    public sealed class FileTransferClientSession : FileTransferSession, IAsyncDisposable
    {
        #region Fields
        private readonly FileTransferClientOptions _options;
        private readonly ILogger<FileTransferClientSession> _logger;
        private readonly SemaphoreSlim _commandLock = new(1, 1);
        private readonly Lock _syncRoot = new();
        private TaskCompletionSource<FileTransferPrepareResult>? _prepareSource;
        private TaskCompletionSource<(FileTransferState State, string Message)>? _startSource;
        private ActiveDownload? _activeDownload;
        #endregion

        #region Constructors
        public FileTransferClientSession(FileTransferClientOptions? options = null, ILoggerFactory? loggerFactory = null)
            : this(CreateConnection(options, loggerFactory), options, true, loggerFactory)
        {
        }

        public FileTransferClientSession(OPNXTcpClient connection, FileTransferClientOptions? options = null, bool ownsConnection = false, ILoggerFactory? loggerFactory = null)
            : base(connection, FileTransferSessionRole.Client, ownsConnection, (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<FileTransferClientSession>())
        {
            _options = options ?? new FileTransferClientOptions();
            ValidateOptions(_options);
            ILoggerFactory factory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = factory.CreateLogger<FileTransferClientSession>();
        }
        #endregion

        #region Properties
        private static OPNXTcpClient CreateConnection(FileTransferClientOptions? options, ILoggerFactory? loggerFactory)
        {
            FileTransferClientOptions clientOptions = options ?? new FileTransferClientOptions();
            ILoggerFactory factory = loggerFactory ?? NullLoggerFactory.Instance;
            return new OPNXTcpClient(null!, clientOptions.ConnectionOptions, clientOptions.ProtocolOptions, factory.CreateLogger<OPNXTcpClient>());
        }
        #endregion

        #region Public Methods
        public async Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(host);
            if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
                throw new ArgumentOutOfRangeException(nameof(port));
            IPAddress address;
            if (!IPAddress.TryParse(host, out address!))
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                address = addresses.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?? addresses.FirstOrDefault()
                    ?? throw new SocketException((int)SocketError.HostNotFound);
            }
            return await ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
        }

        public Task<bool> ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ArgumentNullException.ThrowIfNull(endPoint);
            return Connection.ConnectAsync(endPoint, cancellationToken);
        }

        public Task<FileTransferPrepareResult> PrepareAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(filePaths);
            return PrepareAsync(filePaths.Select(x => new FileTransferRequestItem(Guid.NewGuid(), x)), cancellationToken);
        }

        public async Task<FileTransferPrepareResult> PrepareAsync(IEnumerable<FileTransferRequestItem> files, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ArgumentNullException.ThrowIfNull(files);
            FileTransferRequestItem[] requests = [.. files];
            if (requests.Length == 0)
                throw new ArgumentException("At least one file is required.", nameof(files));
            if (requests.Length > 10000)
                throw new ArgumentOutOfRangeException(nameof(files), "No more than 10000 files can be prepared at once.");
            if (requests.Any(x => x.FileID == Guid.Empty || string.IsNullOrWhiteSpace(x.FilePath)))
                throw new ArgumentException("Every file must have a non-empty ID and path.", nameof(files));

            await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                lock (_syncRoot)
                {
                    if (_activeDownload is not null)
                        throw new InvalidOperationException("A file transfer is already active.");
                }

                Guid transferID = Guid.NewGuid();
                TaskCompletionSource<FileTransferPrepareResult> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_syncRoot)
                    _prepareSource = source;

                var (owner, payloadLength) = FileTransferProtocol.Write(writer =>
                {
                    writer.Write(transferID.ToByteArray());
                    writer.Write(requests.Length);
                    foreach (FileTransferRequestItem file in requests)
                    {
                        writer.Write(file.FileID.ToByteArray());
                        FileTransferProtocol.WriteString(writer, file.FilePath);
                    }
                });
                if (!await Connection.SendOwnedDataAsync(FileTransferProtocol.CreateHeader(PacketType.Request, FileTransferMessageType.Prepare), owner, payloadLength, cancellationToken).ConfigureAwait(false))
                    throw new IOException("Failed to send the prepare request.");
                return await source.Task.WaitAsync(_options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_syncRoot)
                    _prepareSource = null;
                _commandLock.Release();
            }
        }

        public async Task<FileTransferResult> DownloadAsync(FileTransferPrepareResult prepared, string destinationDirectory, FileTransferFlowMode flowMode = FileTransferFlowMode.Automatic, IProgress<FileTransferProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ArgumentNullException.ThrowIfNull(prepared);
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
            if (!prepared.CanStart)
                throw new InvalidOperationException("The prepared transfer has no ready files.");
            EnsureConnected();
            Directory.CreateDirectory(destinationDirectory);

            var download = new ActiveDownload(prepared, destinationDirectory, flowMode, progress, _options, SendContinueAsync);
            lock (_syncRoot)
            {
                if (_activeDownload is not null)
                    throw new InvalidOperationException("A file transfer is already active.");
                _activeDownload = download;
                _startSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            try
            {
                var (owner, payloadLength) = FileTransferProtocol.Write(writer =>
                {
                    writer.Write(prepared.TransferID.ToByteArray());
                    writer.Write((byte)flowMode);
                });
                if (!await Connection.SendOwnedDataAsync(FileTransferProtocol.CreateHeader(PacketType.Request, FileTransferMessageType.Start), owner, payloadLength, cancellationToken).ConfigureAwait(false))
                    throw new IOException("Failed to send the start request.");

                TaskCompletionSource<(FileTransferState State, string Message)> startSource;
                lock (_syncRoot)
                    startSource = _startSource!;
                var (state, message) = await startSource.Task.WaitAsync(_options.CommandTimeout, cancellationToken).ConfigureAwait(false);
                if (state == FileTransferState.Failed)
                    throw new InvalidOperationException(message);

                using CancellationTokenRegistration registration = cancellationToken.Register(() => _ = CancelAsync(prepared.TransferID));
                return await download.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                download.Fail(FileTransferState.Failed, "The download did not complete.");
                throw;
            }
            finally
            {
                lock (_syncRoot)
                {
                    _activeDownload = null;
                    _startSource = null;
                }
                download.Dispose();
            }
        }

        public async Task<bool> CancelAsync(Guid transferID, CancellationToken cancellationToken = default)
        {
            if (IsDisposed || !Connection.IsConnected || transferID == Guid.Empty)
                return false;
            var (owner, payloadLength) = FileTransferProtocol.Write(writer => writer.Write(transferID.ToByteArray()));
            return await Connection.SendOwnedDataAsync(FileTransferProtocol.CreateHeader(PacketType.Request, FileTransferMessageType.Cancel), owner, payloadLength, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
        #endregion

        #region Private Methods
        private static void ValidateOptions(FileTransferClientOptions options)
        {
            if (options.CommandTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "CommandTimeout must be greater than zero.");
        }

        private void EnsureConnected()
        {
            if (!Connection.IsConnected)
                throw new InvalidOperationException("The file transfer client is not connected.");
        }

        protected override void OnDisconnected(DisconnectedEventArgs e)
            => FailPending(new IOException($"The file transfer connection was disconnected. Reason={e.Reason}."));

        protected override void HandlePacket(PacketReceivedEventArgs e)
        {
            try
            {
                FileTransferMessageType messageType = (FileTransferMessageType)e.Header.PayloadType;
                if (e.Header.PacketType == PacketType.Response)
                {
                    if (messageType == FileTransferMessageType.Prepare)
                        ReadPrepareResponse(e.Payload);
                    else if (messageType == FileTransferMessageType.Start)
                        ReadStartResponse(e.Payload);
                    return;
                }
                if (e.Header.PacketType != PacketType.Notice)
                    return;
                ActiveDownload? download;
                lock (_syncRoot)
                    download = _activeDownload;
                if (download is null)
                    return;
                switch (messageType)
                {
                    case FileTransferMessageType.FileBegin:
                        download.Begin(e.Payload);
                        break;
                    case FileTransferMessageType.FileChunk:
                        download.Write(e.Payload.Span);
                        break;
                    case FileTransferMessageType.FileEnd:
                        download.End(e.Payload);
                        break;
                    case FileTransferMessageType.TransferEnd:
                        download.Complete(e.Payload);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process a file transfer packet.");
                lock (_syncRoot)
                    _activeDownload?.Fail(FileTransferState.Failed, ex.Message);
            }
        }

        private void ReadPrepareResponse(ReadOnlyMemory<byte> payload)
        {
            using BinaryReader reader = FileTransferProtocol.CreateReader(payload);
            Guid transferID = new(reader.ReadBytes(16));
            long totalLength = reader.ReadInt64();
            string message = FileTransferProtocol.ReadString(reader);
            int count = reader.ReadInt32();
            if (count is < 0 or > 10000)
                throw new InvalidDataException("Invalid prepared file count.");
            var files = new FileTransferPreparedFile[count];
            for (int i = 0; i < count; i++)
                files[i] = new(new Guid(reader.ReadBytes(16)), FileTransferProtocol.ReadString(reader), reader.ReadInt64(), reader.ReadInt64(), (FileTransferFileState)reader.ReadByte(), FileTransferProtocol.ReadString(reader));
            TaskCompletionSource<FileTransferPrepareResult>? source;
            lock (_syncRoot)
                source = _prepareSource;
            if (!string.IsNullOrEmpty(message) && files.Length == 0)
                source?.TrySetException(new InvalidOperationException(message));
            else
                source?.TrySetResult(new FileTransferPrepareResult(transferID, files, totalLength));
        }

        private void ReadStartResponse(ReadOnlyMemory<byte> payload)
        {
            using BinaryReader reader = FileTransferProtocol.CreateReader(payload);
            Guid transferID = new(reader.ReadBytes(16));
            FileTransferState state = (FileTransferState)reader.ReadByte();
            string message = FileTransferProtocol.ReadString(reader);
            TaskCompletionSource<(FileTransferState State, string Message)>? source;
            lock (_syncRoot)
                source = _activeDownload?.TransferID == transferID ? _startSource : null;
            source?.TrySetResult((state, message));
        }

        private async ValueTask SendContinueAsync(Guid transferID, Guid fileID)
        {
            var (owner, payloadLength) = FileTransferProtocol.Write(writer =>
            {
                writer.Write(transferID.ToByteArray());
                writer.Write(fileID.ToByteArray());
            });
            if (!await Connection.SendOwnedDataAsync(FileTransferProtocol.CreateHeader(PacketType.Request, FileTransferMessageType.Continue), owner, payloadLength).ConfigureAwait(false))
                throw new IOException("Failed to send the continue request.");
        }

        private void FailPending(Exception exception)
        {
            lock (_syncRoot)
            {
                _prepareSource?.TrySetException(exception);
                _startSource?.TrySetException(exception);
                _activeDownload?.Fail(FileTransferState.Failed, exception.Message);
            }
        }

        protected override void OnSessionDispose()
        {
            FailPending(new ObjectDisposedException(nameof(FileTransferClientSession)));
            _commandLock.Dispose();
        }
        #endregion

        private sealed class ActiveDownload : IDisposable
        {
            private readonly FileTransferPrepareResult _prepared;
            private readonly string _destinationDirectory;
            private readonly FileTransferFlowMode _flowMode;
            private readonly IProgress<FileTransferProgress>? _progress;
            private readonly FileTransferClientOptions _options;
            private readonly Func<Guid, Guid, ValueTask> _sendContinue;
            private readonly List<FileTransferDownloadedFile> _files = [];
            private FileStream? _stream;
            private IncrementalHash? _hash;
            private Guid _fileID;
            private string _sourceFileName = string.Empty;
            private string _destinationPath = string.Empty;
            private string _partialPath = string.Empty;
            private long _fileLength;
            private long _fileReceivedLength;
            private long _totalReceivedLength;
            private int _fileIndex;
            private int _fileCount;
            private int _endedReadyFileCount;
            private bool _disposed;

            public ActiveDownload(FileTransferPrepareResult prepared, string destinationDirectory, FileTransferFlowMode flowMode, IProgress<FileTransferProgress>? progress, FileTransferClientOptions options, Func<Guid, Guid, ValueTask> sendContinue)
            {
                _prepared = prepared;
                _destinationDirectory = Path.GetFullPath(destinationDirectory);
                _flowMode = flowMode;
                _progress = progress;
                _options = options;
                _sendContinue = sendContinue;
                Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                foreach (FileTransferPreparedFile file in prepared.Files.Where(x => x.State != FileTransferFileState.Ready))
                    _files.Add(new(file.FileID, file.FileName, string.Empty, 0, file.State, file.Message));
            }

            public Guid TransferID => _prepared.TransferID;
            public TaskCompletionSource<FileTransferResult> Completion { get; }

            public void Begin(ReadOnlyMemory<byte> payload)
            {
                CloseCurrent();
                using BinaryReader reader = FileTransferProtocol.CreateReader(payload);
                Guid transferID = new(reader.ReadBytes(16));
                Guid fileID = new(reader.ReadBytes(16));
                int fileIndex = reader.ReadInt32();
                int fileCount = reader.ReadInt32();
                string fileName = FileTransferProtocol.ReadString(reader);
                long fileLength = reader.ReadInt64();
                if (transferID != TransferID || fileID == Guid.Empty || fileLength < 0 || fileIndex <= 0 || fileCount <= 0)
                    throw new InvalidDataException("Invalid file begin notice.");
                string safeName = Path.GetFileName(fileName);
                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = fileID.ToString("N");
                _destinationPath = GetUniquePath(_destinationDirectory, safeName);
                _partialPath = _destinationPath + ".partial";
                _stream = new FileStream(_partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
                _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                _fileID = fileID;
                _sourceFileName = fileName;
                _fileLength = fileLength;
                _fileReceivedLength = 0;
                _fileIndex = fileIndex;
                _fileCount = fileCount;
                ReportProgress();
            }

            public void Write(ReadOnlySpan<byte> payload)
            {
                if (!FileTransferProtocol.TryReadChunk(payload, out Guid transferID, out Guid fileID, out long offset, out ReadOnlySpan<byte> data) || transferID != TransferID || fileID != _fileID || _stream is null || _hash is null || offset != _fileReceivedLength || _fileReceivedLength + data.Length > _fileLength)
                    throw new InvalidDataException("Invalid file chunk notice.");
                _stream.Write(data);
                _hash.AppendData(data);
                _fileReceivedLength += data.Length;
                _totalReceivedLength += data.Length;
                ReportProgress();
            }

            public void End(ReadOnlyMemory<byte> payload)
            {
                using BinaryReader reader = FileTransferProtocol.CreateReader(payload);
                Guid transferID = new(reader.ReadBytes(16));
                Guid fileID = new(reader.ReadBytes(16));
                FileTransferFileState state = (FileTransferFileState)reader.ReadByte();
                long transferredLength = reader.ReadInt64();
                int hashLength = reader.ReadByte();
                byte[] expectedHash = reader.ReadBytes(hashLength);
                string message = FileTransferProtocol.ReadString(reader);
                if (transferID != TransferID)
                    throw new InvalidDataException("Invalid file end notice.");

                if (_fileID == Guid.Empty)
                {
                    FileTransferPreparedFile preparedFile = _prepared.Files.FirstOrDefault(x => x.FileID == fileID)
                        ?? throw new InvalidDataException("The ended file was not part of the prepared transfer.");
                    if (preparedFile.State != FileTransferFileState.Ready || state == FileTransferFileState.Completed)
                        throw new InvalidDataException("Invalid file end state.");
                    _endedReadyFileCount++;
                    _files.Add(new(fileID, preparedFile.FileName, string.Empty, transferredLength, state, message));
                    int readyCount = _prepared.Files.Count(x => x.State == FileTransferFileState.Ready);
                    if (_flowMode == FileTransferFlowMode.WaitForClientContinue && _endedReadyFileCount < readyCount)
                        _sendContinue(TransferID, fileID).AsTask().GetAwaiter().GetResult();
                    return;
                }
                if (fileID != _fileID)
                    throw new InvalidDataException("Invalid file end notice.");

                string destinationPath = _destinationPath;
                if (state == FileTransferFileState.Completed)
                {
                    byte[] actualHash = _hash?.GetHashAndReset() ?? [];
                    _stream?.Flush(true);
                    CloseCurrent();
                    if (transferredLength != _fileReceivedLength || _fileReceivedLength != _fileLength || !CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                    {
                        state = FileTransferFileState.HashMismatch;
                        message = "The received file length or SHA-256 hash does not match.";
                        DeletePartialFile();
                        destinationPath = string.Empty;
                    }
                    else
                    {
                        File.Move(_partialPath, _destinationPath);
                    }
                }
                else
                {
                    CloseCurrent();
                    DeletePartialFile();
                    destinationPath = string.Empty;
                }
                _files.Add(new(fileID, _sourceFileName, destinationPath, _fileReceivedLength, state, message));
                _endedReadyFileCount++;
                _fileID = Guid.Empty;
                if (_flowMode == FileTransferFlowMode.WaitForClientContinue && _fileIndex < _fileCount)
                    _sendContinue(TransferID, fileID).AsTask().GetAwaiter().GetResult();
            }

            public void Complete(ReadOnlyMemory<byte> payload)
            {
                using BinaryReader reader = FileTransferProtocol.CreateReader(payload);
                Guid transferID = new(reader.ReadBytes(16));
                FileTransferState state = (FileTransferState)reader.ReadByte();
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                string message = FileTransferProtocol.ReadString(reader);
                if (transferID != TransferID)
                    throw new InvalidDataException("Invalid transfer end notice.");
                CloseCurrent();
                Completion.TrySetResult(new(TransferID, state, [.. _files], _totalReceivedLength, message));
            }

            public void Fail(FileTransferState state, string message)
            {
                CloseCurrent();
                DeletePartialFile();
                Completion.TrySetResult(new(TransferID, state, [.. _files], _totalReceivedLength, message));
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                CloseCurrent();
                if (!Completion.Task.IsCompleted)
                    DeletePartialFile();
            }

            private void ReportProgress()
                => _progress?.Report(new(TransferID, _fileID, _sourceFileName, _fileIndex, _fileCount, _fileReceivedLength, _fileLength, _totalReceivedLength, _prepared.TotalLength));

            private void CloseCurrent()
            {
                _stream?.Dispose();
                _stream = null;
                _hash?.Dispose();
                _hash = null;
            }

            private void DeletePartialFile()
            {
                if (!_options.DeletePartialFileOnFailure || string.IsNullOrEmpty(_partialPath))
                    return;
                try
                {
                    if (File.Exists(_partialPath))
                        File.Delete(_partialPath);
                }
                catch { }
            }

            private static string GetUniquePath(string directory, string fileName)
            {
                string path = Path.Combine(directory, fileName);
                if (!File.Exists(path) && !File.Exists(path + ".partial"))
                    return path;
                string name = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                for (int i = 1; ; i++)
                {
                    path = Path.Combine(directory, $"{name}_{i}{extension}");
                    if (!File.Exists(path) && !File.Exists(path + ".partial"))
                        return path;
                }
            }
        }
    }
}
