using FFmpeg.AutoGen;
using System.Collections.Concurrent;

namespace OPNX.Lib.Media.FFMpeg
{
    public class AVFramePool : IDisposable
    {
        #region Fields
        private readonly ConcurrentBag<AVFrameWrapper> framePool = [];
        #endregion

        #region Public Methods
        public unsafe AVFrameWrapper Rent()
        {
            if (framePool.TryTake(out var frame))
            {
                return frame;
            }

            return new AVFrameWrapper(ffmpeg.av_frame_alloc());
        }

        public void Return(AVFrameWrapper frame)
        {
            unsafe
            {
                ffmpeg.av_frame_unref(frame.Frame);
            }
            framePool.Add(frame);
        }

        public void Dispose()
        {
            while (framePool.TryTake(out var frame))
            {
                frame.Dispose();
            }

            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
