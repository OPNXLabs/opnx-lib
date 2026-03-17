namespace OPNX.Lib.Media.FFMpeg.RawFrames
{
    public interface IRawFrame : IDisposable
    {
        long Timestamp { get; }
        ReadOnlyMemory<byte> FrameData { get; }
    }
}
