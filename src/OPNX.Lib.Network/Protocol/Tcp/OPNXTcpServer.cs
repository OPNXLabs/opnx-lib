using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Network.Abstractions.Events;
using OPNX.Lib.Network.Transport.Tcp;

namespace OPNX.Lib.Network.Protocol.Tcp
{
    public class OPNXTcpServer : DisposableBase
    {
        #region Fields
        private readonly TcpAcceptor _tcpAcceptor;
        #endregion

        #region Constructors

        public OPNXTcpServer(string address, int port)
        {
            _tcpAcceptor = new TcpAcceptor(address, port);
            _tcpAcceptor.ClientAccepted += TcpAcceptor_ClientAccepted;
        }
        #endregion

        #region Events
        public event EventHandler<ClientAcceptedEventArgs>? ClientAccepted;
        #endregion

        #region Public Methods
        public void Start()
        {
            _tcpAcceptor.Start();
        }

        public void Stop()
        {
            _tcpAcceptor.Stop();
        }
        #endregion

        #region Private / Protected Methods
        private void TcpAcceptor_ClientAccepted(object? sender, ClientAcceptedEventArgs e)
        {
            ClientAccepted?.Invoke(this, new ClientAcceptedEventArgs(e.Client));
        }

        protected override void OnDispose()
        {
            base.OnDispose();

            _tcpAcceptor.ClientAccepted -= TcpAcceptor_ClientAccepted;
            _tcpAcceptor.Dispose();
        }
        #endregion
    }
}
