namespace OPNX.Lib.Streaming.WebRTC.Events
{
    public sealed class WebRtcClientOpenedEventArgs : EventArgs
    {
        public WebRtcClientOpenedEventArgs(Uri requestUri, Guid connectionID, int videoSourceID)
        {
            RequestUri = requestUri;
            ConnectionID = connectionID;
            VideoSourceID = videoSourceID;
        }

        public Uri RequestUri { get; }
        public Guid ConnectionID { get; }
        public int VideoSourceID { get; set; }
    }
}

