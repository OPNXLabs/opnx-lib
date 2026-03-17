using OPNX.Lib.Network.Protocol.Abstractions;
using OPNX.Lib.Network.Transport.NamedPipe;

namespace OPNX.Lib.Network.Protocol.NamedPipe
{
    public class OPNXNPipeClient(NamedPipeConnection nPipeConnection)
        : OPNXClientBase(nPipeConnection)
    {
        #region Fields
        private readonly NamedPipeConnection _nPipeConnection = nPipeConnection;
        #endregion

        #region Constructors
        public OPNXNPipeClient()
            : this(NamedPipeConnectionOptions.Default)
        {

        }
        public OPNXNPipeClient(NamedPipeConnectionOptions connectionOptions)
            : this(new NamedPipeConnection(connectionOptions))
        {
        }
        #endregion
    }
}
