namespace OPNX.Lib.Streaming.RTSP.Messages
{
    public class RtspRequestPlay : RtspRequest
    {
        public RtspRequestPlay()
        {
            Command = "PLAY * RTSP/1.0";
        }
    }
}
