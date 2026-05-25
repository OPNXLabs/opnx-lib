using SIPSorcery.Net;

namespace OPNX.Lib.Streaming.WebRTC.Sipsorcery
{
    public class SipsorceryPeerConnection(RTCConfiguration? configuration)
        : RTCPeerConnection(configuration)
    {
        #region Fields
        public int VideoSourceID = int.MinValue;
        #endregion

        #region Constructors
        public SipsorceryPeerConnection()
            : this(null)
        {
        }
        #endregion
    }
}

