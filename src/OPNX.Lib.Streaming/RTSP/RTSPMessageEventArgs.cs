using OPNX.Lib.Streaming.RTSP.Messages;
using System.Net.Sockets;

namespace OPNX.Lib.Streaming.RTSP
{
    /// <summary>
    /// Event args containing information for message events.
    /// </summary>
    public class RTSPChunkEventArgs : EventArgs
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPChunkEventArgs"/> class.
        /// </summary>
        /// <param name="aMessage">A message.</param>
        public RTSPChunkEventArgs(RtspChunk aMessage)
        {
            Message = aMessage;
        }

        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public RtspChunk Message { get; set; }
    }

    public class RTSPSocketExceptionEventArgs : EventArgs
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPSocketExceptionEventArgs"/> class.
        /// </summary>
        /// <param name="socketException">A message.</param>
        public RTSPSocketExceptionEventArgs(SocketException socketException)
        {
            Ex = socketException;
        }

        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public SocketException Ex { get; set; }
    }
}
