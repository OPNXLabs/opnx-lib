namespace OPNX.Lib.Streaming.RTSP.Messages
{
    public class RtspRequestRecord : RtspRequest
    {
        public RtspRequestRecord()
        {
            Command = "RECORD * RTSP/1.0";
        }
    }
}