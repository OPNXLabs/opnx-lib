namespace OPNX.Lib.Media.FFMpeg.RawFrames.Audio
{
    public class RawAACFrame : RawAudioFrame
    {
        #region Constructors
        public RawAACFrame(long timestamp, ReadOnlyMemory<byte> frameBytes, ReadOnlyMemory<byte> configSegment)
            : base(timestamp, frameBytes)
        {
            ConfigSegment = configSegment;
        }
        #endregion

        #region Properties
        public ReadOnlyMemory<byte> ConfigSegment { get; }
        #endregion
    }
}