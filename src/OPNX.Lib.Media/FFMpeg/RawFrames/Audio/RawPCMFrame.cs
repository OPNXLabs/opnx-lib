namespace OPNX.Lib.Media.FFMpeg.RawFrames.Audio
{
    public class RawPCMFrame : RawAudioFrame
    {
        #region Constructors
        public RawPCMFrame(long timestamp, ReadOnlyMemory<byte> frameSegment, int sampleRate, int bitsPerSample, int channels)
            : base(timestamp, frameSegment)
        {
            SampleRate = sampleRate;
            BitsPerSample = bitsPerSample;
            Channels = channels;
        }
        #endregion

        #region Properties
        public int SampleRate { get; }
        public int BitsPerSample { get; }
        public int Channels { get; }
        #endregion
    }
}