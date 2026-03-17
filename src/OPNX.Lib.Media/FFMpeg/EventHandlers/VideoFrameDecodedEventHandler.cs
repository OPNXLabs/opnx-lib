using FFmpeg.AutoGen;

namespace OPNX.Lib.Media.FFMpeg.EventHandlers
{
    public delegate void VideoFrameDecodedEventHandler(object sender, VideoFrameDecodedEventArgs e);


    public unsafe class VideoFrameDecodedEventArgs : EventArgs
    {
        #region Constructors
        public VideoFrameDecodedEventArgs(AVCodecID codecID, AVFrame* videoFrame)
        {
            CodecID = codecID;
            VideoFrame = videoFrame;
        }
        #endregion

        #region Properties
        public AVCodecID CodecID { get; set; }
        public AVFrame* VideoFrame { get; set; }
        #endregion
    }
}
