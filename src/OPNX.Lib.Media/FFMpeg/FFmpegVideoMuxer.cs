using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Common.LifeCycle;

namespace OPNX.Lib.Media.FFMpeg
{
    public enum FFmpegVideoMuxerMode
    {
        Encode,
        Remux
    }

    /// <summary>
    /// Single video track writer. The file extension selects the container (for example .mkv).
    /// </summary>
    public sealed unsafe class FFmpegVideoMuxer : DisposableObject
    {
        #region Fields
        private readonly object _sync = new();

        private AVFormatContext* _formatContext;
        private AVStream* _stream;

        private FFmpegVideoEncoder? _encoder;
        private AVRational _inputTimeBase;

        private bool _headerWritten;
        private bool _completed;
        private bool _failed;

        private double? _firstSeconds;
        private double _endSeconds;

        private readonly ILogger _logger;
        #endregion

        #region Constructors
        public FFmpegVideoMuxer(string filePath, FFmpegVideoMuxerMode mode, ILogger? logger = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            if (!Enum.IsDefined(mode))
                throw new ArgumentOutOfRangeException(nameof(mode));

            FilePath = filePath;
            Mode = mode;
            _logger = logger ?? NullLogger.Instance;
        }

        public FFmpegVideoMuxer(string filePath, AVCodecID codecID, AVPixelFormat pixelFormat,
            int width, int height, ILogger? logger = null)
            : this(filePath, AVHWDeviceType.AV_HWDEVICE_TYPE_NONE, codecID, pixelFormat, width, height, logger)
        {
        }

        public FFmpegVideoMuxer(string filePath, AVHWDeviceType hwDeviceType, AVCodecID codecID,
            AVPixelFormat pixelFormat, int width, int height, ILogger? logger = null)
            : this(filePath, FFmpegVideoMuxerMode.Encode, logger)
        {
            Initialize(codecID, pixelFormat, width, height, hwDeviceType);
        }

        #endregion

        #region Properties
        public FFmpegVideoMuxerMode Mode { get; }

        public string FilePath { get; }

        public bool IsInitialized => !IsDisposed && _headerWritten && !_completed && !_failed;

        #endregion

        #region Public Methods
        public bool Initialize(AVCodecID codecID, AVPixelFormat pixelFormat, int width, int height,
            AVHWDeviceType hwDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            lock (_sync)
            {
                if (!CanInitialize(FFmpegVideoMuxerMode.Encode) || width <= 0 || height <= 0 || codecID == AVCodecID.AV_CODEC_ID_NONE)
                    return false;

                try
                {
                    AllocateOutput();
                    bool globalHeader = (_formatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0;
                    _encoder = new FFmpegVideoEncoder(hwDeviceType, codecID, pixelFormat, width, height,
                        30, 30, FFmpegHelper.CalculateMiddleBitrate(codecID, width, height), _logger, globalHeader);
                    if (_encoder.CodecContext == null || ffmpeg.avcodec_is_open(_encoder.CodecContext) == 0)
                        throw new InvalidOperationException("Video encoder could not be opened.");
                    ffmpeg.avcodec_parameters_from_context(_stream->codecpar, _encoder.CodecContext).ThrowExceptionIfError();
                    _stream->avg_frame_rate = _encoder.CodecContext->framerate;
                    _inputTimeBase = _encoder.CodecContext->time_base;
                    WriteHeader();
                    _encoder.VideoFrameEncoded += Encoder_VideoFrameEncoded;
                    return true;
                }
                catch (Exception ex)
                {
                    return InitializationFailed(ex);
                }
            }
        }

        /// <summary>
        /// Copies video codec parameters. Caller retains ownership. Timestamps must use inputTimeBase.
        /// </summary>
        public bool Initialize(AVCodecParameters* parameters, AVRational inputTimeBase)
        {
            lock (_sync)
            {
                if (!CanInitialize(FFmpegVideoMuxerMode.Remux) || parameters == null || !ValidTimeBase(inputTimeBase) ||
                    parameters->codec_type != AVMediaType.AVMEDIA_TYPE_VIDEO || parameters->codec_id == AVCodecID.AV_CODEC_ID_NONE ||
                    parameters->width <= 0 || parameters->height <= 0 || parameters->extradata_size < 0 ||
                    (parameters->extradata_size > 0 && parameters->extradata == null) ||
                    ((parameters->codec_id == AVCodecID.AV_CODEC_ID_H264 || parameters->codec_id == AVCodecID.AV_CODEC_ID_HEVC) &&
                        parameters->extradata_size == 0))
                    return false;

                try
                {
                    AllocateOutput();
                    ffmpeg.avcodec_parameters_copy(_stream->codecpar, parameters).ThrowExceptionIfError();
                    _inputTimeBase = inputTimeBase;
                    WriteHeader();
                    return true;
                }
                catch (Exception ex)
                {
                    return InitializationFailed(ex);
                }
            }
        }

        /// <summary>
        /// Initializes from compressed stream information. For H.264/H.265 MKV, supply codec configuration
        /// (avcC/hvcC or Annex-B parameter sets supported by FFmpeg). No SPS parsing or timestamp inference is performed here.
        /// </summary>
        public bool Initialize(AVCodecID codecID, int width, int height, AVRational inputTimeBase,
            ReadOnlySpan<byte> extraData)
        {
            lock (_sync)
            {
                if (!CanInitialize(FFmpegVideoMuxerMode.Remux))
                    return false;
                AVCodecParameters* parameters = null;

                try
                {
                    parameters = ffmpeg.avcodec_parameters_alloc();
                    if (parameters == null)
                        throw new OutOfMemoryException();
                    parameters->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
                    parameters->codec_id = codecID;
                    parameters->width = width;
                    parameters->height = height;
                    if (!extraData.IsEmpty)
                    {
                        parameters->extradata = (byte*)ffmpeg.av_mallocz((ulong)checked(extraData.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
                        if (parameters->extradata == null)
                            throw new OutOfMemoryException();
                        extraData.CopyTo(new Span<byte>(parameters->extradata, extraData.Length));
                        parameters->extradata_size = extraData.Length;
                    }
                    return Initialize(parameters, inputTimeBase);
                }
                catch (Exception ex)
                {
                    return InitializationFailed(ex);
                }
                finally
                {
                    ffmpeg.avcodec_parameters_free(&parameters);
                }
            }
        }

        public bool TryMuxing(AVFrame* srcFrame)
        {
            lock (_sync)
            {
                if (!IsInitialized || Mode != FFmpegVideoMuxerMode.Encode || srcFrame == null)
                    return false;

                try
                {
                    if (!_encoder!.EncodeFrame(srcFrame))
                        return Fail(new InvalidOperationException("Video frame encoding failed."));
                    return !_failed;
                }
                catch (Exception ex)
                {
                    return Fail(ex);
                }
            }
        }

        /// <summary>
        /// Writes a copy; never consumes the caller's packet. Supply presentation and decoding timestamps
        /// in the configured input time base, in decoding order. Normalize RTP wrap/reset and segment origins upstream.
        /// </summary>
        public bool TryMuxing(AVPacket* packet)
        {
            lock (_sync)
            {
                if (!IsInitialized || Mode != FFmpegVideoMuxerMode.Remux || packet == null || packet->size <= 0 ||
                    packet->data == null || packet->pts == ffmpeg.AV_NOPTS_VALUE || packet->dts == ffmpeg.AV_NOPTS_VALUE ||
                    packet->duration < 0)
                    return false;

                return WritePacket(packet);
            }
        }

        /// <summary>
        /// Writes one complete compressed access unit, not an individual slice. H.264/H.265 data may use Annex-B.
        /// </summary>
        public bool TryMuxing(ReadOnlySpan<byte> data, long pts, long dts, bool isKeyFrame, long duration = 0)
        {
            lock (_sync)
            {
                if (!IsInitialized || Mode != FFmpegVideoMuxerMode.Remux || data.IsEmpty ||
                    pts == ffmpeg.AV_NOPTS_VALUE || dts == ffmpeg.AV_NOPTS_VALUE || duration < 0)
                    return false;
                AVPacket* packet = null;

                try
                {
                    packet = ffmpeg.av_packet_alloc();
                    if (packet == null)
                        throw new OutOfMemoryException();
                    ffmpeg.av_new_packet(packet, data.Length).ThrowExceptionIfError();
                    data.CopyTo(new Span<byte>(packet->data, data.Length));
                    packet->pts = pts;
                    packet->dts = dts;
                    packet->duration = duration;
                    packet->flags = isKeyFrame ? ffmpeg.AV_PKT_FLAG_KEY : 0;
                    return WritePacket(packet);
                }
                catch (Exception ex)
                {
                    return Fail(ex);
                }
                finally
                {
                    ffmpeg.av_packet_free(&packet);
                }
            }
        }

        public TimeSpan GetVideoDuration()
        {
            lock (_sync)
            {
                return _firstSeconds.HasValue ? TimeSpan.FromSeconds(Math.Max(0, _endSeconds - _firstSeconds.Value)) : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Flushes delayed encoded packets and finalizes the container. Repeated calls return the same result.
        /// </summary>
        public bool Complete()
        {
            lock (_sync)
            {
                if (_completed)
                    return !_failed;
                if (!_headerWritten || _formatContext == null)
                    return false;

                try
                {
                    if (_encoder != null && !_failed)
                    {
                        ffmpeg.avcodec_send_frame(_encoder.CodecContext, null).ThrowExceptionIfError();
                        AVPacket* packet = ffmpeg.av_packet_alloc();
                        if (packet == null)
                            throw new OutOfMemoryException();
                        try
                        {
                            while (true)
                            {
                                int result = ffmpeg.avcodec_receive_packet(_encoder.CodecContext, packet);
                                if (result == ffmpeg.AVERROR_EOF)
                                    break;
                                result.ThrowExceptionIfError();
                                if (!WritePacket(packet))
                                    break;
                                ffmpeg.av_packet_unref(packet);
                            }
                        }
                        finally
                        {
                            ffmpeg.av_packet_free(&packet);
                        }
                    }
                    ffmpeg.av_write_trailer(_formatContext).ThrowExceptionIfError();
                }
                catch (Exception ex)
                {
                    Fail(ex);
                }
                finally
                {
                    _completed = true;
                    ReleaseOutput();
                }
                return !_failed;
            }
        }

        #endregion

        #region Private / Protected Methods
        private bool CanInitialize(FFmpegVideoMuxerMode mode)
        {
            return !IsDisposed && Mode == mode &&
                !_headerWritten && !_completed && !_failed && _formatContext == null;
        }

        private static bool ValidTimeBase(AVRational value)
        {
            return value.num > 0 && value.den > 0;
        }

        private void AllocateOutput()
        {
            AVFormatContext* context = null;
            int result = ffmpeg.avformat_alloc_output_context2(&context, null, null, FilePath);
            _formatContext = context;
            result.ThrowExceptionIfError();
            if (context == null)
                throw new OutOfMemoryException();
            _stream = ffmpeg.avformat_new_stream(context, null);
            if (_stream == null)
                throw new OutOfMemoryException();
        }

        private void WriteHeader()
        {
            _stream->codecpar->codec_tag = 0;
            _stream->time_base = _inputTimeBase;
            if ((_formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                ffmpeg.avio_open(&_formatContext->pb, FilePath, ffmpeg.AVIO_FLAG_WRITE).ThrowExceptionIfError();
            ffmpeg.avformat_write_header(_formatContext, null).ThrowExceptionIfError();
            _headerWritten = true;
        }

        private void Encoder_VideoFrameEncoded(object sender, EventHandlers.VideoFrameEncodedEventArgs e)
        {
            if (!_failed)
                WritePacket(e.EncodedPacket);
        }

        private bool WritePacket(AVPacket* source)
        {
            AVPacket* packet = null;

            try
            {
                packet = ffmpeg.av_packet_clone(source);
                if (packet == null)
                    throw new OutOfMemoryException();
                ffmpeg.av_packet_rescale_ts(packet, _inputTimeBase, _stream->time_base);
                packet->stream_index = _stream->index;
                packet->pos = -1;
                long pts = packet->pts;
                double seconds = pts * (double)_stream->time_base.num / _stream->time_base.den;
                double end = seconds + packet->duration * (double)_stream->time_base.num / _stream->time_base.den;
                ffmpeg.av_interleaved_write_frame(_formatContext, packet).ThrowExceptionIfError();
                if (pts != ffmpeg.AV_NOPTS_VALUE)
                {
                    _endSeconds = _firstSeconds.HasValue ? Math.Max(_endSeconds, end) : end;
                    _firstSeconds = _firstSeconds.HasValue ? Math.Min(_firstSeconds.Value, seconds) : seconds;
                }
                return true;
            }
            catch (Exception ex)
            {
                return Fail(ex);
            }
            finally
            {
                ffmpeg.av_packet_free(&packet);
            }
        }

        private bool Fail(Exception ex)
        {
            _failed = true;
            _logger.LogError(ex, "Video muxing failed for {FilePath} ({Mode}).", FilePath, Mode);
            return false;
        }

        private bool InitializationFailed(Exception ex)
        {
            Fail(ex);
            ReleaseOutput();
            return false;
        }

        private void ReleaseOutput()
        {
            if (_encoder != null)
            {
                _encoder.VideoFrameEncoded -= Encoder_VideoFrameEncoded;
                _encoder.Dispose();
                _encoder = null;
            }
            if (_formatContext != null)
            {
                if (_formatContext->pb != null && (_formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                {
                    int result = ffmpeg.avio_closep(&_formatContext->pb);
                    if (result < 0)
                        Fail(new IOException($"Closing muxer output failed: {result}."));
                }
                ffmpeg.avformat_free_context(_formatContext);
                _formatContext = null;
                _stream = null;
            }
        }

        protected override void OnDispose()
        {
            lock (_sync)
            {
                Complete();
                ReleaseOutput();
            }
        }

        protected override ValueTask OnDisposeAsync()
        {
            OnDispose();
            return ValueTask.CompletedTask;
        }
        #endregion
    }
}
