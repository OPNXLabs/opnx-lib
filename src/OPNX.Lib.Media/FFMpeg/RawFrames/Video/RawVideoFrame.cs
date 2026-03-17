using System.Buffers;

namespace OPNX.Lib.Media.FFMpeg.RawFrames.Video
{
    public abstract class RawVideoFrame(long timestamp, ReadOnlyMemory<byte> frameData, byte[]? rentedBuffer = null, byte[]? rentedParammeterSetBuffer = null) 
        : RawFrame(timestamp, frameData)
    {
        #region Fields
        public static readonly byte[] StartMarkerArray = [ 0, 0, 0, 1 ];

        private byte[]? _rentedBuffer = rentedBuffer;
        private byte[]? _rentedParammeterSetBuffer = rentedParammeterSetBuffer;
        #endregion

        #region Properties
        public override FrameType Type => FrameType.Video;
        public string Codec { get; set; } = string.Empty;
        public bool IsKeyFrame { get; set; } = false;
        public double FPS { get; set; }
        public virtual ReadOnlyMemory<byte> ParameterSets => ReadOnlyMemory<byte>.Empty;
        #endregion

        protected override void OnDispose()
        {
            if (_rentedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_rentedBuffer);
                _rentedBuffer = null;
            }

            if (_rentedParammeterSetBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_rentedParammeterSetBuffer);
                _rentedParammeterSetBuffer = null;
            }

            base.OnDispose();
        }
    }
}
