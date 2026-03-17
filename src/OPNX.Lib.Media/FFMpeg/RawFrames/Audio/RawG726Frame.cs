namespace OPNX.Lib.Media.FFMpeg.RawFrames.Audio
{
    public class RawG726Frame : RawAudioFrame
    {
        #region Constructors
        public RawG726Frame(long timestamp, ReadOnlyMemory<byte> frameSegment, int bitsPerCodedSample)
            : base(timestamp, frameSegment)
        {
            BitsPerCodedSample = bitsPerCodedSample;
        }
        #endregion

        #region Properties
        public int BitsPerCodedSample { get; }
        #endregion
    }
}