namespace OPNX.Lib.Streaming.RTSP.Commons.Interfaces
{
    public delegate byte[] RtspProvideSdpDataHandler(Guid connectionId, VideoSource videoSource);
    public delegate void RtspPlayRequestHandler(Guid connectionId);

    public delegate void RtspConnectionRemovedHandler(Guid connectionId, VideoSource videoSource);
    public delegate void RtspConnectionAddedHandler(Uri? requestUrl, Guid connectionId, ref VideoSource videoSource);

    public interface IRTSPServer : IDisposable
    {
        event RtspConnectionAddedHandler OnConnectionAdded;
        event RtspConnectionRemovedHandler OnConnectionRemoved;

        event RtspProvideSdpDataHandler OnProvideSdpData;
        //event RtspPlayRequestHandler OnPlay;
        //event RtspPlayRequestHandler OnStop;

        void SendRtpAudioData(Guid connectionId, ReadOnlySpan<byte> data);
        void SendRtpVideoData(Guid connectionId, ReadOnlySpan<byte> data);

        void ForceDisconnectPool(List<Guid> connectionIds);

    }
}
