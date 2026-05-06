namespace OPNX.Lib.Media.FFMpeg.RawFrames.Video
{
    public class RawH265PFrame(long timestamp, ReadOnlyMemory<byte> frameData, byte[]? rentedBuffer = null, byte[]? rentedParammeterSetBuffer = null)
        : RawH265Frame(timestamp, frameData, rentedBuffer, rentedParammeterSetBuffer)
    {

    }
}
