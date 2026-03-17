using System.Net;

namespace OPNX.Lib.Streaming.RTSP
{
    public interface IRtspTransport : IDisposable
    {
        /// Gets the stream of the transport.
        /// </summary>
        /// <returns>A stream</returns>
        System.IO.Stream GetStream();

        /// <summary>
        /// Gets the remote endpoint.
        /// </summary>
        /// <value>The remote endpoint.</value>
        IPEndPoint RemoteEndPoint { get; }

        /// <summary>
        /// Gets the remote endpoint.
        /// </summary>
        /// <value>The remote endpoint.</value>
        IPEndPoint LocalEndPoint { get; }

        /// <summary>
        /// Get next command index. Increment at each call.
        /// </summary>
        uint NextCommandIndex();

        /// <summary>
        /// Closes this instance.
        /// </summary>
        void Close();

        /// <summary>
        /// Gets a value indicating whether this <see cref="IRtspTransport"/> is connected.
        /// </summary>
        /// <value><see langword="true"/> if connected; otherwise, <see langword="false"/>.</value>
        bool Connected { get; }

        /// <summary>
        /// Reconnect this instance.
        /// <remarks>Must do nothing if already connected.</remarks>
        /// </summary>
        void Reconnect();
    }
}
