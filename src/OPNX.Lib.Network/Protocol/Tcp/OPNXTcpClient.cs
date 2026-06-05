using Microsoft.Extensions.Logging;
using OPNX.Lib.Network.Protocol.Abstractions;
using OPNX.Lib.Network.Transport.Tcp;
using System.Net.Sockets;

namespace OPNX.Lib.Network.Protocol.Tcp
{
    public class OPNXTcpClient : OPNXClientBase
    {
        #region Fields
        private readonly TcpConnection _tcpConnection;
        #endregion

        #region Constructors
        public OPNXTcpClient(TcpClient tcpClient, ILogger? logger = null)
            : this(tcpClient, TcpConnectionOptions.Default, logger)
        {
        }

        public OPNXTcpClient(TcpClient tcpClient, TcpConnectionOptions connectionOptions, ILogger? logger = null)
            : this(new TcpConnection(connectionOptions), tcpClient, logger)
        {
        }

        private OPNXTcpClient(TcpConnection tcpConnection, TcpClient tcpClient, ILogger? logger = null)
            : base(tcpConnection, logger: logger)
        {
            _tcpConnection = tcpConnection;
            if (tcpClient != null)
                _tcpConnection.Attach(tcpClient);
        }
        #endregion

        #region Public Methods
        public bool Attach(TcpClient tcpClient)
        {
            return _tcpConnection?.Attach(tcpClient) == true;
        }
        #endregion
    }
}
