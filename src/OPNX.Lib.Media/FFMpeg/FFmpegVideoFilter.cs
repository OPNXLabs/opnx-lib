using FFmpeg.AutoGen;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OPNX.Lib.Media.FFMpeg
{
    public class FFmpegVideoFilter : IDisposable, INotifyPropertyChanged
    {
        #region Fields
        private unsafe AVFilterGraph* _filterGraph = null;
        private unsafe AVFilterContext* _bufferSrcContext = null;
        private unsafe AVFilterContext* _bufferSinkContext = null;

        private float _brightness = 0.0f;            //-1.0~1.0 기본값 : 0
        private float _contrast = 1.0f;              //-1000.0~1000.0 기본값 : 1
        private float _saturation = 1.0f;            //0.0~3.0 기본값 : 1

        private float _hue = 0.0f;                   //-360~360 기본값 0

        private float _gamma = 1.0f;                 //0.1~10.0 기본값 : 1
        private float _gamma_r = 1.0f;               //0.1~10.0 기본값 : 1
        private float _gamma_g = 1.0f;               //0.1~10.0 기본값 : 1
        private float _gamma_b = 1.0f;               //0.1~10.0 기본값 : 1
        private float _gamma_weight = 1.0f;          //0.1~10.0 기본값 : 1

        private unsafe AVFrame* _filterFrame = null;

        private readonly int _width;
        private readonly int _height;
        private readonly AVPixelFormat _pixelFormat;
        private readonly AVRational _timebase;
        #endregion

        #region Constructors
        public FFmpegVideoFilter(int width, int height, AVPixelFormat pixelFormat, AVRational timebase,
              float brightness = 0.0f, float contrast = 1.0f, float saturation = 1.0f, float hue = 0.0f,
              float gamma = 1.0f, float gamma_r = 1.0f, float gamma_g = 1.0f,
              float gamma_b = 1.0f, float gamma_weight = 1.0f)
        {
            _width = width;
            _height = height;
            _pixelFormat = pixelFormat;
            _timebase = timebase;

            _brightness = brightness;
            _contrast = contrast;
            _saturation = saturation;
            _hue = hue;
            _gamma = gamma;
            _gamma_r = gamma_r;
            _gamma_g = gamma_g;
            _gamma_b = gamma_b;
            _gamma_weight = gamma_weight;

            this.PropertyChanged += FFmpegVideoFilter_PropertyChanged;

            InitializeFilterGraph(width, height, pixelFormat, timebase);
        }
        #endregion

        #region Properties
        public int Width => _width;
        public int Height => _height;
        public AVPixelFormat PixelFormat => _pixelFormat;
        public AVRational TimeBase => _timebase;

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
        #endregion

        #region Events
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Public Methods
        public unsafe AVFrame* TryFilter(AVFrame* srcFrame)
        {
            if (_filterFrame == null)
                _filterFrame = ffmpeg.av_frame_alloc();
            else
                ffmpeg.av_frame_unref(_filterFrame);

            if (ffmpeg.av_buffersrc_add_frame(_bufferSrcContext, srcFrame) < 0)
                throw new Exception("Failed to add the frame to the buffer source.");

            if (ffmpeg.av_buffersink_get_frame(_bufferSinkContext, _filterFrame) < 0)
                throw new Exception("Failed to get the frame from the buffer sink.");

            return _filterFrame;
        }
        #endregion

        #region Private / Protected Methods
        private void FFmpegVideoFilter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
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
                        InitializeFilterGraph(_width, _height, _pixelFormat, _timebase);
                    }
                    break;
            }
        }

        private unsafe void InitializeFilterGraph(int width, int height, AVPixelFormat pixelFormat, AVRational timebase)
        {
            if (_filterGraph != null)
            {
                var filterGraph = _filterGraph;
                ffmpeg.avfilter_graph_free(&filterGraph);
                _filterGraph = null;
            }

            _filterGraph = ffmpeg.avfilter_graph_alloc();
            if (_filterGraph is null)
                throw new Exception("Failed to allocate the filter graph.");

            var buffersrc = ffmpeg.avfilter_get_by_name("buffer");
            var buffersink = ffmpeg.avfilter_get_by_name("buffersink");

            AVFilterInOut* outputs = ffmpeg.avfilter_inout_alloc();
            AVFilterInOut* inputs = ffmpeg.avfilter_inout_alloc();

            if (outputs is null || inputs is null)
            {
                throw new Exception("Failed to allocate the filter input and output.");
            }

            // 필터 인자 설정
            string args = $"video_size={width}x{height}:pix_fmt={(int)pixelFormat}:" +
                          $"time_base={timebase.num}/{timebase.den}:pixel_aspect=1/1";

            // 입력 필터 생성
            fixed (AVFilterContext** pbuffersrc_ctx = &_bufferSrcContext)
            {
                int ret = ffmpeg.avfilter_graph_create_filter(pbuffersrc_ctx, buffersrc, "in", args, null, _filterGraph);
                if (ret < 0)
                {
                    string errorMsg = $"Failed to create the buffer source. ErrorCode={ret}.";
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, errorMsg);
                    throw new Exception(errorMsg);
                }
            }

            // 출력 필터 생성
            fixed (AVFilterContext** pbuffersink_ctx = &_bufferSinkContext)
            {
                AVBufferSrcParameters* buffersink_params = ffmpeg.av_buffersrc_parameters_alloc();
                buffersink_params->format = (int)pixelFormat;

                int ret = ffmpeg.avfilter_graph_create_filter(pbuffersink_ctx, buffersink, "out", null, buffersink_params, _filterGraph);
                ffmpeg.av_free(buffersink_params);
                if (ret < 0)
                {
                    string errorMsg = $"Failed to create the buffer sink. ErrorCode={ret}.";
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, errorMsg);
                    throw new Exception(errorMsg);
                }
            }

            // 픽셀 포맷 설정
            //Span<int> pix_fmts = stackalloc int[] { (int)pixelFormat };
            Span<int> pix_fmts = [(int)pixelFormat];
            fixed (int* pfmts = pix_fmts)
            {
                int ret = ffmpeg.av_opt_set_bin(_bufferSinkContext, "pix_fmts", (byte*)pfmts, pix_fmts.Length * sizeof(int), ffmpeg.AV_OPT_SEARCH_CHILDREN);
                if (ret < 0)
                {
                    string errorMsg = $"Failed to set the output pixel format. ErrorCode={ret}.";
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, errorMsg);
                    throw new Exception(errorMsg);
                }
            }

            // 필터 연결
            outputs->name = ffmpeg.av_strdup("in");
            outputs->filter_ctx = _bufferSrcContext;
            outputs->pad_idx = 0;
            outputs->next = null;

            inputs->name = ffmpeg.av_strdup("out");
            inputs->filter_ctx = _bufferSinkContext;
            inputs->pad_idx = 0;
            inputs->next = null;

            // 필터 그래프 파싱 및 구성
            string filter_descr = $"eq=contrast={Contrast}:brightness={Brightness}:saturation={Saturation}:gamma={Gamma}:gamma_r={Gamma_R}:gamma_g={Gamma_G}:gamma_b={Gamma_B}:gamma_weight={Gamma_Weight}, hue=h={Hue}";
            int ret2 = ffmpeg.avfilter_graph_parse_ptr(_filterGraph, filter_descr, &inputs, &outputs, null);
            if (ret2 < 0)
            {
                string errorMsg = $"Failed to parse the filter graph. ErrorCode={ret2}.";
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, errorMsg);
                throw new Exception(errorMsg);
            }

            int ret3 = ffmpeg.avfilter_graph_config(_filterGraph, null);
            if (ret3 < 0)
            {
                string errorMsg = $"Failed to configure the filter graph. ErrorCode={ret3}.";
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, errorMsg);
                throw new Exception(errorMsg);
            }

            // 필터 인, 아웃 객체 메모리 해제
            ffmpeg.avfilter_inout_free(&inputs);
            ffmpeg.avfilter_inout_free(&outputs);
        }
        #endregion

        #region IDisposable
        public unsafe void Dispose()
        {
            this.PropertyChanged -= FFmpegVideoFilter_PropertyChanged;

            if (_filterGraph != null)
            {
                var filterGraph = _filterGraph;
                ffmpeg.avfilter_graph_free(&filterGraph);
                _filterGraph = null;
            }

            FFmpegHelper.FreeFrame(ref _filterFrame);

            _bufferSrcContext = null;
            _bufferSinkContext = null;

            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
