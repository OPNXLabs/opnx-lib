namespace OPNX.Lib.Media.FFMpeg.RawFrames.Audio
{
    public class RawG711AFrame : RawG711Frame
    {
        #region Constructors
        public RawG711AFrame(long timestamp, ReadOnlyMemory<byte> frameSegment)
            : base(timestamp, frameSegment)
        {
        }
        #endregion
    }
}