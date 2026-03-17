using FFmpeg.AutoGen;

namespace OPNX.Lib.Media.FFMpeg.Generic
{
    public unsafe struct OutputStream
    {
        public AVStream* st;
        public AVCodecContext* enc;

        public long next_pts;
        public int samples_count;

        public AVFrame* frame;
        public AVFrame* tmp_frame;

        public AVPacket* tmp_pkt;

        public float t;
        public float tincr;
        public float tincr2;

        public SwsContext* sws_ctx;
        public SwrContext* swr_ctx;
    }
}
