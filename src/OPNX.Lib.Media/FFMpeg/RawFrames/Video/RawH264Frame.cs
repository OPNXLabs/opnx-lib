namespace OPNX.Lib.Media.FFMpeg.RawFrames.Video
{
    public readonly struct H264ParameterSets
    {
        public ReadOnlyMemory<byte> SPS { get; init; }
        public ReadOnlyMemory<byte> PPS { get; init; }
        public ReadOnlyMemory<byte> Combined { get; init; }

        public bool IsValid => !SPS.IsEmpty && !PPS.IsEmpty;
    }

    public abstract class RawH264Frame(long timestamp, ReadOnlyMemory<byte> frameData, byte[]? rentedBuffer = null, byte[]? rentedParammeterSetBuffer = null)
        : RawVideoFrame(timestamp, frameData, rentedBuffer, rentedParammeterSetBuffer)
    {
        #region Fields
        public static readonly byte[] StartMarker = [ 0, 0, 0, 1 ];
        #endregion

        #region Properties
        public virtual bool IsIFrame { get; }
        #endregion
    }
}
