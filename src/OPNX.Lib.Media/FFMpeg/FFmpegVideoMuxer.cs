using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Media.FFMpeg.Generic;

namespace OPNX.Lib.Media.FFMpeg
{
    public sealed unsafe class FFmpegVideoMuxer : DisposableObject
    {
        #region Fields        
        private readonly AVOutputFormat* _outputFormat = null;
        private readonly AVFormatContext* _formatContext = null;
        private readonly AVDictionary* _opt = null;

        private readonly OutputStream _outputStream = new();

        private readonly FFmpegVideoEncoder? _encoder = null;

        private readonly bool _isInitialized = false;

        private readonly string _filePath = string.Empty;

        private long _firstPTS = long.MinValue;
        private long _currentPTS = long.MinValue;
        private readonly ILogger _logger;
        #endregion

        public FFmpegVideoMuxer(string filePath, AVCodecID codecID, AVPixelFormat pixelFormat, int width, int height, ILogger? logger = null)
            : this(filePath, AVHWDeviceType.AV_HWDEVICE_TYPE_NONE, codecID, pixelFormat, width, height, logger)
        {
        }

        public FFmpegVideoMuxer(string filePath, AVHWDeviceType hwDeviceType, AVCodecID codecID, AVPixelFormat pixelFormat, int width, int height, ILogger? logger = null)
            : base()
        {
            _logger = logger ?? NullLogger.Instance;

            try
            {
                _encoder = new FFmpegVideoEncoder(hwDeviceType, codecID, pixelFormat, width, height, logger);
                _encoder.VideoFrameEncoded += Encoder_VideoFrameEncoded;

                fixed (AVFormatContext** pFormatContext = &_formatContext)
                {
                    if (ffmpeg.avformat_alloc_output_context2(pFormatContext, null, null, filePath) < 0)
                    {
                        throw new Exception("Failed to allocate the output context.");
                    }
                }

                _outputFormat = _formatContext->oformat;

                fixed (OutputStream* pOutputStream = &_outputStream)
                fixed (AVDictionary** pOpt = &_opt)
                {
                    pOutputStream->st = ffmpeg.avformat_new_stream(_formatContext, _encoder.Codec);

                    if (pOutputStream->st is null)
                    {
                        throw new ApplicationException("Failed to allocate the stream.");
                    }

                    pOutputStream->st->id = (int)(_formatContext->nb_streams - 1);
                    pOutputStream->enc = _encoder.CodecContext;

                    if ((_formatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) == ffmpeg.AVFMT_GLOBALHEADER)
                    {
                        _encoder.CodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                    }

                    PrepareVideoStream(pOutputStream);

                    if ((_outputFormat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                    {
                        ffmpeg.avio_open(&_formatContext->pb, filePath, ffmpeg.AVIO_FLAG_WRITE).ThrowExceptionIfError();
                    }

                    ffmpeg.avformat_write_header(_formatContext, pOpt).ThrowExceptionIfError();


                    _filePath = filePath;

                    _isInitialized = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
            }
        }

        #region Properties
        public bool IsInitialized => _isInitialized;

        public string FilePath => _filePath;
        #endregion

        #region Public Methods
        public unsafe bool TryMuxing(AVFrame* srcFrame)
        {
            if (IsDisposed)
                return false;

            bool result = false;

            try
            {
                _encoder?.TryEncode(srcFrame);
                result = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
            }
            return result;
        }

        public TimeSpan GetVideoDuration()
        {
            if (_formatContext == null)
            {
                throw new InvalidOperationException("Format context is not initialized.");
            }

            if (_firstPTS <= long.MinValue || _currentPTS <= long.MinValue)
                return TimeSpan.Zero;

            // 스트림의 첫 번째 스트림을 선택하여 time_base를 가져옵니다.
            AVStream* stream = _formatContext->streams[0];
            AVRational timeBase = stream->time_base;

            // Duration을 계산합니다.
            double durationInSeconds = (_currentPTS - _firstPTS) * timeBase.num / (double)timeBase.den;

            return TimeSpan.FromSeconds(durationInSeconds);
        }
        #endregion

        #region Private / Protected Methods
        protected override void OnDispose()
        {
            try
            {
                if (_encoder != null)
                    _encoder.VideoFrameEncoded -= Encoder_VideoFrameEncoded;

                if (_formatContext != null)
                    ffmpeg.av_write_trailer(_formatContext);

                fixed (OutputStream* pOs = &_outputStream)
                {
                    CloseStream(pOs);
                }

                if (_formatContext != null &&
                    (_outputFormat->flags & ffmpeg.AVFMT_NOFILE) == 0 &&
                    _formatContext->pb != null)
                {
                    ffmpeg.avio_closep(&_formatContext->pb);
                }

                if (_formatContext != null)
                {
                    ffmpeg.avformat_free_context(_formatContext);
                }

                if (_opt != null)
                {
                    fixed (AVDictionary** pOpt = &_opt)
                    {
                        ffmpeg.av_dict_free(pOpt);
                    }
                }
            }
            finally
            {
                _encoder?.Dispose();
            }
        }

        private void Encoder_VideoFrameEncoded(object sender, EventHandlers.VideoFrameEncodedEventArgs e)
        {
            if (e.EncodedPacket->size <= 0)
                return;

            AVPacket* packet = ffmpeg.av_packet_clone(e.EncodedPacket);
            if (packet == null)
                return;

            try
            {
                if (_firstPTS <= long.MinValue)
                    _firstPTS = packet->pts;
                _currentPTS = packet->pts;

                packet->stream_index = _outputStream.st->index;

                ffmpeg.av_interleaved_write_frame(_formatContext, packet).ThrowExceptionIfError();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
            }
            finally
            {
                ffmpeg.av_packet_free(&packet);
            }
        }

        private static unsafe void PrepareVideoStream(OutputStream* ost)
        {
            AVCodecContext* c = ost->enc;

            ost->frame = Alloc_picture(c->pix_fmt, c->width, c->height);
            if (ost->frame == null)
            {
                throw new ApplicationException("Failed to allocate the video frame.");
            }

            ost->tmp_frame = null;
            if (c->pix_fmt != AVPixelFormat.AV_PIX_FMT_YUV420P)
            {
                ost->tmp_frame = Alloc_picture(AVPixelFormat.AV_PIX_FMT_YUV420P, c->width, c->height);
                if (ost->tmp_frame == null)
                {
                    throw new ApplicationException("Failed to allocate the temporary picture.");
                }
            }

            ffmpeg.avcodec_parameters_from_context(ost->st->codecpar, c).ThrowExceptionIfError();
        }

        private unsafe static AVFrame* Alloc_picture(AVPixelFormat pix_fmt, int width, int height)
        {
            AVFrame* picture;

            picture = ffmpeg.av_frame_alloc();
            if (picture == null)
            {
                return null;
            }

            picture->format = (int)pix_fmt;
            picture->width = width;
            picture->height = height;

            ffmpeg.av_frame_get_buffer(picture, 0).ThrowExceptionIfError();

            return picture;
        }

        private static unsafe void CloseStream(OutputStream* ost)
        {
            ffmpeg.av_frame_free(&ost->frame);
            ffmpeg.av_frame_free(&ost->tmp_frame);
            ffmpeg.av_packet_free(&ost->tmp_pkt);
            ffmpeg.sws_freeContext(ost->sws_ctx);
            ffmpeg.swr_free(&ost->swr_ctx);
        }
        #endregion
    }
}



