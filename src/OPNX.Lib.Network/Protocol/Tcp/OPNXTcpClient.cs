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
        public OPNXTcpClient(TcpClient tcpClient)
            : this(tcpClient, TcpConnectionOptions.Default)
        {

        }

        public OPNXTcpClient(TcpClient tcpClient, TcpConnectionOptions connectionOptions)
            : this(new TcpConnection(connectionOptions), tcpClient)
        {

        }

        private OPNXTcpClient(TcpConnection tcpConnection, TcpClient tcpClient)
            : base(tcpConnection)
        {
            _tcpConnection = tcpConnection;
            _tcpConnection.Attach(tcpClient);
        }
        #endregion
    }
}
