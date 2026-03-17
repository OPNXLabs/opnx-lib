using System.Net;

namespace OPNX.Lib.Network.Transport.NamedPipe
{
    public sealed class NamedPipeEndPoint(string pipeName) : EndPoint
    {
        public string PipeName { get; } = pipeName;

        public override string ToString() => PipeName;
    }
}
