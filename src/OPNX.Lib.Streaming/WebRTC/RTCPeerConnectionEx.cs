using SIPSorcery.Net;

namespace OPNX.Lib.Streaming.WebRTC
{
    public class RTCPeerConnectionEx(RTCConfiguration configuration)
        : RTCPeerConnection(configuration)
    {
        #region Fields
        public int VideoSourceID = int.MinValue;
        #endregion

        #region Constructors
        public RTCPeerConnectionEx()
            : this(null)
        {
        }
        #endregion
    }
}
