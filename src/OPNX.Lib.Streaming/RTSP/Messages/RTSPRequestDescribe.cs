namespace OPNX.Lib.Streaming.RTSP.Messages
{
    public class RtspRequestDescribe : RtspRequest
    {
        public RtspRequestDescribe()
        {
            Command = "DESCRIBE * RTSP/1.0";
        }
    }
}
