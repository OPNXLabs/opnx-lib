namespace OPNX.Lib.Media.FFMpeg.RawFrames.Audio
{
    public class RawG711UFrame : RawG711Frame
    {
        #region Constructors
        public RawG711UFrame(long timestamp, ReadOnlyMemory<byte> frameSegment)
            : base(timestamp, frameSegment)
        {
        }
        #endregion
    }
}