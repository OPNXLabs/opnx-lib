using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace OPNX.Lib.Streaming.WebRTC.Sipsorcery
{
    public class SipsorceryClientSession : WebSocketBehavior
    {
        #region Fields
        private SipsorceryPeerConnection? peerConnection = null;
        #endregion

        #region Events
        public event Func<WebSocketContext, Task<SipsorceryPeerConnection?>>? SocketOpened;
        public event Action<WebSocketContext, SipsorceryPeerConnection?, string>? MessageReceived;
        public event Action<WebSocketContext, SipsorceryPeerConnection?>? SocketClosed;
        #endregion

        #region Private / Protected Methods
        protected override void OnMessage(MessageEventArgs e)
        {
            MessageReceived?.Invoke(Context, peerConnection, e.Data);
        }

        protected override async void OnOpen()
        {
            var handler = SocketOpened;
            if (handler == null) return;
            peerConnection = await handler(Context);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            var handler = SocketClosed;
            if (handler == null) return;
            handler(Context, peerConnection);
            peerConnection = null;
        }
        #endregion
    }
}

