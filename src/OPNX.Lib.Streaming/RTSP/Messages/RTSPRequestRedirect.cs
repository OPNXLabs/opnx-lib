namespace OPNX.Lib.Streaming.RTSP.Messages
{
    public class RtspRequestRedirect : RtspRequest
    {
        public RtspRequestRedirect()
        {
            Command = "REDIRECT * RTSP/1.0";
        }
    }
}
