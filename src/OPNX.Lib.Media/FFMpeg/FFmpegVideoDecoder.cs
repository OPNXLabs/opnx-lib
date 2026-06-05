using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Platform.Windows;
using OPNX.Lib.Media.FFMpeg.EventHandlers;
using OPNX.Lib.Media.FFMpeg.RawFrames.Video;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OPNX.Lib.Media.FFMpeg
{
    public sealed class FFmpegVideoDecoder : DisposableObject, INotifyPropertyChanged
    {
        #region Fields
        private const int AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;

        private unsafe readonly AVCodec* _codec;
        private unsafe AVCodecContext* _codecContext;
        private unsafe AVPacket* _packet = ffmpeg.av_packet_alloc();

        private int _extraDataLength = 0;
        //private ReadOnlyMemory<byte> extraData = new ReadOnlyMemory<byte>();
        //private byte[] extraData = new byte[0];

        private readonly Dictionary<int, AVHWDeviceType> _availableHWDecoders = [];
        private readonly AVHWDeviceType _hwDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

        private FFmpegVideoConverter? _converter = null;
        private FFmpegVideoFilter? _filter = null;

        private unsafe AVFrame* _decodedFrame = ffmpeg.av_frame_alloc();
        private unsafe AVFrame* _convertedFrame = ffmpeg.av_frame_alloc();
        private unsafe AVFrame* _hwDecodedFrame = null;

        private bool _isDecodedFirstKeyFrame = false;

        //private byte* tmpBuffer = null;
        //private AVFrame* cpuFrame = null;

        private readonly AVPixelFormat _hwPixFmt = AVPixelFormat.AV_PIX_FMT_NONE;

        private float _brightness = 0.0f;            //-1.0~1.0 기본값 : 0
        private float _contrast = 1.0f;              //-1000.0~1000.0 기본값 : 1
        private float _saturation = 1.0f;            //0.0~3.0 기본값 : 1

        private float _hue = 0.0f;                   //-360~360 기본값 0

        private float _gamma = 1.0f;                 //0.1~10.0 기본값 : 1
        private float _gamma_r = 1.0f;               //0.1~10.0 기본값 : 1
        private float _gamma_g = 1.0f;               //0.1~10.0 기본값 : 1
        private float _gamma_b = 1.0f;               //0.1~10.0 기본값 : 1
        private float _gamma_weight = 1.0f;          //0.1~10.0 기본값 : 1

        private readonly List<ReadOnlyMemory<byte>> _pendingParameterSets = [];

        private readonly byte[] _tempBuffer = new byte[64 * 1024]; // 임시 버퍼 재사용
        private readonly ILogger _logger;
        #endregion

        #region Constructors
        public FFmpegVideoDecoder(AVCodecID codecID, ILogger? logger = null)
            : this(AVHWDeviceType.AV_HWDEVICE_TYPE_NONE, codecID, logger)
        {
        }
        public FFmpegVideoDecoder(AVHWDeviceType hwDeviceType, AVCodecID codecID, ILogger? logger = null)
            : base()
        {
            _logger = logger ?? NullLogger.Instance;

            try
            {
                unsafe
                {
                    _codec = ffmpeg.avcodec_find_decoder(codecID);
                    ArgumentNullException.ThrowIfNull(_codec);
                    if (_codec->id == AVCodecID.AV_CODEC_ID_NONE)
                        throw new Exception("Failed to find the video codec.");

                    _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
                    ArgumentNullException.ThrowIfNull(_codecContext);

                    _codecContext->err_recognition = ffmpeg.AV_EF_IGNORE_ERR;

                    AVDictionary* options = null;

                    // 공통 옵션
                    ffmpeg.av_dict_set(&options, "refcounted_frames", "1", 0);      // AVFrame 참조 기반 관리
                    ffmpeg.av_dict_set(&options, "flags2", "+fast", 0);            // 디코딩 속도 우선
                    ffmpeg.av_opt_set_int(_codecContext->priv_data, "err_detect", 0, 0);

                    switch (_codec->id)
                    {
                        case AVCodecID.AV_CODEC_ID_HEVC:
                            {
                                // H.265 전용 플래그 설정
                                //codecContext->flags |= ffmpeg.AV_CODEC_FLAG_OUTPUT_CORRUPT;  // 손상된 프레임도 출력 (초기 화질 문제 완화)
                                //codecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_SHOW_ALL;     // 모든 프레임 표시
                                //codecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;         // 빠른 디코딩 우선
                                _codecContext->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;

                                // H.265는 프레임 레벨 병렬처리가 효과적
                                _codecContext->thread_type = ffmpeg.FF_THREAD_FRAME;
                                _codecContext->thread_count = Math.Min(4, Environment.ProcessorCount); // H.265는 4개 이하가 효율적

                                // H.265 전용 지연 시간 최적화
                                _codecContext->delay = 0;
                                _codecContext->has_b_frames = 0; // B-frame 비활성화로 지연 감소

                                // H.265 전용 옵션
                                ffmpeg.av_dict_set(&options, "threads", "4", 0);

                                // H.265 최적 스레드 수
                                //ffmpeg.av_dict_set(&options, "strict", "experimental", 0); // 실험적 기능 허용
                                //ffmpeg.av_dict_set(&options, "tune", "zerolatency", 0);    // 제로 지연 튜닝
                                //ffmpeg.av_dict_set(&options, "preset", "ultrafast", 0);    // 초고속 프리셋
                                //ffmpeg.av_dict_set(&options, "profile", "main", 0);        // Main 프로파일 강제 (Main10 대신)

                                //// H.265 디코딩 품질 개선
                                //ffmpeg.av_dict_set(&options, "apply_defdispwin", "1", 0);  // 기본 디스플레이 윈도우 적용
                                //ffmpeg.av_dict_set(&options, "strict_std_compliance", "0", 0); // 표준 준수 완화
                            }
                            break;
                        case AVCodecID.AV_CODEC_ID_AV1:
                            {
                                _codecContext->thread_type = ffmpeg.FF_THREAD_FRAME;
                                _codecContext->thread_count = Math.Min(8, Environment.ProcessorCount);

                                string? decoder = Marshal.PtrToStringAnsi((IntPtr)_codec->name);

                                if (decoder!.Contains("dav1d"))
                                {
                                    // dav1d 디코더 옵션
                                    ffmpeg.av_dict_set(&options, "threads", "8", 0);
                                    ffmpeg.av_dict_set(&options, "tilethreads", "2", 0);
                                    ffmpeg.av_dict_set(&options, "framethreads", "4", 0);
                                    ffmpeg.av_dict_set(&options, "fast", "1", 0);   // dav1d의 fast-decode
                                }
                                else if (decoder.Contains("aom") || decoder.Contains("libaom"))
                                {
                                    // libaom-av1 옵션
                                    ffmpeg.av_dict_set(&options, "threads", "8", 0);
                                    ffmpeg.av_dict_set(&options, "row-mt", "1", 0);
                                    ffmpeg.av_dict_set(&options, "tile_threads", "2", 0);
                                    ffmpeg.av_dict_set(&options, "cpu-used", "8", 0);
                                }
                                else
                                {
                                    // fallback (FFmpeg 내부 AV1 디코더)
                                    ffmpeg.av_dict_set(&options, "threads", "4", 0);
                                }
                            }
                            break;
                        case AVCodecID.AV_CODEC_ID_H264:
                            {
                                // H.264 최적화
                                _codecContext->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
                                _codecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
                                _codecContext->thread_type = ffmpeg.FF_THREAD_FRAME;
                                _codecContext->thread_count = Math.Max(1, Environment.ProcessorCount);

                                // H.264 전용 옵션
                                ffmpeg.av_dict_set(&options, "threads", "auto", 0);        // H.264는 자동 스레드 최적화
                                ffmpeg.av_dict_set(&options, "low_delay", "1", 0);         // 낮은 지연 시간
                            }
                            break;
                    }

                    if (ffmpeg.avcodec_open2(_codecContext, _codec, &options) < 0)
                        throw new Exception("Failed to open the video codec.");

                    if (options != null)
                        ffmpeg.av_dict_free(&options);
                }

                if (hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA && !FFmpegHelper.IsCUDAInstalled())
                    hwDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2;
                if (hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2 && !FFmpegHelper.IsDirectXInstalled())
                    hwDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
                if (hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA && !FFmpegHelper.IsDirectXInstalled())
                    hwDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

                //if (hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA)
                //{
                //    if (!FFmpegHelper.IsCUDAInstalled())
                //        hwDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2;
                //}

                //if (hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2)
                //{
                //    if (!FFmpegHelper.IsDirectXInstalled())
                //        hwDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                //}


                if (hwDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
                {
                    unsafe
                    {
                        if (ffmpeg.av_hwdevice_ctx_create(&_codecContext->hw_device_ctx, hwDeviceType, null, null, 0) == 0)
                            _hwDeviceType = hwDeviceType;

                        for (int i = 0; i < 64; i++)
                        {
                            AVCodecHWConfig* config = ffmpeg.avcodec_get_hw_config(_codec, i);
                            if (config == null)
                                break;
                            if (config->device_type == hwDeviceType)
                            {
                                _hwPixFmt = config->pix_fmt;
                                break;
                            }
                        }


                        //for (int i = 0; ; i++)
                        //{
                        //    AVCodecHWConfig* config = ffmpeg.avcodec_get_hw_config(codec, i);
                        //    if (config == null)
                        //    {
                        //        string decoderName = Marshal.PtrToStringUTF8(new IntPtr(codec->name));
                        //        Console.WriteLine($"Decoder {decoderName} does not support device type {ffmpeg.av_hwdevice_get_type_name(hwDeviceType)}");
                        //        break;
                        //    }

                        //    if ((config->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) == AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX &&
                        //            config->device_type == hwDeviceType)
                        //    {
                        //        hwPixFmt = config->pix_fmt;
                        //        break;
                        //    }
                        //}
                    }
                }

                if (CodecID == AVCodecID.AV_CODEC_ID_MJPEG)
                {
                    _isDecodedFirstKeyFrame = true;
                }

                this.PropertyChanged += FFmpegVideoDecoder_PropertyChanged;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
            }
        }
        #endregion

        #region Events
        public event VideoFrameDecodedEventHandler? VideoFrameDecoded;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Properties    
        public bool UseFilter { get; set; } = false;

        public float Brightness
        {
            get => _brightness;
            set
            {
                if (_brightness != value)
                {
                    _brightness = value;
                    OnPropertyChanged();
                }
            }
        }

        public float Contrast
        {
            get => _contrast;
            set
            {
                if (_contrast != value)
                {
                    _contrast = value;
                    OnPropertyChanged();
                }
            }
        }

        public float Saturation
        {
            get => _saturation;
            set
            {
                if (_saturation != value)
                {
                    _saturation = value;
                    OnPropertyChanged();
                }
            }
        }

        public float Hue
        {
            get => _hue;
            set
            {
                if (_hue != value)
                {
                    _hue = value;
                    OnPropertyChanged();
                }
            }
        }

        public float Gamma
        {
            get => _gamma;
            set
            {
                if (_gamma != value)
                {
                    _gamma = value;
                    OnPropertyChanged();
                }
            }
        }

        public float Gamma_R
        {
            get => _gamma_r;
            set
            {
                if (_gamma_r != value)
                {
                    _gamma_r = value;
                    OnPropertyChanged();
                }
            }
        }

        public float Gamma_G
        {
            get => _gamma_g;
            set
            {
                if (_gamma_g != value)
                {
                    _gamma_g = value;
                    OnPropertyChanged();
                }
            }
        }

        public float Gamma_B
        {
            get => _gamma_b;
            set
            {
                if (_gamma_b != value)
                {
                    _gamma_b = value;
                    OnPropertyChanged();
                }
            }
        }

        public float Gamma_Weight
        {
            get => _gamma_weight;
            set
            {
                if (_gamma_weight != value)
                {
                    _gamma_weight = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDecodedFirstKeyFrame => _isDecodedFirstKeyFrame;
        public unsafe AVCodecID CodecID => _codec->id;

        #endregion

        #region Private / Protected Methods
        // H.265 코덱 확인
        private unsafe bool IsH265Codec()
        {
            return _codecContext != null && _codecContext->codec_id == AVCodecID.AV_CODEC_ID_HEVC;
        }


        protected override void OnDispose()
        {
            try
            {
                this.PropertyChanged -= FFmpegVideoDecoder_PropertyChanged;

                unsafe
                {
                    if (_codecContext != null)
                    {
                        ffmpeg.avcodec_send_packet(_codecContext, null);

                        // 2. 프레임 수신: 드레인 모드 후, 디코더 버퍼에 남아 있는 프레임들을 수신합니다.
                        AVFrame* frame = ffmpeg.av_frame_alloc();
                        try
                        {
                            while (ffmpeg.avcodec_receive_frame(_codecContext, frame) == 0)
                            {
                                ffmpeg.av_frame_unref(frame);
                            }
                        }
                        finally
                        {
                            ffmpeg.av_frame_free(&frame);
                            //frame = (AVFrame*)IntPtr.Zero;
                        }

                        AVCodecContext* codecContext = _codecContext;
                        ffmpeg.avcodec_free_context(&codecContext);
                        _codecContext = null;
                    }

                    FFmpegHelper.FreeFrame(ref _decodedFrame);
                    FFmpegHelper.FreeFrame(ref _hwDecodedFrame);
                    FFmpegHelper.FreeFrame(ref _convertedFrame);

                    FFmpegHelper.FreePacket(ref _packet);
                }
                _converter?.Dispose();
                _filter?.Dispose();

                _converter = null;
                _filter = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
            }
        }

        private void FFmpegVideoDecoder_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Brightness):
                case nameof(Contrast):
                case nameof(Saturation):
                case nameof(Hue):
                case nameof(Gamma):
                case nameof(Gamma_R):
                case nameof(Gamma_G):
                case nameof(Gamma_B):
                case nameof(Gamma_Weight):
                    {
                        if (UseFilter)
                        {
                            PropertyInfo? sourceProperty = GetType().GetProperty(e.PropertyName);
                            if (sourceProperty == null)
                                return;

                            object? value = sourceProperty.GetValue(this);

                            PropertyInfo? targetProperty = _filter?.GetType().GetProperty(e.PropertyName);
                            if (targetProperty != null && targetProperty.CanWrite)
                            {
                                targetProperty.SetValue(_filter, value);
                            }
                        }
                    }
                    break;
            }
        }

        private unsafe int Set_video_decoder_extradata(IntPtr extradata, int extradataLength)
        {
            try
            {
                if (_codecContext != null)
                {
                    var codecContext = _codecContext;
                    ffmpeg.avcodec_free_context(&codecContext);
                    _codecContext = null;
                }

                byte* newExtradata = (byte*)ffmpeg.av_malloc((ulong)(extradataLength + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
                if (newExtradata == null)
                    return -2;

                //Unsafe.CopyBlockUnaligned(newExtradata, (void*)extradata, (uint)extradataLength);                
                //Buffer.MemoryCopy((byte*)extradata, newExtradata, extradataLength, extradataLength);
                Win32.MemCopy((IntPtr)newExtradata, (IntPtr)extradata, (UIntPtr)extradataLength);
                //Unsafe.CopyBlock(newExtradata, (void*)extradata, (uint)extradataLength);

                _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
                if (_codecContext == null)
                {
                    ffmpeg.av_free(newExtradata);
                    return -4;
                }

                _codecContext->extradata = newExtradata;
                _codecContext->extradata_size = extradataLength;

                switch (_hwDeviceType)
                {
                    case AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA:
                    case AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2:
                        ffmpeg.av_hwdevice_ctx_create(&_codecContext->hw_device_ctx, _hwDeviceType, null, null, 0).ThrowExceptionIfError();
                        _codecContext->get_format = (AVCodecContext_get_format_func)Get_hw_format;
                        break;
                }

                if (ffmpeg.avcodec_open2(_codecContext, _codec, null) < 0)
                {
                    ffmpeg.av_free(newExtradata);
                    return -3;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                return -99; // 예외 코드 반환
            }

            return 0;
        }

        //private unsafe int set_video_decoder_extradata(IntPtr extradata, int extradataLength)
        //{
        //    try
        //    {
        //        byte* newExtradata = null;
        //        if (codecContext->extradata == null || codecContext->extradata_size < extradataLength)
        //        {
        //            // 이미 할당된 extradata의 크기가 충분한지 확인
        //            if (codecContext->extradata != null)
        //            {
        //                // 이미 할당된 extradata의 크기가 충분하지 않으면 해제
        //                ffmpeg.av_free(codecContext->extradata);
        //            }
        //            newExtradata = (byte*)ffmpeg.av_malloc(Convert.ToUInt64(extradataLength + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
        //            if (newExtradata == null)
        //            {
        //                return -2;
        //            }
        //            //ffmpeg.av_memcpy_backptr(codecContext->extradata, 0, extradataLength + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE);
        //        }


        //        if (newExtradata != null)
        //        {
        //            Unsafe.CopyBlockUnaligned(newExtradata, (void*)extradata, (uint)extradataLength);
        //            //Buffer.MemoryCopy((byte*)extradata, newExtradata, extradataLength, extradataLength);
        //            //Win32.memcpy((IntPtr)newExtradata, (IntPtr)extradata, (UIntPtr)extradataLength);
        //            //Unsafe.CopyBlock(newExtradata, (void*)extradata, (uint)extradataLength);

        //            var _codecContext = codecContext;
        //            ffmpeg.avcodec_free_context(&_codecContext);

        //            codecContext = ffmpeg.avcodec_alloc_context3(codec);
        //            if (codecContext == null)
        //            {
        //                ffmpeg.av_free(newExtradata); // 실패 시 할당된 메모리 해제
        //                return -4; // codecContext 할당 실패
        //            }
        //            else
        //            {
        //                // 새로운 extradata 설정
        //                codecContext->extradata = newExtradata;
        //                codecContext->extradata_size = extradataLength;
        //            }

        //            switch (hwDeviceType)
        //            {
        //                case AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA:
        //                case AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2:
        //                    {
        //                        ffmpeg.av_hwdevice_ctx_create(&codecContext->hw_device_ctx, hwDeviceType, null, null, 0).ThrowExceptionIfError();
        //                        codecContext->get_format = (AVCodecContext_get_format_func)get_hw_format;
        //                    }
        //                    break;
        //            }

        //            if (ffmpeg.avcodec_open2(codecContext, codec, null) < 0)
        //            {
        //                return -3;
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "{Message}", ex.Message);
        //    }

        //    return 0;
        //}

        private unsafe AVPixelFormat Get_hw_format(AVCodecContext* ctx, AVPixelFormat* pix_fmts)
        {
            AVPixelFormat* p;

            for (p = pix_fmts; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
            {
                if (*p == _hwPixFmt)
                {
                    return *p;
                }
            }

            return AVPixelFormat.AV_PIX_FMT_NONE;
        }

        private unsafe bool TryConvertFrame(AVFrame* srcFrame, Size targetSize, AVPixelFormat targetPixelFormat, AVFrame* targetFrame)
        {
            targetFrame = null;

            if (targetSize == Size.Empty && targetPixelFormat == AVPixelFormat.AV_PIX_FMT_NONE)
            {
                return false; // 변환이 필요 없을 경우
            }

            bool isSameSize = srcFrame->width == targetSize.Width && srcFrame->height == targetSize.Height;
            bool isSameFormat = srcFrame->format == (int)targetPixelFormat;
            if (isSameSize && isSameFormat)
            {
                return false;
            }

            bool requiresNewConverter = _converter == null ||
                                   (targetSize != Size.Empty && targetSize != _converter.DstSize) ||
                                   (targetPixelFormat != AVPixelFormat.AV_PIX_FMT_NONE && targetPixelFormat != _converter.DstFixFmt);


            if (requiresNewConverter)
            {
                _converter?.Dispose();
                _converter = new FFmpegVideoConverter(new Size(srcFrame->width, srcFrame->height), (AVPixelFormat)srcFrame->format,
                    targetSize == Size.Empty ? new Size(srcFrame->width, srcFrame->height) : targetSize,
                    targetPixelFormat == AVPixelFormat.AV_PIX_FMT_NONE ? (AVPixelFormat)srcFrame->format : targetPixelFormat);
            }

            ffmpeg.av_frame_unref(_convertedFrame);

            try
            {
                return _converter!.TryConvert(srcFrame, _convertedFrame);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                return false;
            }
        }


        private unsafe AVFrame* ApplyFilter(AVFrame* srcFrame)
        {
            if (_filter == null || _filter.Width != srcFrame->width ||
                _filter.Height != srcFrame->height || _filter.PixelFormat != (AVPixelFormat)srcFrame->format)
            {
                _filter?.Dispose();
                _filter = new FFmpegVideoFilter(srcFrame->width, srcFrame->height,
                    (AVPixelFormat)srcFrame->format, new AVRational() { den = 1, num = 30 },
                    Brightness, Contrast, Saturation, Hue, Gamma, Gamma_R, Gamma_G, Gamma_B, Gamma_Weight);
            }
            return _filter.TryFilter(srcFrame);
        }


        private unsafe AVFrame* GetSourceFrame()
        {
            AVFrame* resultFrame = null;

            switch (_hwDeviceType)
            {
                case AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA:
                case AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2:
                    {
                        if (_hwDecodedFrame == null)
                            _hwDecodedFrame = ffmpeg.av_frame_alloc();
                        else
                            ffmpeg.av_frame_unref(_hwDecodedFrame);

                        //copy from GPU to CPU                                             
                        if (ffmpeg.av_hwframe_transfer_data(_hwDecodedFrame, _decodedFrame, 0) == 0)
                        {
                            resultFrame = _hwDecodedFrame;
                            //int size = ffmpeg.av_image_get_buffer_size((AVPixelFormat)hwDecodedFrame->format, hwDecodedFrame->width, hwDecodedFrame->height, 1);                            
                            //if (tmpBuffer == null)
                            //    tmpBuffer = (byte*)ffmpeg.av_malloc((ulong)size);
                            //if (tmpBuffer != null)
                            //{
                            //    try
                            //    {
                            //        byte_ptr4* ptrFrameData = (byte_ptr4*)&hwDecodedFrame->data;
                            //        int4* ptrLineSize = (int4*)&hwDecodedFrame->linesize;
                            //        int ret = ffmpeg.av_image_copy_to_buffer(tmpBuffer, size, *ptrFrameData, *ptrLineSize, (AVPixelFormat)hwDecodedFrame->format, hwDecodedFrame->width, hwDecodedFrame->height, 1);                                    
                            //        if (ret > 0)
                            //        {
                            //            byte_ptr4 data = new byte_ptr4();
                            //            int4 linesize = new int4();                  
                            //            // AVFrame에 이미지 데이터 설정
                            //            //ret = ffmpeg.av_image_fill_arrays(ref data, ref linesize, tmpBuffer, (AVPixelFormat)hwDecodedFrame->format, hwDecodedFrame->width, hwDecodedFrame->height, 1);
                            //            ret = ffmpeg.av_image_fill_arrays(ref data, ref linesize, tmpBuffer, AVPixelFormat.AV_PIX_FMT_YUV420P, hwDecodedFrame->width, hwDecodedFrame->height, 1);
                            //            if (ret > 0)
                            //            {
                            //                ret = ffmpeg.av_hwframe_transfer_data(cpuFrame, hwDecodedFrame, 0);

                            //                cpuFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
                            //                cpuFrame->width = hwDecodedFrame->width;
                            //                cpuFrame->height = hwDecodedFrame->height;
                            //                cpuFrame->linesize[0] = linesize[0];
                            //                cpuFrame->linesize[1] = linesize[1];
                            //                cpuFrame->linesize[2] = linesize[2];
                            //                cpuFrame->data[0] = data[0];
                            //                cpuFrame->data[1] = data[1];
                            //                cpuFrame->data[2] = data[2];

                            //                //result = cpuFrame;
                            //                return cpuFrame;
                            //            }
                            //        }
                            //    }
                            //    catch (Exception ex)
                            //    {
                            //    }
                            //}                                           
                        }
                    }
                    break;
                default:
                    {
                        if (_decodedFrame->width > 0 && _decodedFrame->height > 0 && _decodedFrame->format != (int)AVPixelFormat.AV_PIX_FMT_NONE)
                            resultFrame = _decodedFrame;
                    }
                    break;
            }

            return resultFrame;
        }
        #endregion

        #region Public Methods        

        public unsafe AVFrame* Decode(RawVideoFrame rawVideoFrame, Size convertSize, AVPixelFormat convertPixelFormat)
        {
            if (IsDisposed)
                return null;

            if (rawVideoFrame.FrameData.IsEmpty)
            {
                _pendingParameterSets.Add(rawVideoFrame.ParameterSets);
                return null;
            }

            var parameterSets = rawVideoFrame.ParameterSets;
            int parameterSetsLength = parameterSets.Length;

            // 파라미터 세트 처리 최적화
            if (parameterSetsLength > 0 && _extraDataLength != parameterSetsLength)
            {
                _extraDataLength = parameterSetsLength;
                fixed (byte* ptr = parameterSets.Span)
                {
                    Set_video_decoder_extradata((IntPtr)ptr, _extraDataLength);
                }

                ffmpeg.avcodec_flush_buffers(_codecContext);
            }

            // 첫 번째 키프레임 처리 최적화
            if (!_isDecodedFirstKeyFrame)
            {
                if (!rawVideoFrame.IsKeyFrame || _extraDataLength == 0)
                    return null;

                if (rawVideoFrame is RawH265IFrame h265 && !h265.HasValidParameterSets)
                    return null;

                _isDecodedFirstKeyFrame = true;
            }

            // 펜딩 파라미터 세트 처리 최적화
            int pendingCount = _pendingParameterSets.Count;
            if (pendingCount > 0)
            {
                // 총 길이 계산 최적화
                int totalLength = 0;
                var pendingSpan = CollectionsMarshal.AsSpan(_pendingParameterSets);

                for (int i = 0; i < pendingCount; i++)
                {
                    totalLength += pendingSpan[i].Length;
                }

                // 작은 버퍼는 스택 할당 사용, 큰 버퍼는 ArrayPool 사용
                byte[] combined = totalLength <= _tempBuffer.Length ? _tempBuffer : System.Buffers.ArrayPool<byte>.Shared.Rent(totalLength);

                try
                {
                    int offset = 0;
                    for (int i = 0; i < pendingCount; i++)
                    {
                        var ps = pendingSpan[i];
                        ps.Span.CopyTo(combined.AsSpan(offset, ps.Length));
                        offset += ps.Length;
                    }

                    if (_extraDataLength != totalLength)
                    {
                        _extraDataLength = totalLength;
                        fixed (byte* ptr = combined)
                        {
                            Set_video_decoder_extradata((IntPtr)ptr, _extraDataLength);
                        }

                        ffmpeg.avcodec_flush_buffers(_codecContext);
                    }
                }
                finally
                {
                    if (totalLength > _tempBuffer.Length)
                        System.Buffers.ArrayPool<byte>.Shared.Return(combined);

                    _pendingParameterSets.Clear();
                }
            }

            try
            {
                ffmpeg.av_packet_unref(_packet);

                ReadOnlyMemory<byte> frameData = rawVideoFrame.FrameData;
                int frameSize = frameData.Length;
                long timestamp = rawVideoFrame.Timestamp;
                int flags = rawVideoFrame.IsKeyFrame ? ffmpeg.AV_PKT_FLAG_KEY : 0;

                fixed (byte* rawDataPtr = frameData.Span)
                {
                    _packet->data = rawDataPtr;
                    _packet->size = frameSize;
                    _packet->pts = timestamp;
                    _packet->dts = timestamp;
                    _packet->flags = flags;
                }

                if (ffmpeg.avcodec_send_packet(_codecContext, _packet) != 0)
                    return null;

                ffmpeg.av_frame_unref(_decodedFrame);

                int ret = ffmpeg.avcodec_receive_frame(_codecContext, _decodedFrame);
                if (ret == ffmpeg.EAGAIN || ret == ffmpeg.AVERROR_EOF)
                    return null;

                if (ret < 0)
                {
                    Console.WriteLine($"Failed to decode the video frame. Error={FFmpegHelper.Av_strerror(ret)}.");
                    return null;
                }

                AVFrame* srcFrame = GetSourceFrame();
                if (srcFrame == null)
                    return null;

                if (UseFilter)
                    srcFrame = ApplyFilter(srcFrame);

                AVFrame* dstFrame = TryConvertFrame(srcFrame, convertSize, convertPixelFormat, _convertedFrame) ?
                    _convertedFrame : srcFrame;

                return dstFrame;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                return null;
            }
            finally
            {
                rawVideoFrame?.Dispose();
            }
        }

        public unsafe bool TryDecode(RawVideoFrame rawVideoFrame, Size convertSize, AVPixelFormat convertPixelFormat)
        {
            if (IsDisposed)
                return false;

            if (rawVideoFrame.FrameData.IsEmpty)
            {
                _pendingParameterSets.Add(rawVideoFrame.ParameterSets);
                return false;
            }

            var parameterSets = rawVideoFrame.ParameterSets;
            int parameterSetsLength = parameterSets.Length;

            // 파라미터 세트 처리 최적화
            if (parameterSetsLength > 0 && _extraDataLength != parameterSetsLength)
            {
                _extraDataLength = parameterSetsLength;
                fixed (byte* ptr = parameterSets.Span)
                {
                    Set_video_decoder_extradata((IntPtr)ptr, _extraDataLength);
                }

                ffmpeg.avcodec_flush_buffers(_codecContext);
            }

            // 첫 번째 키프레임 처리 최적화
            if (!_isDecodedFirstKeyFrame)
            {
                if (!rawVideoFrame.IsKeyFrame || _extraDataLength == 0)
                    return false;

                if (rawVideoFrame is RawH265IFrame h265 && !h265.HasValidParameterSets)
                    return false;

                _isDecodedFirstKeyFrame = true;
            }

            // 펜딩 파라미터 세트 처리 최적화
            int pendingCount = _pendingParameterSets.Count;
            if (pendingCount > 0)
            {
                // 총 길이 계산 최적화
                int totalLength = 0;
                var pendingSpan = CollectionsMarshal.AsSpan(_pendingParameterSets);

                for (int i = 0; i < pendingCount; i++)
                {
                    totalLength += pendingSpan[i].Length;
                }

                // 작은 버퍼는 스택 할당 사용, 큰 버퍼는 ArrayPool 사용
                byte[] combined = totalLength <= _tempBuffer.Length ? _tempBuffer : System.Buffers.ArrayPool<byte>.Shared.Rent(totalLength);

                try
                {
                    int offset = 0;
                    for (int i = 0; i < pendingCount; i++)
                    {
                        var ps = pendingSpan[i];
                        ps.Span.CopyTo(combined.AsSpan(offset, ps.Length));
                        offset += ps.Length;
                    }

                    if (_extraDataLength != totalLength)
                    {
                        _extraDataLength = totalLength;
                        fixed (byte* ptr = combined)
                        {
                            Set_video_decoder_extradata((IntPtr)ptr, _extraDataLength);
                        }

                        ffmpeg.avcodec_flush_buffers(_codecContext);
                    }
                }
                finally
                {
                    if (totalLength > _tempBuffer.Length)
                        System.Buffers.ArrayPool<byte>.Shared.Return(combined);

                    _pendingParameterSets.Clear();
                }
            }

            try
            {
                ffmpeg.av_packet_unref(_packet);

                ReadOnlyMemory<byte> frameData = rawVideoFrame.FrameData;
                int frameSize = frameData.Length;
                long timestamp = rawVideoFrame.Timestamp;
                int flags = rawVideoFrame.IsKeyFrame ? ffmpeg.AV_PKT_FLAG_KEY : 0;

                fixed (byte* rawDataPtr = frameData.Span)
                {
                    _packet->data = rawDataPtr;
                    _packet->size = frameSize;
                    _packet->pts = timestamp;
                    _packet->dts = timestamp;
                    _packet->flags = flags;
                }

                if (ffmpeg.avcodec_send_packet(_codecContext, _packet) != 0)
                    return false;

                ffmpeg.av_frame_unref(_decodedFrame);

                int ret = ffmpeg.avcodec_receive_frame(_codecContext, _decodedFrame);
                if (ret == ffmpeg.EAGAIN || ret == ffmpeg.AVERROR_EOF)
                    return false;

                if (ret < 0)
                {
                    Console.WriteLine($"Failed to decode the video frame. Error={FFmpegHelper.Av_strerror(ret)}.");
                    return false;
                }

                AVFrame* srcFrame = GetSourceFrame();
                if (srcFrame == null)
                    return false;

                if (UseFilter)
                    srcFrame = ApplyFilter(srcFrame);

                AVFrame* dstFrame = TryConvertFrame(srcFrame, convertSize, convertPixelFormat, _convertedFrame) ?
                    _convertedFrame : srcFrame;

                VideoFrameDecoded?.Invoke(this, new VideoFrameDecodedEventArgs(_codec->id, dstFrame));

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                return false;
            }
            finally
            {
                rawVideoFrame?.Dispose();
            }
        }



        //public unsafe AVFrame* TryDecode(RawVideoFrame rawVideoFrame, Size convertSize, AVPixelFormat convertPixelFormat)
        //{            
        //    if (isDisposed || rawVideoFrame == null)
        //        return null;                       

        //    switch (rawVideoFrame)
        //    {
        //        case RawH264IFrame rawH264IFrame:
        //            {
        //                try
        //                {
        //                    if (rawH264IFrame.SpsPpsSegment.Array != null &&
        //                        !_extraData.SequenceEqual(rawH264IFrame.SpsPpsSegment))
        //                    {
        //                        if (_extraData.Length != rawH264IFrame.SpsPpsSegment.Count)
        //                            _extraData = new byte[rawH264IFrame.SpsPpsSegment.Count];

        //                        Buffer.BlockCopy(rawH264IFrame.SpsPpsSegment.Array, rawH264IFrame.SpsPpsSegment.Offset,
        //                            _extraData, 0, rawH264IFrame.SpsPpsSegment.Count);
        //                    }

        //                    fixed (byte* initDataPtr = _extraData)
        //                    {
        //                        if (set_video_decoder_extradata((IntPtr)initDataPtr, _extraData.Length) != 0)
        //                        {
        //                            //LogWriter.WriteLogEntry($"An error occurred while setting video extra data, {codec->id} codec");
        //                        }
        //                    }

        //                    if (!isDecodedFirstKeyFrame)
        //                        isDecodedFirstKeyFrame = true;
        //                }
        //                catch (Exception ex)
        //                {

        //                }
        //            }
        //            break;
        //        default:
        //            {
        //                if (!isDecodedFirstKeyFrame)
        //                    return null;
        //            }
        //            break;
        //    }
        //    //do
        //    //{
        //    //    if (isDisposed)
        //    //        return null;               

        //    //    try
        //    //    {
        //    //        ffmpeg.av_frame_unref(decodedFrame);

        //    //        fixed (byte* rawDataPtr = rawVideoFrame.FrameSegment.Array)
        //    //        {
        //    //            packet->data = rawDataPtr;
        //    //            packet->size = rawVideoFrame.FrameSegment.Count;
        //    //        }

        //    //        if (ffmpeg.avcodec_send_packet(codecContext, packet) == 0)
        //    //            ret = ffmpeg.avcodec_receive_frame(codecContext, decodedFrame);
        //    //    }
        //    //    catch (Exception ex)
        //    //    {
        //    //        LogWriter.WriteLogEntry(ex);
        //    //    }

        //    //} while (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN));            

        //    AVFrame* result = null;
        //    int ret = 0;

        //    ffmpeg.av_packet_unref(packet);
        //    try
        //    {
        //        fixed (byte* rawDataPtr = rawVideoFrame.FrameSegment.Array)
        //        {
        //            packet->data = rawDataPtr;
        //            packet->size = rawVideoFrame.FrameSegment.Count;
        //        }

        //        ret = ffmpeg.avcodec_send_packet(codecContext, packet);                
        //    }
        //    catch (Exception ex)
        //    {

        //    }

        //    if (ret != 0)
        //    {
        //        ffmpeg.av_packet_unref(packet);
        //        return null;
        //    }

        //    try
        //    {
        //        ffmpeg.av_frame_unref(decodedFrame);
        //        ret = ffmpeg.avcodec_receive_frame(codecContext, decodedFrame);
        //    }
        //    catch (Exception ex)
        //    {

        //    }

        //    if (ret != 0)
        //    {
        //        ffmpeg.av_frame_unref(decodedFrame);
        //        return null;
        //    }

        //    result = decodedFrame;

        //    if (codecContext->hw_device_ctx != null)
        //    {
        //        ffmpeg.av_frame_unref(hwDecodedFrame);
        //        try
        //        {

        //            if (ffmpeg.av_hwframe_transfer_data(hwDecodedFrame, decodedFrame, 0) >= 0)
        //                result = hwDecodedFrame;
        //        }
        //        catch (Exception ex)
        //        {

        //        }
        //    }

        //    if (result->width <= 0 || result->height <= 0 || result->format == (int)AVPixelFormat.AV_PIX_FMT_NONE)
        //    {
        //        return null;
        //    }

        //    if (result != null && (convertSize != Size.Empty || convertPixelFormat != AVPixelFormat.AV_PIX_FMT_NONE))
        //    {
        //        try
        //        {
        //            if (converter != null && ((convertSize != Size.Empty && convertSize != converter.DstSize) || convertPixelFormat != converter.DstFixFmt))
        //            {
        //                converter.Dispose();
        //                converter = null;
        //            }

        //            if (converter == null)
        //            {
        //                converter = new FFmpegVideoConverter(new Size(result->width, result->height), (AVPixelFormat)result->format,
        //                                                            convertSize == Size.Empty ? new Size(result->width, result->height) : convertSize,
        //                                                            convertPixelFormat == AVPixelFormat.AV_PIX_FMT_NONE ? (AVPixelFormat)result->format : convertPixelFormat);
        //            }

        //            ffmpeg.av_frame_unref(convertedFrame);

        //            if (converter.TryConvert(decodedFrame, convertedFrame))
        //            {
        //                result = convertedFrame;
        //            }
        //            else
        //            {
        //                //LogWriter.WriteLogEntry("[Decoder] Converter Fail");

        //                //AVFrame* tmpFrame = convertedFrame;
        //                //ffmpeg.av_frame_unref(tmpFrame);
        //                //ffmpeg.av_frame_free(&tmpFrame);
        //                //convertedFrame = (AVFrame*)IntPtr.Zero;

        //                converter.Dispose();
        //                converter = null;

        //                //convertedFrame = ffmpeg.av_frame_alloc();

        //                result = null;
        //            }
        //        }
        //        catch (Exception ex)
        //        {

        //        }
        //    }

        //    return result;
        //}
        #endregion
    }
}


