namespace OPNX.Lib.Streaming.RTSP.Commons.Interfaces
{
    public enum RTSPClientStopReason
    {
        COMMAND = 0,
        CONNECTION_FAILED = 1,
        CONNECTION_LOST = 2,
        AUTHORIZATION_FAILED = 3,
        SESSION_FAILED = 4,
        SESSION_CLOSED = 5,
        TRANSPORT_FAILED = 6
    }

    public interface IRTSPClient
    {
        int EntityID { get; }

        string URL { get; }

        double FPS { get; }
        double BitRate { get; }

        void Start();

        bool Stop(RTSPClientStopReason reason = RTSPClientStopReason.COMMAND);
    }
}
