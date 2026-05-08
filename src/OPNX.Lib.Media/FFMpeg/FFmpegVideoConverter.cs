using FFmpeg.AutoGen;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Logging;
using System.Drawing;

namespace OPNX.Lib.Media.FFMpeg
{
    public sealed unsafe class FFmpegVideoConverter : DisposableBase
    {
        #region Fields
        //private readonly IntPtr _convertedFrameBufferPtr;

        //private readonly byte_ptr4 _dstData;
        //private readonly int4 _dstLinesize;
        private SwsContext* _swsContext;
        private Size _dstSize;
        private readonly AVPixelFormat _dstPixfmt;
        #endregion

        #region Constructors
        public FFmpegVideoConverter(Size sourceSize, AVPixelFormat sourcePixelFormat,
            Size destinationSize, AVPixelFormat destinationPixelFormat)
        {
            _dstSize = destinationSize;
            _dstPixfmt = destinationPixelFormat;

            try
            {
                _swsContext = ffmpeg.sws_getContext(
                    sourceSize.Width,
                    sourceSize.Height,
                    sourcePixelFormat,
                    destinationSize.Width,
                    destinationSize.Height,
                    destinationPixelFormat,
                    (int)SwsFlags.SWS_BILINEAR,
                    null,
                    null,
                    null
                );
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }

            if (_swsContext == null)
                throw new ApplicationException("Failed to initialize the conversion context.");

            //var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPixelFormat,
            //    destinationSize.Width,
            //    destinationSize.Height,
            //    1);

            //_convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);
            //_dstData = new byte_ptr4();
            //_dstLinesize = new int4();

            //ffmpeg.av_image_fill_arrays
            //    (
            //    ref _dstData,
            //    ref _dstLinesize,
            //    (byte*)_convertedFrameBufferPtr,
            //    destinationPixelFormat,
            //    destinationSize.Width,
            //    destinationSize.Height,
            //    1
            //    );
        }
        #endregion

        #region Properties
        public Size DstSize => _dstSize;

        public AVPixelFormat DstFixFmt => _dstPixfmt;
        #endregion

        #region Public Methods
        public bool TryConvert(AVFrame* sourceFrame, AVFrame* convertedFrame)
        {
            return TryConvert(sourceFrame->data, sourceFrame->linesize, sourceFrame->pts, sourceFrame->height, convertedFrame);
        }

        //public bool TryConvert(byte_ptr8 srcData, int8 srcLineSize, long pts, int height, AVFrame* convertedFrame)
        public bool TryConvert(byte_ptrArray8 srcData, int_array8 srcLineSize, long pts, int height, AVFrame* convertedFrame)
        {
            if (IsDisposed)
                return false;

            try
            {
                convertedFrame->width = _dstSize.Width;
                convertedFrame->height = _dstSize.Height;
                convertedFrame->format = (int)_dstPixfmt;
                //convertedFrame->color_range = AVColorRange.AVCOL_RANGE_JPEG;// sourceFrame.color_range;                            

                int ret = ffmpeg.av_frame_get_buffer(convertedFrame, 32);
                if (ret == 0)
                {
                    ret = ffmpeg.sws_scale(_swsContext,
                                            srcData,
                                            srcLineSize,
                                            0,
                                            height,
                                            convertedFrame->data,
                                            convertedFrame->linesize);
                    if (ret >= 0)
                    {
                        convertedFrame->pts = pts;

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);

            }

            //var data = new byte_ptr8();
            //data.UpdateFrom(_dstData);
            //var linesize = new int8();
            //linesize.UpdateFrom(_dstLinesize);

            //convertedFrame->data.UpdateFrom(_dstData);
            //convertedFrame->linesize.UpdateFrom(_dstLinesize);
            //convertedFrame->width = _destinationSize.Width;
            //convertedFrame->height = _destinationSize.Height;

            return false;

            //return new AVFrame
            //{
            //    data = data,
            //    linesize = linesize,
            //    width = _destinationSize.Width,
            //    height = _destinationSize.Height
            //};
        }
        #endregion

        #region Private / Protected Methods
        protected override void OnDispose()
        {
            //Marshal.FreeHGlobal(_convertedFrameBufferPtr);
            try
            {
                ffmpeg.sws_freeContext(_swsContext);
                _swsContext = null;
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }
        #endregion
    }
}
