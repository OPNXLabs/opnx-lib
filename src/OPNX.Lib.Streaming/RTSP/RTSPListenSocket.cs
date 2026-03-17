using System.Net.Sockets;

namespace OPNX.Lib.Streaming.RTSP
{
    public class RtspListenSocket(TcpListener tcpListener) : IRtspListenSocket
    {
        private readonly TcpListener _tcpListener = tcpListener;
        //private readonly ILogger _logger;

        //public RtspListenSocket(TcpListener tcpListener, ILoggerFactory loggerFactory = null)
        //public RtspListenSocket
        //{
        //    _tcpListener = tcpListener;
        //    //_logger = loggerFactory?.CreateLogger<RtspListenSocket>() as ILogger ?? NullLogger.Instance;
        //}

        //public IRtspTransport Accept()
        //{
        //    var client = _tcpListener.AcceptTcpClient();
        //    return new RTSPTcpTransport(client);
        //}

        public async Task<IRtspTransport> AcceptAsync(CancellationToken cancellationToken)
        {
#if NET8_0_OR_GREATER
            var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
#else
            TcpClient client;
            using (cancellationToken.Register(() => _tcpListener.Stop()))
            {
                client = await _tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
#endif
            return new RTSPTcpTransport(client);
        }

        public void Start()
        {
            _tcpListener.Start();
        }

        public void Stop()
        {
            _tcpListener.Stop();
        }
    }
}
