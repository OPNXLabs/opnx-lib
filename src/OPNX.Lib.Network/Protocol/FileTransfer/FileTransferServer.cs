using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Network.Abstractions.Events;
using OPNX.Lib.Network.Protocol.Framing;
using OPNX.Lib.Network.Protocol.Tcp;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OPNX.Lib.Network.Protocol.FileTransfer
{
    public sealed class FileTransferServer : IDisposable
    {
        #region Fields
        private readonly OPNXTcpServer _server;
        private readonly FileTransferServerOptions _options;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<FileTransferServer> _logger;
        private readonly SemaphoreSlim _transferSlots;
        private readonly ConcurrentDictionary<Guid, FileTransferServerSession> _sessions = [];
        private bool _disposed;
        #endregion

        #region Constructors
        public FileTransferServer(int port, FileTransferServerOptions? options = null, ILoggerFactory? loggerFactory = null)
        {
            _options = options ?? new FileTransferServerOptions();
            ValidateOptions(_options);
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<FileTransferServer>();
            _transferSlots = new SemaphoreSlim(_options.MaximumConcurrentTransfers, _options.MaximumConcurrentTransfers);
            _server = new OPNXTcpServer(port, _loggerFactory);
            _server.ClientAccepted += Server_ClientAccepted;
        }
        #endregion

        #region Properties
        public int Port => _server.Port;
        public int ConnectionCount => _sessions.Count;
        #endregion

        #region Public Methods
        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _server.Start();
        }

        public void Stop()
        {
            if (_disposed)
                return;
            _server.Stop();
            foreach (var session in _sessions.Values)
                session.Dispose();
            _sessions.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _server.ClientAccepted -= Server_ClientAccepted;
            _server.Stop();
            foreach (var session in _sessions.Values)
                session.Dispose();
            _sessions.Clear();
            _server.Dispose();
            _transferSlots.Dispose();
        }
        #endregion

        #region Private Methods
        private static void ValidateOptions(FileTransferServerOptions options)
        {
            if (options.ChunkSize is < 64 * 1024 or > 4 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(options), "ChunkSize must be between 64 KiB and 4 MiB.");
            if (options.MaximumConcurrentTransfers <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaximumConcurrentTransfers must be greater than zero.");
            if (options.PrepareExpiration <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "PrepareExpiration must be greater than zero.");
        }

        private void Server_ClientAccepted(object? sender, ClientAcceptedEventArgs e)
        {
            try
            {
                var connection = new OPNXTcpClient(e.Client, _options.ConnectionOptions, _options.ProtocolOptions, _loggerFactory.CreateLogger<OPNXTcpClient>());
                var session = new FileTransferServerSession(connection, _options, _transferSlots, true, _loggerFactory);
                session.Disconnected += Session_Disconnected;
                if (!_sessions.TryAdd(connection.SessionID, session))
                    session.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize a file transfer client session.");
                e.Client.Dispose();
            }
        }

        private void Session_Disconnected(object? sender, DisconnectedEventArgs e)
        {
            if (_sessions.TryRemove(e.SessionID, out var session))
            {
                session.Disconnected -= Session_Disconnected;
                session.Dispose();
            }
        }
        #endregion
    }

    public sealed class FileTransferServerSession : FileTransferSession, IAsyncDisposable
    {
        private sealed record PreparedFile(Guid FileID, string FilePath, string FileName, long Length, long LastWriteTimeUnixMS, FileTransferFileState State, string Message);

        private readonly FileTransferServerOptions _options;
        private readonly SemaphoreSlim _transferSlots;
        private readonly bool _ownsTransferSlots;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _commandLock = new(1, 1);
        private readonly Lock _syncRoot = new();
        private Guid _transferID;
        private DateTime _preparedAtUtc;
        private PreparedFile[] _preparedFiles = [];
        private CancellationTokenSource? _transferCts;
        private Task _transferTask = Task.CompletedTask;
        private TaskCompletionSource<Guid>? _continueSource;
        private readonly Lock _operationSync = new();
        private readonly HashSet<Task> _operations = [];
        private Task? _stopTask;
        private bool _stopping;

        public FileTransferServerSession(OPNXTcpClient connection, FileTransferServerOptions? options = null, SemaphoreSlim? transferSlots = null, bool ownsConnection = false, ILoggerFactory? loggerFactory = null)
            : base(connection, FileTransferSessionRole.Server, ownsConnection, (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<FileTransferServerSession>())
        {
            _options = options ?? new FileTransferServerOptions();
            ValidateOptions(_options);
            _ownsTransferSlots = transferSlots is null;
            _transferSlots = transferSlots ?? new SemaphoreSlim(_options.MaximumConcurrentTransfers, _options.MaximumConcurrentTransfers);
            _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<FileTransferServerSession>();
        }

        public Task<FileTransferPrepareResult> RegisterTransferAsync(IEnumerable<FileTransferSource> files, CancellationToken cancellationToken = default)
            => TrackOperation(() => RegisterTransferCoreAsync(files, cancellationToken));

        private async Task<FileTransferPrepareResult> RegisterTransferCoreAsync(IEnumerable<FileTransferSource> files, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ArgumentNullException.ThrowIfNull(files);
            FileTransferSource[] sources = [.. files];
            if (sources.Length == 0)
                throw new ArgumentException("At least one file is required.", nameof(files));
            if (sources.Length > 10000)
                throw new ArgumentOutOfRangeException(nameof(files), "No more than 10000 files can be registered at once.");
            if (sources.Any(x => x.FileID == Guid.Empty || string.IsNullOrWhiteSpace(x.FilePath)))
                throw new ArgumentException("Every file must have a non-empty ID and path.", nameof(files));
            if (sources.Select(x => x.FileID).Distinct().Count() != sources.Length)
                throw new ArgumentException("File IDs must be unique within a transfer.", nameof(files));

            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(SessionToken, cancellationToken);
            await _commandLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                if (!_transferTask.IsCompleted)
                    throw new InvalidOperationException("A transfer is already active.");

                var prepared = new List<PreparedFile>(sources.Length);
                foreach (FileTransferSource source in sources)
                {
                    prepared.Add(await PrepareFileAsync(
                        new FileTransferRequestItem(source.FileID, source.FilePath),
                        linkedCts.Token).ConfigureAwait(false));
                }

                Guid transferID = Guid.NewGuid();
                PreparedFile[] preparedFiles = [.. prepared];
                SetPreparedTransfer(transferID, preparedFiles);
                return CreatePrepareResult(transferID, preparedFiles);
            }
            finally
            {
                _commandLock.Release();
            }
        }

        protected override void OnSessionDispose()
        {
            StopAsync().GetAwaiter().GetResult();
            lock (_syncRoot)
            {
                _transferCts?.Cancel();
                _continueSource?.TrySetCanceled();
                ClearPreparedTransferCore();
            }
            _commandLock.Dispose();
            if (_ownsTransferSlots)
                _transferSlots.Dispose();
        }

        protected override void OnDisconnected(DisconnectedEventArgs e)
        {
            lock (_syncRoot)
            {
                _transferCts?.Cancel();
                _continueSource?.TrySetCanceled();
                ClearPreparedTransferCore();
            }
        }

        protected override void HandlePacket(PacketReceivedEventArgs e)
        {
            if (e.Header.PacketType != PacketType.Request)
                return;
            try
            {
                switch ((FileTransferMessageType)e.Header.PayloadType)
                {
                    case FileTransferMessageType.Prepare:
                        var (prepareTransferID, files) = ReadPrepareRequest(e.Payload);
                        _ = TrackOperation(() => PrepareAsync(prepareTransferID, files));
                        break;
                    case FileTransferMessageType.Start:
                        var (startTransferID, flowMode) = ReadStartRequest(e.Payload);
                        _ = TrackOperation(() => StartAsync(startTransferID, flowMode));
                        break;
                    case FileTransferMessageType.Continue:
                        var (continueTransferID, fileID) = ReadContinueRequest(e.Payload);
                        Continue(continueTransferID, fileID);
                        break;
                    case FileTransferMessageType.Cancel:
                        Guid cancelTransferID = ReadTransferID(e.Payload);
                        _ = TrackOperation(() => CancelAsync(cancelTransferID));
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid file transfer request received.");
            }
        }

        public Task StopAsync()
        {
            lock (_operationSync)
            {
                if (_stopTask is not null)
                    return _stopTask;
                _stopping = true;
                return _stopTask = StopCoreAsync(_operations.ToArray());
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            Dispose();
        }

        private async Task StopCoreAsync(Task[] operations)
        {
            CancelSession();
            try
            {
                await Task.WhenAll(operations).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A file transfer command failed during shutdown.");
            }
            try
            {
                await _transferTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A file transfer failed during shutdown.");
            }
        }

        private Task TrackOperation(Func<Task> operation)
        {
            lock (_operationSync)
            {
                ObjectDisposedException.ThrowIf(_stopping || IsDisposed, this);
                Task task = operation();
                ObserveOperation(task);
                return task;
            }
        }

        private Task<T> TrackOperation<T>(Func<Task<T>> operation)
        {
            lock (_operationSync)
            {
                ObjectDisposedException.ThrowIf(_stopping || IsDisposed, this);
                Task<T> task = operation();
                ObserveOperation(task);
                return task;
            }
        }

        private void ObserveOperation(Task task)
        {
            _operations.Add(task);
            _ = task.ContinueWith(completed =>
            {
                lock (_operationSync)
                    _operations.Remove(completed);
                if (completed.Exception is { } exception)
                    _logger.LogWarning(exception, "A file transfer command failed.");
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task PrepareAsync(Guid transferID, FileTransferRequestItem[] files)
        {
            await _commandLock.WaitAsync(SessionToken).ConfigureAwait(false);
            try
            {
                if (!_transferTask.IsCompleted)
                {
                    await SendPrepareResponseAsync(transferID, [], 0, "A transfer is already active.", SessionToken).ConfigureAwait(false);
                    return;
                }
                var prepared = new List<PreparedFile>(files.Length);
                foreach (var file in files)
                    prepared.Add(await PrepareFileAsync(file, SessionToken).ConfigureAwait(false));
                PreparedFile[] preparedFiles = [.. prepared];
                SetPreparedTransfer(transferID, preparedFiles);
                FileTransferPrepareResult result = CreatePrepareResult(transferID, preparedFiles);
                await SendPrepareResponseAsync(transferID, preparedFiles, result.TotalLength, string.Empty, SessionToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (SessionToken.IsCancellationRequested) { }
            finally
            {
                _commandLock.Release();
            }
        }

        private async ValueTask<PreparedFile> PrepareFileAsync(FileTransferRequestItem request, CancellationToken cancellationToken)
        {
            string filePath;
            try
            {
                if (string.IsNullOrWhiteSpace(request.FilePath) || !Path.IsPathFullyQualified(request.FilePath))
                    return new(request.FileID, string.Empty, string.Empty, 0, 0, FileTransferFileState.InvalidPath, "A fully qualified file path is required.");
                filePath = Path.GetFullPath(request.FilePath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return new(request.FileID, string.Empty, string.Empty, 0, 0, FileTransferFileState.InvalidPath, ex.Message);
            }
            if (!IsAllowedPath(filePath))
                return new(request.FileID, filePath, Path.GetFileName(filePath), 0, 0, FileTransferFileState.AccessDenied, "The file is outside the allowed roots.");
            if (_options.AuthorizeFileAsync is not null && !await _options.AuthorizeFileAsync(filePath, cancellationToken).ConfigureAwait(false))
                return new(request.FileID, filePath, Path.GetFileName(filePath), 0, 0, FileTransferFileState.AccessDenied, "File access was denied.");
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists)
                    return new(request.FileID, filePath, info.Name, 0, 0, FileTransferFileState.NotFound, "File not found.");
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
                return new(request.FileID, filePath, info.Name, info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds(), FileTransferFileState.Ready, string.Empty);
            }
            catch (IOException ex)
            {
                return new(request.FileID, filePath, Path.GetFileName(filePath), 0, 0, FileTransferFileState.FileInUse, ex.Message);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return new(request.FileID, filePath, Path.GetFileName(filePath), 0, 0, FileTransferFileState.AccessDenied, ex.Message);
            }
        }

        private bool IsAllowedPath(string filePath)
        {
            foreach (string rootPath in _options.AllowedRootPaths)
            {
                if (string.IsNullOrWhiteSpace(rootPath))
                    continue;
                string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (filePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private async Task StartAsync(Guid transferID, FileTransferFlowMode flowMode)
        {
            await _commandLock.WaitAsync(SessionToken).ConfigureAwait(false);
            try
            {
                string error = string.Empty;
                FileTransferState state = FileTransferState.Queued;
                if (!_transferTask.IsCompleted)
                {
                    state = FileTransferState.Failed;
                    error = "A transfer is already active.";
                }
                else if (transferID != _transferID || DateTime.UtcNow - _preparedAtUtc > _options.PrepareExpiration || !_preparedFiles.Any(x => x.State == FileTransferFileState.Ready))
                {
                    state = FileTransferState.Failed;
                    error = "The prepared transfer is missing, expired, or has no ready files.";
                    ClearPreparedTransfer(transferID);
                }
                await SendStateResponseAsync(FileTransferMessageType.Start, transferID, state, error, SessionToken).ConfigureAwait(false);
                if (state == FileTransferState.Failed)
                    return;
                _transferCts = CancellationTokenSource.CreateLinkedTokenSource(SessionToken);
                _transferTask = TransferAsync(transferID, flowMode, _preparedFiles, _transferCts.Token);
            }
            catch (OperationCanceledException) when (SessionToken.IsCancellationRequested) { }
            finally
            {
                _commandLock.Release();
            }
        }

        private async Task TransferAsync(Guid transferID, FileTransferFlowMode flowMode, PreparedFile[] files, CancellationToken cancellationToken)
        {
            int completedCount = 0;
            int failedCount = files.Count(x => x.State != FileTransferFileState.Ready);
            bool slotAcquired = false;
            try
            {
                await _transferSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                slotAcquired = true;
                int fileIndex = 0;
                int readyCount = files.Count(x => x.State == FileTransferFileState.Ready);
                foreach (PreparedFile file in files.Where(x => x.State == FileTransferFileState.Ready))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    fileIndex++;
                    TaskCompletionSource<Guid>? continueSource = flowMode == FileTransferFlowMode.WaitForClientContinue && fileIndex < readyCount
                        ? CreateContinueSource()
                        : null;
                    FileTransferFileState result = await TransferFileAsync(transferID, file, fileIndex, readyCount, cancellationToken).ConfigureAwait(false);
                    if (result == FileTransferFileState.Completed)
                        completedCount++;
                    else
                        failedCount++;
                    if (continueSource is not null)
                        await WaitForContinueAsync(file.FileID, continueSource, cancellationToken).ConfigureAwait(false);
                }
                FileTransferState state = failedCount == 0 ? FileTransferState.Completed : completedCount > 0 ? FileTransferState.PartiallyCompleted : FileTransferState.Failed;
                await SendTransferEndAsync(transferID, state, completedCount, failedCount, string.Empty, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await SendTransferEndAsync(transferID, FileTransferState.Canceled, completedCount, failedCount, "Transfer canceled.", SessionToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File transfer failed. TransferID={TransferID}.", transferID);
                await SendTransferEndAsync(transferID, FileTransferState.Failed, completedCount, failedCount + 1, ex.Message, SessionToken).ConfigureAwait(false);
            }
            finally
            {
                if (slotAcquired)
                    _transferSlots.Release();
                lock (_syncRoot)
                {
                    _transferCts?.Dispose();
                    _transferCts = null;
                    _continueSource = null;
                    ClearPreparedTransferCore(transferID);
                }
            }
        }

        private async Task<FileTransferFileState> TransferFileAsync(Guid transferID, PreparedFile file, int fileIndex, int fileCount, CancellationToken cancellationToken)
        {
            try
            {
                var info = new FileInfo(file.FilePath);
                info.Refresh();
                long lastWriteUnixMS = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                if (!info.Exists || info.Length != file.Length || lastWriteUnixMS != file.LastWriteTimeUnixMS)
                {
                    await SendFileEndAsync(transferID, file.FileID, FileTransferFileState.Changed, 0, [], "File changed after prepare.", cancellationToken).ConfigureAwait(false);
                    return FileTransferFileState.Changed;
                }
                await SendFileBeginAsync(transferID, file, fileIndex, fileCount, cancellationToken).ConfigureAwait(false);
                using var stream = new FileStream(file.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, _options.ChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(_options.ChunkSize);
                long offset = 0;
                try
                {
                    while (offset < file.Length)
                    {
                        int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, file.Length - offset)), cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                            throw new EndOfStreamException();
                        hash.AppendData(buffer, 0, read);
                        var (owner, payloadLength) = FileTransferProtocol.WriteChunk(transferID, file.FileID, offset, buffer.AsSpan(0, read));
                        bool sent = await Connection.SendOwnedDataAsync(FileTransferProtocol.CreateHeader(PacketType.Notice, FileTransferMessageType.FileChunk), owner, payloadLength, cancellationToken).ConfigureAwait(false);
                        if (!sent)
                            throw new IOException("Failed to queue a file chunk.");
                        offset += read;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                byte[] digest = hash.GetHashAndReset();
                await SendFileEndAsync(transferID, file.FileID, FileTransferFileState.Completed, offset, digest, string.Empty, cancellationToken).ConfigureAwait(false);
                return FileTransferFileState.Completed;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await SendFileEndAsync(transferID, file.FileID, FileTransferFileState.ReadFailed, 0, [], ex.Message, cancellationToken).ConfigureAwait(false);
                return FileTransferFileState.ReadFailed;
            }
        }

        private TaskCompletionSource<Guid> CreateContinueSource()
        {
            lock (_syncRoot)
            {
                TaskCompletionSource<Guid> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _continueSource = source;
                return source;
            }
        }

        private static async Task WaitForContinueAsync(Guid fileID, TaskCompletionSource<Guid> source, CancellationToken cancellationToken)
        {
            Guid continuedFileID = await source.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (continuedFileID != fileID)
                throw new InvalidDataException("Unexpected file continuation received.");
        }

        private void Continue(Guid transferID, Guid fileID)
        {
            if (transferID != _transferID)
                return;
            lock (_syncRoot)
                _continueSource?.TrySetResult(fileID);
        }

        private async Task CancelAsync(Guid transferID)
        {
            if (transferID != _transferID)
                return;
            lock (_syncRoot)
            {
                _transferCts?.Cancel();
                ClearPreparedTransferCore(transferID);
            }
            try
            {
                if (!await SendStateResponseAsync(FileTransferMessageType.Cancel, transferID, FileTransferState.CancelRequested, string.Empty, SessionToken).ConfigureAwait(false))
                    _logger.LogWarning("Failed to send the file transfer cancel response. TransferID={TransferID}.", transferID);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send the file transfer cancel response. TransferID={TransferID}.", transferID);
            }
        }

        private ValueTask<bool> SendPrepareResponseAsync(Guid transferID, IReadOnlyList<PreparedFile> files, long totalLength, string message, CancellationToken cancellationToken)
        {
            var (owner, payloadLength) = FileTransferProtocol.Write(writer =>
            {
                writer.Write(transferID.ToByteArray());
                writer.Write(totalLength);
                FileTransferProtocol.WriteString(writer, message);
                writer.Write(files.Count);
                foreach (var file in files)
                {
                    writer.Write(file.FileID.ToByteArray());
                    FileTransferProtocol.WriteString(writer, file.FileName);
                    writer.Write(file.Length);
                    writer.Write(file.LastWriteTimeUnixMS);
                    writer.Write((byte)file.State);
                    FileTransferProtocol.WriteString(writer, file.Message);
                }
            });
            return Connection.SendOwnedDataAsync(FileTransferProtocol.CreateHeader(PacketType.Response, FileTransferMessageType.Prepare), owner, payloadLength, cancellationToken);
        }

        private ValueTask<bool> SendStateResponseAsync(FileTransferMessageType messageType, Guid transferID, FileTransferState state, string message, CancellationToken cancellationToken)
        {
            var (owner, payloadLength) = FileTransferProtocol.Write(writer =>
            {
                writer.Write(transferID.ToByteArray());
                writer.Write((byte)state);
                FileTransferProtocol.WriteString(writer, message);
            });
            return Connection.SendOwnedDataAsync(FileTransferProtocol.CreateHeader(PacketType.Response, messageType), owner, payloadLength, cancellationToken);
        }

        private ValueTask<bool> SendFileBeginAsync(Guid transferID, PreparedFile file, int fileIndex, int fileCount, CancellationToken cancellationToken)
        {
            var (owner, payloadLength) = FileTransferProtocol.Write(writer =>
            {
                writer.Write(transferID.ToByteArray());
                writer.Write(file.FileID.ToByteArray());
                writer.Write(fileIndex);
                writer.Write(fileCount);
                FileTransferProtocol.WriteString(writer, file.FileName);
                writer.Write(file.Length);
            });
            return Connection.SendOwnedDataAsync(FileTransferProtocol.CreateHeader(PacketType.Notice, FileTransferMessageType.FileBegin), owner, payloadLength, cancellationToken);
        }

        private ValueTask<bool> SendFileEndAsync(Guid transferID, Guid fileID, FileTransferFileState state, long transferredLength, byte[] hash, string message, CancellationToken cancellationToken)
        {
            var (owner, payloadLength) = FileTransferProtocol.Write(writer =>
            {
                writer.Write(transferID.ToByteArray());
                writer.Write(fileID.ToByteArray());
                writer.Write((byte)state);
                writer.Write(transferredLength);
                writer.Write((byte)hash.Length);
                writer.Write(hash);
                FileTransferProtocol.WriteString(writer, message);
            });
            return Connection.SendOwnedDataAsync(FileTransferProtocol.CreateHeader(PacketType.Notice, FileTransferMessageType.FileEnd), owner, payloadLength, cancellationToken);
        }

        private ValueTask<bool> SendTransferEndAsync(Guid transferID, FileTransferState state, int completedCount, int failedCount, string message, CancellationToken cancellationToken)
        {
            var (owner, payloadLength) = FileTransferProtocol.Write(writer =>
            {
                writer.Write(transferID.ToByteArray());
                writer.Write((byte)state);
                writer.Write(completedCount);
                writer.Write(failedCount);
                FileTransferProtocol.WriteString(writer, message);
            });
            return Connection.SendOwnedDataAsync(FileTransferProtocol.CreateHeader(PacketType.Notice, FileTransferMessageType.TransferEnd), owner, payloadLength, cancellationToken);
        }

        private static (Guid TransferID, FileTransferRequestItem[] Files) ReadPrepareRequest(ReadOnlyMemory<byte> payload)
        {
            using BinaryReader reader = FileTransferProtocol.CreateReader(payload);
            Guid transferID = new(reader.ReadBytes(16));
            int count = reader.ReadInt32();
            if (count is < 0 or > 10000)
                throw new InvalidDataException("Invalid file count.");
            var files = new FileTransferRequestItem[count];
            for (int i = 0; i < count; i++)
                files[i] = new FileTransferRequestItem(new Guid(reader.ReadBytes(16)), FileTransferProtocol.ReadString(reader));
            return (transferID, files);
        }

        private static (Guid TransferID, FileTransferFlowMode FlowMode) ReadStartRequest(ReadOnlyMemory<byte> payload)
        {
            using BinaryReader reader = FileTransferProtocol.CreateReader(payload);
            return (new Guid(reader.ReadBytes(16)), (FileTransferFlowMode)reader.ReadByte());
        }

        private static (Guid TransferID, Guid FileID) ReadContinueRequest(ReadOnlyMemory<byte> payload)
        {
            using BinaryReader reader = FileTransferProtocol.CreateReader(payload);
            return (new Guid(reader.ReadBytes(16)), new Guid(reader.ReadBytes(16)));
        }

        private static Guid ReadTransferID(ReadOnlyMemory<byte> payload)
        {
            if (payload.Length != 16)
                throw new InvalidDataException("Invalid transfer ID payload.");
            return new Guid(payload.Span);
        }

        private static FileTransferPrepareResult CreatePrepareResult(Guid transferID, IReadOnlyList<PreparedFile> files)
        {
            FileTransferPreparedFile[] preparedFiles =
            [
                .. files.Select(x => new FileTransferPreparedFile(
                    x.FileID,
                    x.FileName,
                    x.Length,
                    x.LastWriteTimeUnixMS,
                    x.State,
                    x.Message))
            ];
            long totalLength = preparedFiles.Where(x => x.State == FileTransferFileState.Ready).Sum(x => x.Length);
            return new FileTransferPrepareResult(transferID, preparedFiles, totalLength);
        }

        private void SetPreparedTransfer(Guid transferID, PreparedFile[] files)
        {
            lock (_syncRoot)
            {
                ClearPreparedTransferCore();
                _transferID = transferID;
                _preparedAtUtc = DateTime.UtcNow;
                _preparedFiles = files;
            }
        }

        private void ClearPreparedTransfer(Guid transferID)
        {
            lock (_syncRoot)
                ClearPreparedTransferCore(transferID);
        }

        private void ClearPreparedTransferCore(Guid? transferID = null)
        {
            if (transferID.HasValue && _transferID != transferID.Value)
                return;
            _transferID = Guid.Empty;
            _preparedAtUtc = default;
            _preparedFiles = [];
        }

        private static void ValidateOptions(FileTransferServerOptions options)
        {
            if (options.ChunkSize is < 64 * 1024 or > 4 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(options), "ChunkSize must be between 64 KiB and 4 MiB.");
            if (options.MaximumConcurrentTransfers <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaximumConcurrentTransfers must be greater than zero.");
            if (options.PrepareExpiration <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "PrepareExpiration must be greater than zero.");
        }
    }
}
