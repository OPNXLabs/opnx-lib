using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace OPNX.Lib.Streaming.WebRTC.DataChannel
{
    public class DataChannelClientSession : WebSocketBehavior
    {
        private DataChannelPeerConnection? peerConnection;

        public event Func<WebSocketContext, Task<DataChannelPeerConnection?>>? SocketOpened;
        public event Action<WebSocketContext, DataChannelPeerConnection?, string>? MessageReceived;
        public event Action<WebSocketContext, DataChannelPeerConnection?>? SocketClosed;

        protected override void OnMessage(MessageEventArgs e)
        {
            MessageReceived?.Invoke(Context, peerConnection, e.Data);
        }

        protected override async void OnOpen()
        {
            var handler = SocketOpened;
            if (handler == null) return;
            peerConnection = await handler(Context).ConfigureAwait(false);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            SocketClosed?.Invoke(Context, peerConnection);
        }
    }
}
