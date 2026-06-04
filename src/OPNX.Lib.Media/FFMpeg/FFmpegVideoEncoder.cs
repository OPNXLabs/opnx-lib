using FFmpeg.AutoGen;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Media.FFMpeg.EventHandlers;
using System.Drawing;

namespace OPNX.Lib.Media.FFMpeg
{
    public sealed class FFmpegVideoEncoder : DisposableObject
    {
        #region Fields
        private const int DEFAULT_FPS_VALUE = 30;
        private const int DEFAULT_GOP_VALUE = 30;
        private const int DEFAULT_MAX_B_FRAME_COUNT = 0; // B-프레임 생성하지 않음
        private const int DEFAULT_MIN_QUALITY_VALUE = 15;
        private const int DEFAULT_MAX_QUALITY_VALUE = 45;

        private readonly Dictionary<int, AVHWDeviceType> availableHWDecoders = [];

        private unsafe readonly AVCodec* _codec;
        private unsafe readonly AVCodecContext* _codecContext;
        private unsafe AVPacket* _packet;

        private FFmpegVideoConverter? _converter = null;
        private unsafe AVFrame* _convertedFrame = null;

        private readonly int _fps = DEFAULT_FPS_VALUE;
        private readonly int _gop = DEFAULT_GOP_VALUE;
        #endregion

        #region Constructors
        public FFmpegVideoEncoder(AVHWDeviceType hwDeviceType, AVCodecID codecID, AVPixelFormat pixelFormat, int width, int height)
            : this(hwDeviceType, codecID, pixelFormat, width, height, DEFAULT_FPS_VALUE, DEFAULT_GOP_VALUE)
        {
        }

        public FFmpegVideoEncoder(AVHWDeviceType hwDeviceType, AVCodecID codecID, AVPixelFormat pixelFormat, int width, int height, int fps, int gop)
            : this(hwDeviceType, codecID, pixelFormat, width, height, fps, gop, FFmpegHelper.CalculateMiddleBitrate(codecID, width, height))
        {
        }
        public FFmpegVideoEncoder(AVHWDeviceType hwDeviceType, AVCodecID codecID, AVPixelFormat pixelFormat,
            int width, int height, int fps, int gop, long bitRate)
            : base()
        {
            try
            {
                _fps = fps;
                _gop = gop;
                long bitRateKbps = bitRate * 1000;

                if (hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA && !FFmpegHelper.IsCUDAInstalled())
                    hwDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

                unsafe
                {
                    _codec = codecID switch
                    {
                        AVCodecID.AV_CODEC_ID_H264 when hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA =>
                            ffmpeg.avcodec_find_encoder_by_name("h264_nvenc"),
                        AVCodecID.AV_CODEC_ID_HEVC when hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA =>
                            ffmpeg.avcodec_find_encoder_by_name("hevc_nvenc"),
                        AVCodecID.AV_CODEC_ID_AV1 => ffmpeg.avcodec_find_encoder_by_name("libaom-av1"),
                        _ => ffmpeg.avcodec_find_encoder(codecID)
                    };

                    _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
                    _codecContext->pix_fmt = pixelFormat;
                    _codecContext->bit_rate = bitRateKbps;
                    _codecContext->width = width;
                    _codecContext->height = height;
                    _codecContext->time_base = new AVRational { num = 1, den = fps };
                    _codecContext->framerate = new AVRational { num = fps, den = 1 };
                    _codecContext->has_b_frames = 0;
                    _codecContext->max_b_frames = 0;


                    if (codecID == AVCodecID.AV_CODEC_ID_H264 || codecID == AVCodecID.AV_CODEC_ID_HEVC)
                    {
                        _codecContext->slices = 1;
                        _codecContext->max_b_frames = DEFAULT_MAX_B_FRAME_COUNT; // 지연 방지
                        _codecContext->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
                    }


                    //if (codecContext->codec_id == AVCodecID.AV_CODEC_ID_MPEG2VIDEO)
                    //{
                    //    codecContext->max_b_frames = 2;
                    //}

                    //if (codecContext->codec_id == AVCodecID.AV_CODEC_ID_MPEG1VIDEO)
                    //{
                    //    codecContext->mb_decision = 2;
                    //}

                    if (hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA)
                    {
                        ffmpeg.av_hwdevice_ctx_create(&_codecContext->hw_device_ctx, hwDeviceType, null, null, 0)
                            .ThrowExceptionIfError();
                    }

                    switch (codecID)
                    {
                        case AVCodecID.AV_CODEC_ID_H264:
                            {
                                ffmpeg.av_opt_set(_codecContext->priv_data, "profile", "baseline", 0).ThrowExceptionIfError();

                                //CRF설정
                                //ffmpeg.av_opt_set(codecContext->priv_data, "crf", "23", 0).ThrowExceptionIfError(); // CRF (Constant Rate Factor)                                              

                                //CBR설정 : 비트레이트 제한 모드. 
                                ffmpeg.av_opt_set(_codecContext->priv_data, "bitrate", bitRateKbps.ToString(), 0);      // kbps 단위
                                ffmpeg.av_opt_set(_codecContext->priv_data, "bufsize", (bitRateKbps * 2).ToString(), 0); // VBV 버퍼 크기, 대략 2배 정도                                
                                ffmpeg.av_opt_set(_codecContext->priv_data, "maxrate", bitRateKbps.ToString(), 0);      // 최대 비트레이트

                                ffmpeg.av_opt_set(_codecContext->priv_data, "preset", "ultrafast", 0).ThrowExceptionIfError();//ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow
                                ffmpeg.av_opt_set(_codecContext->priv_data, "tune", "fastdecode", 0).ThrowExceptionIfError(); // "zerolatency"는 실시간 스트리밍을 위해 지연 시간을 최소화하는 것을 목표 fastdecode                                        
                                //ffmpeg.av_opt_set(codecContext->priv_data, "tune", "zerolatency", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_codecContext->priv_data, "level", FFmpegHelper.GetCodecLevel(AVCodecID.AV_CODEC_ID_H264, width, height), 0).ThrowExceptionIfError();


                                //ffmpeg.av_opt_set(codecContext->priv_data, "x264opts", $"keyint={gop}:min-keyint={gop}", 0).ThrowExceptionIfError();
                                //ffmpeg.av_opt_set(codecContext->priv_data, "x264opts", $"keyint={gop}:min-keyint={gop / 2}:no-scenecut", 0).ThrowExceptionIfError();                                

                                //string x264Params = "repeat-headers=1:no-mbtree=1:aq-mode=0:ref=1";
                                //ffmpeg.av_opt_set(codecContext->priv_data, "x264-params", x264Params, 0).ThrowExceptionIfError();
                            }
                            break;
                        case AVCodecID.AV_CODEC_ID_HEVC:
                            {
                                ffmpeg.av_opt_set(_codecContext->priv_data, "crf", "23", 0).ThrowExceptionIfError(); // CRF (Constant Rate Factor)          
                                ffmpeg.av_opt_set(_codecContext->priv_data, "profile", "main", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_codecContext->priv_data, "preset", "ultrafast", 0).ThrowExceptionIfError();//ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow                                    
                                ffmpeg.av_opt_set(_codecContext->priv_data, "tune", "fastdecode", 0).ThrowExceptionIfError(); // "zerolatency"는 실시간 스트리밍을 위해 지연 시간을 최소화하는 것을 목표 fastdecode                                                                        
                                ffmpeg.av_opt_set(_codecContext->priv_data, "x265-params", "annexb=1:repeat-headers=1:aud=1", 0).ThrowExceptionIfError();
                            }
                            break;
                        case AVCodecID.AV_CODEC_ID_AV1:
                            {
                                // 화질 / 속도 밸런스
                                ffmpeg.av_opt_set(_codecContext->priv_data, "crf", "32", 0).ThrowExceptionIfError(); // 품질(낮을수록 고화질, 30~34 권장)                                
                                ffmpeg.av_opt_set(_codecContext->priv_data, "cpu-used", "6", 0).ThrowExceptionIfError(); // 속도 우선(0~8, 클수록 빠름)
                                ffmpeg.av_opt_set(_codecContext->priv_data, "row-mt", "1", 0).ThrowExceptionIfError();   // 멀티스레드 인코딩
                                ffmpeg.av_opt_set(_codecContext->priv_data, "tile-columns", "1", 0).ThrowExceptionIfError(); // 멀티코어 분할
                                ffmpeg.av_opt_set(_codecContext->priv_data, "tile-rows", "0", 0).ThrowExceptionIfError();

                                // 지연 최소화
                                ffmpeg.av_opt_set(_codecContext->priv_data, "lag-in-frames", "0", 0).ThrowExceptionIfError(); // 버퍼링 최소화           

                                // 필요 시: 화질 튜닝
                                //ffmpeg.av_opt_set(codecContext->priv_data, "aq-mode", "0", 0).ThrowExceptionIfError(); // 단순화 (0=off)
                                //ffmpeg.av_opt_set(codecContext->priv_data, "static-thresh", "0", 0).ThrowExceptionIfError(); // 빠른 장면 판단
                                //ffmpeg.av_opt_set(codecContext->priv_data, "arnr-maxframes", "3", 0).ThrowExceptionIfError(); // 노이즈 억제 약하게
                                //ffmpeg.av_opt_set(codecContext->priv_data, "arnr-strength", "1", 0).ThrowExceptionIfError();                                
                            }
                            break;
                    }

                    /* check that the encoder supports s16 pcm input */
                    //codecContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_NONE;

                    ///* select other audio parameters supported by the encoder */
                    //codecContext->sample_rate = select_sample_rate(codec);
                    //codecContext->channel_layout = select_channel_layout(codec);
                    //codecContext->channels = ffmpeg.av_get_channel_layout_nb_channels(codecContext->channel_layout);

                    //if (hwDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE && availableHWDecoders.Any(x => x.Value == hwDeviceType))
                    //{
                    //    ffmpeg.av_hwdevice_ctx_create(&codecContext->hw_device_ctx, hwDeviceType, null, null, 0)
                    //        .ThrowExceptionIfError();
                    //}

                    //AVDictionary* opts = null;
                    ////ffmpeg.av_dict_set_int(&opts, "allow_skip_frames", 1, 0);

                    //if (ffmpeg.avcodec_open2(codecContext, codec, &opts) < 0)
                    //{
                    //    throw new Exception($"Could not open codec");
                    //}

                    if (ffmpeg.avcodec_open2(_codecContext, _codec, null) != 0)
                    {
                        throw new Exception("Failed to open the video codec.");
                    }

                    _packet = ffmpeg.av_packet_alloc();
                }
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }


        //private unsafe AVPixelFormat get_hw_format(AVCodecContext* ctx, AVPixelFormat* pix_fmts)
        //{
        //    AVPixelFormat* p;

        //    for (p = pix_fmts; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
        //    {
        //        if (*p ==  AVPixelFormat.AV_PIX_FMT_NV12)
        //        {
        //            return *p;
        //        }
        //    }

        //    Console.WriteLine("Failed to get HW surface format.");
        //    return AVPixelFormat.AV_PIX_FMT_NONE;
        //}
        #endregion

        #region Events
        public event VideoFrameEncodedEventHandler? VideoFrameEncoded;
        #endregion

        #region Properties
        public unsafe int Width => _codecContext == null ? int.MinValue : _codecContext->width;

        public unsafe int Height => _codecContext == null ? int.MinValue : _codecContext->height;

        public unsafe AVCodecID CodecID => _codecContext == null ? AVCodecID.AV_CODEC_ID_NONE : _codecContext->codec_id;

        public unsafe AVCodec* Codec => _codec;

        public unsafe AVCodecContext* CodecContext => _codecContext;

        public unsafe AVPixelFormat PixelFormat => _codecContext == null ? AVPixelFormat.AV_PIX_FMT_NONE : _codecContext->pix_fmt;
        #endregion

        #region Private / Protected Methods
        //private static int select_sample_rate(AVCodec* codec)
        //{
        //    int best_samplerate = 0;

        //    if (codec->supported_samplerates == null)
        //        return 44100;

        //    int* p = codec->supported_samplerates;
        //    while (*p != 0)
        //    {
        //        if (best_samplerate != 0 || Math.Abs(44100 - *p) < Math.Abs(44100 - best_samplerate))
        //            best_samplerate = *p;
        //        p++;
        //    }
        //    return best_samplerate;
        //}

        /* select layout with the highest channel count */
        //private static ulong select_channel_layout(AVCodec* codec)
        //{
        //    ulong* p;
        //    ulong best_ch_layout = 0;
        //    int best_nb_channels = 0;


        //    if (codec->ch_layouts == null)
        //        return ffmpeg.AV_CH_LAYOUT_STEREO;

        //    p = codec->channel_layouts;
        //    while (*p != 0)
        //    {

        //        int nb_channels = ffmpeg.av_get_channel_layout_nb_channels(*p);

        //        if (nb_channels > best_nb_channels)
        //        {
        //            best_ch_layout = *p;
        //            best_nb_channels = nb_channels;
        //        }
        //        p++;
        //    }

        //    return best_ch_layout;
        //}
        protected override void OnDispose()
        {
            try
            {
                _converter?.Dispose();
                _converter = null;

                unsafe
                {
                    if (_codecContext != null)
                    {
                        ffmpeg.avcodec_send_frame(_codecContext, null);

                        AVPacket* _packet = ffmpeg.av_packet_alloc();
                        // 2. 인코더로부터 남은 패킷들을 받아냅니다.
                        while (ffmpeg.avcodec_receive_packet(_codecContext, _packet) == 0)
                        {
                            // 패킷 처리...
                            ffmpeg.av_packet_unref(_packet); // 사용한 패킷을 해제
                        }
                        ffmpeg.av_packet_free(&_packet);

                        var codecContext = _codecContext;
                        ffmpeg.avcodec_free_context(&codecContext);
                    }

                    FFmpegHelper.FreePacket(ref _packet);

                    FFmpegHelper.FreeFrame(ref _convertedFrame);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }

        //private unsafe static int getStringSize(byte* buffer, int length)
        //{
        //    for (int i = 0; i < length; i++)
        //    {
        //        if (buffer[i] == 0)
        //        {
        //            return i;
        //        }
        //    }

        //    return 0;
        //}
        #endregion

        #region Public Methods          
        public unsafe void TryEncode(AVFrame* srcFrame)
        {
            if (IsDisposed)
                return;

            AVFrame* encodeFrame = srcFrame;

            if (srcFrame->width != _codecContext->width || srcFrame->height != _codecContext->height || (AVPixelFormat)srcFrame->format != _codecContext->pix_fmt)
            {
                if (_converter == null || _codecContext->width != _converter.DstSize.Width || _codecContext->height != _converter.DstSize.Height || _codecContext->pix_fmt != _converter.DstFixFmt)
                {
                    _converter?.Dispose();  // 기존 변환기 해제
                    _converter = new FFmpegVideoConverter(new Size(srcFrame->width, srcFrame->height), (AVPixelFormat)srcFrame->format,
                                                            new Size(_codecContext->width, _codecContext->height),
                                                            _codecContext->pix_fmt);
                }

                if (_convertedFrame == null)
                    _convertedFrame = ffmpeg.av_frame_alloc(); // convertedFrame이 null일 경우에만 할당
                else
                    ffmpeg.av_frame_unref(_convertedFrame); // 이전 프레임 해제

                if (!_converter.TryConvert(encodeFrame, _convertedFrame))
                {
                    _converter.Dispose();
                    _converter = null;
                    return;
                }

                encodeFrame = _convertedFrame; // 변환된 프레임 사용
            }

            try
            {
                if (ffmpeg.avcodec_send_frame(_codecContext, encodeFrame) < 0)
                    return;

                while (true)
                {
                    int ret = ffmpeg.avcodec_receive_packet(_codecContext, _packet);
                    if (ret == 0)
                    {
                        VideoFrameEncoded?.Invoke(this, new VideoFrameEncodedEventArgs(_packet));
                        ffmpeg.av_packet_unref(_packet);
                        continue;
                    }

                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                        break;

                    ffmpeg.av_packet_unref(_packet);
                    return;
                }
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }
        #endregion
    }
}

