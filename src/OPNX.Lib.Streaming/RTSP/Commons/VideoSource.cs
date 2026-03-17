using OPNX.Lib.Common.Primitives.Media;
using System.Runtime.Serialization;

namespace OPNX.Lib.Streaming.RTSP.Commons
{
    [DataContract]
    public class VideoSource
    {
        [DataMember(Name = "entityID", Order = 1)]
        public int EntityID { get; set; }

        [DataMember(Name = "rtspUrl", Order = 2)]
        public string RtspURL { get; set; }
        [DataMember(Name = "rtspID", Order = 3)]
        public string RtspID { get; set; }
        [DataMember(Name = "rtspPW", Order = 4)]
        public string RtspPW { get; set; }

        [DataMember(Name = "useTCP", Order = 5)]
        public bool UseTCP { get; set; } = true;

        [DataMember(Order = 6)]
        public VideoTrack Video { get; set; } = new VideoTrack();

        [DataMember(Order = 7)]
        public AudioTrack Audio { get; set; } = new AudioTrack();
    }

    [DataContract]
    public class VideoTrack
    {
        [DataMember(Order = 1)]
        public CodecId Codec { get; set; } = CodecId.H264;

        public int PayloadType => (int)Codec; // enum 값을 바로 PayloadType으로 사용
    }

    [DataContract]
    public class AudioTrack
    {
        [DataMember(Order = 1)]
        public CodecId Codec { get; set; } = CodecId.PCMU;

        public int PayloadType => (int)Codec;
    }
}
