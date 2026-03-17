using OPNX.Lib.Network.Abstractions.Events;
using System.IO.Pipelines;
using System.Net;

namespace OPNX.Lib.Network.Abstractions
{
    public interface IConnection : IDisposable, IAsyncDisposable
    {
        Guid SessionID { get; }
        bool IsConnected { get; }
        PipeReader? Reader { get; }
        PipeWriter? Writer { get; }

        public event EventHandler<ConnectedEventArgs> Connected;
        public event EventHandler<DisconnectedEventArgs> Disconnected;

        Task<bool> ConnectAsync(EndPoint endPoint, CancellationToken ct = default);
        bool Connect(EndPoint endPoint);

        Task DisconnectAsync(DisconnectReason reason = DisconnectReason.Requested, CancellationToken ct = default);
        void Disconnect(DisconnectReason reason = DisconnectReason.Requested);

    }
}
