using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace OPNX.Lib.Streaming.RTSP
{
    public class RtspTlsListenSocket(TcpListener tcpListener,
            X509Certificate2 certificate,
            RemoteCertificateValidationCallback? userCertificateValidationCallback = null) : IRtspListenSocket
    {
        private readonly TcpListener _tcpListener = tcpListener;
        private readonly X509Certificate2 _certificate = certificate;
        private readonly RemoteCertificateValidationCallback? _userCertificateValidationCallback = userCertificateValidationCallback;
        //private readonly ILogger _logger;

        //public RtspTlsListenSocket(TcpListener tcpListener,
        //    X509Certificate2 certificate, RemoteCertificateValidationCallback userCertificateValidationCallback = null,
        //    ILoggerFactory loggerFactory = null)
        //public RtspTlsListenSocket(TcpListener tcpListener,
        //    X509Certificate2 certificate, 
        //    RemoteCertificateValidationCallback userCertificateValidationCallback = null)            
        //{
        //    _tcpListener = tcpListener;
        //    //_logger = loggerFactory?.CreateLogger<RtspTlsListenSocket>() as ILogger ?? NullLogger.Instance;
        //    _certificate = certificate;
        //    _userCertificateValidationCallback = userCertificateValidationCallback;
        //}

        //public IRtspTransport Accept()
        //{
        //    var client = _tcpListener.AcceptTcpClient();
        //    return new RTSPTcpTlsTransport(client, _certificate, _userCertificateValidationCallback);
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
            return new RTSPTcpTlsTransport(client, _certificate, _userCertificateValidationCallback);
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
