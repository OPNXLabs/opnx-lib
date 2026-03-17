using FFmpeg.AutoGen;

namespace OPNX.Lib.Media.FFMpeg.EventHandlers
{
    public delegate void VideoFrameEncodedEventHandler(object sender, VideoFrameEncodedEventArgs e);

    public unsafe class VideoFrameEncodedEventArgs : EventArgs
    {
        public VideoFrameEncodedEventArgs(AVPacket* encodedPacket)
        {
            EncodedPacket = encodedPacket;
        }
        public AVPacket* EncodedPacket { get; set; }
    }
}
