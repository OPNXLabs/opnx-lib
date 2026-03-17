namespace OPNX.Lib.Common.Primitives.Media
{
    public static class CodecIdExtensions
    {
        public static bool IsVideo(this CodecId codec)
            => codec is CodecId.H264 or CodecId.H265 or CodecId.AV1 or CodecId.MJPEG;

        public static bool IsAudio(this CodecId codec)
            => codec is CodecId.AAC or CodecId.OPUS or CodecId.MP3 or CodecId.PCMU or CodecId.PCMA;
    }
}
