using FFmpeg.AutoGen;

namespace OPNX.Lib.Media.FFMpeg.EventHandlers
{
    public delegate void AudioFrameDecodedEventHandler(object sender, AudioFrameDecodedEventArgs e);

    public unsafe class AudioFrameDecodedEventArgs(AVCodecID codecID, AVFrame* audioFrame) : EventArgs
    {
        #region Properties
        public AVCodecID CodecID { get; set; } = codecID;
        public AVFrame* AudioFrame { get; set; } = audioFrame;
        #endregion
    }
}
