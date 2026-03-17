using System.Net.Sockets;

namespace OPNX.Lib.Network.Abstractions.Events
{
    public sealed class ClientAcceptedEventArgs(TcpClient tcpClient) : EventArgs
    {
        public TcpClient Client { get; } = tcpClient;
    }
}
