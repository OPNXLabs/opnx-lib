namespace OPNX.Lib.Streaming.RTSP.Messages
{
    public class RtspRequestSetParameter : RtspRequest
    {
        public RtspRequestSetParameter()
        {
            Command = "SET_PARAMETER * RTSP/1.0";
        }
    }
}
