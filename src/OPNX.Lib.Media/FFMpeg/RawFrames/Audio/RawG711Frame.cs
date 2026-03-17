namespace OPNX.Lib.Media.FFMpeg.RawFrames.Audio
{
    public abstract class RawG711Frame : RawAudioFrame
    {
        #region Constructors
        protected RawG711Frame(long timestamp, ReadOnlyMemory<byte> frameSegment)
            : base(timestamp, frameSegment)
        {
        }
        #endregion

        #region Properties
        public int SampleRate { get; set; } = 8000;
        public int Channels { get; set; } = 1;
        #endregion
    }
}