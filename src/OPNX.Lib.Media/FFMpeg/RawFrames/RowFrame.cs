namespace OPNX.Lib.Media.FFMpeg.RawFrames
{
    public abstract class RawFrame : IRawFrame
    {
        protected bool isDisposed = false;

        public long Timestamp { get; }
        //public ArraySegment<byte> FrameSegment { get; }
        public ReadOnlyMemory<byte> FrameData { get; }
        public abstract FrameType Type { get; }

        protected RawFrame(long timestamp, ReadOnlyMemory<byte> frameData)
        {
            Timestamp = timestamp;
            FrameData = frameData;
        }

        protected virtual void OnDispose()
        {
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;

            OnDispose();
        }
    }
}
