using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Network.Abstractions.Events;
using OPNX.Lib.Network.Protocol.Tcp;

namespace OPNX.Lib.Network.Protocol.FileTransfer
{
    public enum FileTransferSessionRole
    {
        Client,
        Server
    }

    public interface IFileTransferSession : IDisposable
    {
        Guid SessionID { get; }
        bool IsConnected { get; }
        FileTransferSessionRole Role { get; }
        event EventHandler<DisconnectedEventArgs>? Disconnected;
    }

    public abstract class FileTransferSession : IFileTransferSession
    {
        #region Fields
        private readonly bool _ownsConnection;
        private readonly CancellationTokenSource _sessionCts = new();
        private bool _disposed;
        #endregion

        #region Constructors
        protected FileTransferSession(OPNXTcpClient connection, FileTransferSessionRole role, bool ownsConnection, ILogger? logger = null)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Role = role;
            _ownsConnection = ownsConnection;
            Logger = logger ?? NullLogger.Instance;
            Connection.PacketReceived += Connection_PacketReceived;
            Connection.Disconnected += Connection_Disconnected;
        }
        #endregion

        #region Properties
        protected OPNXTcpClient Connection { get; }
        protected ILogger Logger { get; }
        protected CancellationToken SessionToken => _sessionCts.Token;
        protected bool IsDisposed => _disposed;
        public Guid SessionID => Connection.SessionID;
        public bool IsConnected => Connection.IsConnected;
        public FileTransferSessionRole Role { get; }
        #endregion

        #region Events
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        #endregion

        #region Public Methods
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _sessionCts.Cancel();
            Connection.PacketReceived -= Connection_PacketReceived;
            Connection.Disconnected -= Connection_Disconnected;
            OnSessionDispose();
            if (_ownsConnection)
                Connection.Dispose();
            _sessionCts.Dispose();
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Private / Protected Methods
        protected abstract void HandlePacket(PacketReceivedEventArgs e);

        protected virtual void OnDisconnected(DisconnectedEventArgs e)
        {
        }

        protected virtual void OnSessionDispose()
        {
        }

        private void Connection_PacketReceived(object? sender, PacketReceivedEventArgs e)
        {
            if (!FileTransferProtocol.IsFileTransferMessage(e.Header.PayloadType))
                return;
            try
            {
                HandlePacket(e);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Invalid file transfer packet received. SessionID={SessionID}.", SessionID);
            }
        }

        private void Connection_Disconnected(object? sender, DisconnectedEventArgs e)
        {
            if (!_sessionCts.IsCancellationRequested)
                _sessionCts.Cancel();
            OnDisconnected(e);
            Disconnected?.Invoke(this, e);
        }
        #endregion
    }

    public static class FileTransferSessionFactory
    {
        public static FileTransferClientSession CreateClient(FileTransferClientOptions? options = null, ILoggerFactory? loggerFactory = null)
            => new FileTransferClientSession(options, loggerFactory);

        public static FileTransferClientSession CreateClient(OPNXTcpClient connection, FileTransferClientOptions? options = null, bool ownsConnection = false, ILoggerFactory? loggerFactory = null)
            => new FileTransferClientSession(connection, options, ownsConnection, loggerFactory);

        public static FileTransferServerSession CreateServer(OPNXTcpClient connection, FileTransferServerOptions? options = null, SemaphoreSlim? transferSlots = null, bool ownsConnection = false, ILoggerFactory? loggerFactory = null)
            => new FileTransferServerSession(connection, options, transferSlots, ownsConnection, loggerFactory);
    }
}
