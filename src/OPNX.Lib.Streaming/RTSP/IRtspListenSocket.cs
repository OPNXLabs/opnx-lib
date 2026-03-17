namespace OPNX.Lib.Streaming.RTSP
{
    public interface IRtspListenSocket
    {
        /// <summary>
        /// Accept a new connection
        /// </summary>
        /// <returns>Connection accepeted</returns>
        //IRtspTransport Accept();

        /// <summary>
        /// Accept a new connection
        /// </summary>
        /// <returns></returns>
        Task<IRtspTransport> AcceptAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Start listening
        /// </summary>
        void Start();

        /// <summary>
        /// Stop listening
        /// </summary>
        void Stop();
    }
}
