namespace OPNX.Lib.Media.FFMpeg.RawFrames.Video
{
    public class RawH264PFrame(long timestamp, ReadOnlyMemory<byte> frameData, byte[]? rentedBuffer = null, byte[]? rentedParammeterSetBuffer = null)
        : RawH264Frame(timestamp, frameData, rentedBuffer, rentedParammeterSetBuffer)
    {
        
    }
}
