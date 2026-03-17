namespace OPNX.Lib.Streaming.RTSP
{
    [Serializable]
    public class HttpBadResponseException : Exception
    {
        public HttpBadResponseException()
        {
        }

        public HttpBadResponseException(string message) : base(message)
        {
        }

        public HttpBadResponseException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
