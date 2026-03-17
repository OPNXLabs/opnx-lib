using FFmpeg.AutoGen;

namespace OPNX.Lib.Media.FFMpeg.EventHandlers
{
    public delegate void AudioFrameEncodedEventHandler(object sender, AudioFrameEncodedEventArgs e);

    public unsafe class AudioFrameEncodedEventArgs : EventArgs
    {
        public AudioFrameEncodedEventArgs(AVPacket* encodedPacket)
        {
            EncodedPacket = encodedPacket;
        }
        public AVPacket* EncodedPacket { get; set; }
    }
}
