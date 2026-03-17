using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace OPNX.Lib.Streaming.WebRTC
{
    public class WebRTCClient : WebSocketBehavior
    {
        #region Fields
        private RTCPeerConnectionEx peerConnection;
        #endregion

        #region Events
        public event Func<WebSocketContext, Task<RTCPeerConnectionEx>> SocketOpened;
        public event Action<WebSocketContext, RTCPeerConnectionEx, string> MessageReceived;
        public event Action<WebSocketContext, RTCPeerConnectionEx> SocketClosed;
        #endregion

        #region Constructors
        public WebRTCClient()
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
