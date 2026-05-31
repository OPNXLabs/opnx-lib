using System.Diagnostics.Contracts;
using System.Net;
using System.Net.Sockets;

namespace OPNX.Lib.Streaming.RTSP
{
    /// <summary>
    /// TCP Connection for Rtsp
    /// </summary>
    public class RTSPTcpTransport : IRtspTransport
    {
        private readonly IPEndPoint _currentEndPoint;
        private readonly IPEndPoint _localEndPoint;
        private TcpClient? _rtspServerClient;
        private uint _commandCounter;

        /// <summary>
        /// Initializes a new instance of the <see cref="RtspTcpTransport"/> class.
        /// </summary>
        /// <param name="tcpConnection">The underlying TCP connection.</param>
        public RTSPTcpTransport(TcpClient tcpConnection)
        {
            ArgumentNullException.ThrowIfNull(tcpConnection);

            Contract.EndContractBlock();

            _currentEndPoint = tcpConnection.Client.RemoteEndPoint as IPEndPoint ?? throw new InvalidOperationException("The local endpoint can not be determined.");
            _localEndPoint = tcpConnection.Client.LocalEndPoint as IPEndPoint ?? throw new InvalidOperationException("The remote endpoint can not be determined.");
            _rtspServerClient = tcpConnection;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RtspTcpTransport"/> class.
        /// </summary>
        /// <param name="aHost">A host.</param>
        /// <param name="aPortNumber">A port number.</param>
        public RTSPTcpTransport(Uri uri)
           : this(new TcpClient(uri.Host, uri.Port))
        {
        }

        public static RTSPTcpTransport Create(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            TcpClient tcpClient = new();
            try
            {
                tcpClient.Connect(uri.Host, uri.Port);
                return new RTSPTcpTransport(tcpClient);
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }

        public static async Task<RTSPTcpTransport> CreateAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);

            TcpClient tcpClient = new();

            try
            {
                using (cancellationToken.Register(() => tcpClient.Dispose()))
                {
                    await tcpClient.ConnectAsync(uri.Host, uri.Port).ConfigureAwait(false);
                }
                return new RTSPTcpTransport(tcpClient);
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }

        #region IRtspTransport Membres

        /// <summary>
        /// Gets the stream of the transport.
        /// </summary>
        /// <returns>A stream</returns>
        public virtual Stream GetStream() => _rtspServerClient?.GetStream()
            ?? throw new ObjectDisposedException(nameof(RTSPTcpTransport));

        /// <summary>
        /// Gets the remote address.
        /// </summary>
        /// <value>The remote address.</value>
        public string RemoteAddress => _currentEndPoint.ToString();

        /// <summary>
        /// Gets the remote endpoint.
        /// </summary>
        /// <value>The remote endpoint.</value>
        public IPEndPoint RemoteEndPoint => _currentEndPoint;

        /// <summary>
        /// Gets the local endpoint.
        /// </summary>
        /// <value>The local endpoint.</value>
        public IPEndPoint LocalEndPoint => _localEndPoint;

        public uint NextCommandIndex() => ++_commandCounter;

        /// <summary>
        /// Closes this instance.
        /// </summary>
        public void Close()
        {
            Dispose(true);
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="IRtspTransport"/> is connected.
        /// </summary>
        /// <value><c>true</c> if connected; otherwise, <c>false</c>.</value>
        public bool Connected
        {
            get
            {
                if (_rtspServerClient == null)
                    return false;
                return _rtspServerClient.Client != null && _rtspServerClient.Connected;
            }
            //get
            //{
            //    if (_rtspServerClient?.Client == null)
            //        return false;

            //    Socket socket = _rtspServerClient.Client;

            //    try
            //    {
            //        return !(socket.Poll(1000, SelectMode.SelectRead) && socket.Available == 0);
            //    }
            //    catch (SocketException)
            //    {
            //        return false;
            //    }
            //}
        }

        /// <summary>
        /// Reconnect this instance.
        /// <remarks>Must do nothing if already connected.</remarks>
        /// </summary>
        /// <exception cref="System.Net.Sockets.SocketException">Error during socket </exception>
        public void Reconnect()
        {
            if (Connected)
                return;
            _rtspServerClient = new TcpClient();
            _rtspServerClient.Connect(_currentEndPoint);
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _rtspServerClient?.Close();
                _rtspServerClient?.Dispose();
                _rtspServerClient = null;
            }
        }
    }
}
