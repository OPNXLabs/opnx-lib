using FFmpeg.AutoGen;

namespace OPNX.Lib.Media.FFMpeg
{
    public unsafe class AVFrameWrapper(AVFrame* frame) : IDisposable
    {
        #region Fields
        private bool isDisposed = false;
        #endregion

        #region Properties
        public unsafe AVFrame* Frame { get; set; } = frame;
        public long Timestamp
        {
            get
            {
                if (Frame != null)
                    return Frame->pts;
                return long.MinValue;
            }
        }
        #endregion

        #region Public Methods
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Private / Protected Methods
        protected virtual void Dispose(bool disposing)
        {
            if (isDisposed) return;

            AVFrame* frame = Frame;
            FFmpegHelper.FreeFrame(ref frame);

            isDisposed = true;
        }
        #endregion
    }
}
