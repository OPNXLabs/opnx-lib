namespace OPNX.Lib.Media.FFMpeg.RawFrames.Video
{
    public class RawJpegFrame : RawVideoFrame
    {
        #region Fields
        public static readonly byte[] StartMarkerBytes = { 0xFF, 0xD8 };
        public static readonly byte[] EndMarkerBytes = { 0xFF, 0xD9 };
        #endregion

        #region Constructors
        public RawJpegFrame(long timestamp, ReadOnlyMemory<byte> frameData)
            : base(timestamp, frameData)
        {
            IsKeyFrame = true;
        }
        #endregion
    }
}
