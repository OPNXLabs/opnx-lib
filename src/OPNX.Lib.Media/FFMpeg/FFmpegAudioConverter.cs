using FFmpeg.AutoGen;
using OPNX.Lib.Common.LifeCycle;

namespace OPNX.Lib.Media.FFMpeg
{
    public sealed unsafe class FFmpegAudioConverter : DisposableBase
    {
        #region Fields
        private readonly SwrContext* _swrContext;

        private readonly AVSampleFormat _srcFormat;
        private readonly int _srcSampleRate;
        private readonly int _srcChannels;

        private readonly AVSampleFormat _dstFormat;
        private readonly int _dstSampleRate;
        private readonly int _dstChannels;

        private AVChannelLayout _inChannelLayout;
        private AVChannelLayout _outChannelLayout;
        #endregion;

        #region Constructors
        public FFmpegAudioConverter(AVSampleFormat srcFormat, int srcSampleRate, int srcChannels,
                                    AVSampleFormat dstFormat, int dstSampleRate, int dstChannels)
        {
            _srcFormat = srcFormat;
            _srcSampleRate = srcSampleRate;
            _srcChannels = srcChannels;

            _dstFormat = dstFormat;
            _dstSampleRate = dstSampleRate;
            _dstChannels = dstChannels;

            fixed (AVChannelLayout* pInLayout = &_inChannelLayout)
            fixed (AVChannelLayout* pOutLayout = &_outChannelLayout)
            {
                ffmpeg.av_channel_layout_default(pInLayout, srcChannels);
                ffmpeg.av_channel_layout_default(pOutLayout, dstChannels);

                SwrContext* swrTemp = null;

                int ret = ffmpeg.swr_alloc_set_opts2(
                    &swrTemp,
                    pOutLayout, dstFormat, dstSampleRate,
                    pInLayout, srcFormat, srcSampleRate,
                    0,
                    null);

                if (ret < 0 || swrTemp == null)
                    throw new ApplicationException("swr_alloc_set_opts2 failed");

                _swrContext = swrTemp;

                if (_swrContext == null || ffmpeg.swr_init(_swrContext) < 0)
                {
                    fixed (SwrContext** ctx = &_swrContext)
                        ffmpeg.swr_free(ctx);

                    throw new ApplicationException("Could not initialize swr context");
                }
            }
        }
        #endregion

        #region Properties
        public AVSampleFormat DstFormat => _dstFormat;
        public int DstSampleRate => _dstSampleRate;
        public int DstChannels => _dstChannels;
        #endregion

        #region Public Methods

        public bool Convert(AVFrame* srcFrame, AVFrame* dstFrame)
        {
            if (IsDisposed || _swrContext == null)
                return false;

            // 출력 프레임 설정
            dstFrame->format = (int)_dstFormat;
            dstFrame->sample_rate = _dstSampleRate;
            dstFrame->ch_layout = _outChannelLayout;

            // 출력 샘플 수 계산
            int dstNbSamples = (int)ffmpeg.av_rescale_rnd(
                srcFrame->nb_samples,
                _dstSampleRate,
                _srcSampleRate,
                AVRounding.AV_ROUND_UP);

            dstFrame->nb_samples = dstNbSamples;

            // 출력 프레임 버퍼 할당
            int ret = ffmpeg.av_frame_get_buffer(dstFrame, 0);
            if (ret < 0)
                return false;

            // 변환 수행
            int convertedSamples = ffmpeg.swr_convert(
                _swrContext,
                dstFrame->extended_data,
                dstNbSamples,
                srcFrame->extended_data,
                srcFrame->nb_samples);

            if (convertedSamples < 0)
                return false;

            // 실제 변환된 샘플 수로 업데이트
            dstFrame->nb_samples = convertedSamples;

            // 타임스탬프 조정
            if (srcFrame->pts != ffmpeg.AV_NOPTS_VALUE)
            {
                dstFrame->pts = ffmpeg.av_rescale_q(
                    srcFrame->pts,
                    new AVRational { num = 1, den = _srcSampleRate },
                    new AVRational { num = 1, den = _dstSampleRate });
            }

            return true;
        }

        public unsafe int TryConvert(AVFrame* srcFrame, AVFrame* dstFrame)
        {
            if (IsDisposed || _swrContext == null)
                return -1;

            dstFrame->format = (int)_dstFormat;
            dstFrame->nb_samples = srcFrame->nb_samples;
            dstFrame->ch_layout.nb_channels = _dstChannels;
            dstFrame->sample_rate = _dstSampleRate;

            int ret = ffmpeg.av_frame_get_buffer(dstFrame, 0);
            if (ret < 0)
                return ret;

            ret = ffmpeg.swr_convert_frame(_swrContext, dstFrame, srcFrame);

            return ret;
        }

        //public int TryConvert(AVFrame* srcFrame, byte** dstData, int dstNbSamples)
        //{
        //    if (isDisposed || _swrContext == null)
        //        return -1;

        //    int result = ffmpeg.swr_convert(
        //        _swrContext,
        //        dstData,
        //        dstNbSamples,
        //        srcFrame->extended_data,
        //        srcFrame->nb_samples);

        //    return result; // 변환된 샘플 수 또는 에러 코드
        //}
        #endregion

        #region Private / Protected Methods
        protected override void OnDispose()
        {
            fixed (SwrContext** ctx = &_swrContext)
                ffmpeg.swr_free(ctx);
        }
        #endregion
    }
}
