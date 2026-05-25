using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace OPNX.Lib.Streaming.WebRTC.Sipsorcery
{
    public class SipsorceryClientSession : WebSocketBehavior
    {
        #region Fields
        private SipsorceryPeerConnection peerConnection;
        #endregion

        #region Events
        public event Func<WebSocketContext, Task<SipsorceryPeerConnection>> SocketOpened;
        public event Action<WebSocketContext, SipsorceryPeerConnection, string> MessageReceived;
        public event Action<WebSocketContext, SipsorceryPeerConnection> SocketClosed;
        #endregion

        #region Constructors
        public SipsorceryClientSession()
        {

        }
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
            peerConnection = await SocketOpened(Context);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            var handler = SocketClosed;
            if (handler == null) return;
            SocketClosed(Context, peerConnection);
        }
        #endregion
    }
}

