namespace OPNX.Lib.Media.FFMpeg.RawFrames.Audio
{
    public abstract class RawAudioFrame : RawFrame
    {
        #region Constructors
        protected RawAudioFrame(long timestamp, ReadOnlyMemory<byte> frameSegment)
            : base(timestamp, frameSegment)
        {
        }
        #endregion

        #region Properties
        public override FrameType Type => FrameType.Audio;
        #endregion
    }
}