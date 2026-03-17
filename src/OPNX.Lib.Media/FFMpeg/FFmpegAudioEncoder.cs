using FFmpeg.AutoGen;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Media.FFMpeg.EventHandlers;

namespace OPNX.Lib.Media.FFMpeg
{
    public sealed class FFmpegAudioEncoder : DisposableBase
    {
        #region Fields
        private unsafe readonly AVCodec* _codec;
        private unsafe readonly AVCodecContext* _codecContext;
        private unsafe AVPacket* _packet;

        private FFmpegAudioConverter? _converter = null;
        private unsafe AVFrame* _convertedFrame = null;
        #endregion

        #region Constructors
        public FFmpegAudioEncoder(AVCodecID codecID, AVSampleFormat sampleFormat, int sampleRate, int channels, long bitRate)
           : base()
        {
            unsafe
            {
                _codec = ffmpeg.avcodec_find_encoder(codecID);
                ArgumentNullException.ThrowIfNull(_codec);
                
                _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
                ArgumentNullException.ThrowIfNull(_codecContext);                

                _codecContext->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
                _codecContext->codec_id = codecID;
                _codecContext->sample_fmt = sampleFormat;
                _codecContext->sample_rate = sampleRate;
                ffmpeg.av_channel_layout_default(&_codecContext->ch_layout, channels);
                _codecContext->bit_rate = bitRate;

                // 샘플 포맷 지원 확인
                if (!IsSampleFormatSupported(_codecContext, _codec, sampleFormat))
                {
                    var supportedFormat = GetBestSupportedSampleFormat(_codecContext, _codec);
                    if (supportedFormat != AVSampleFormat.AV_SAMPLE_FMT_NONE)
                    {
                        _codecContext->sample_fmt = supportedFormat;
                    }
                    else
                    {
                        throw new Exception($"Encoder does not support sample format: {sampleFormat}");
                    }
                }

                //if (!IsSampleFormatSupportedLegacy(codec, sampleFormat))
                //{
                //    var supportedFormat = GetBestSupportedSampleFormatLegacy(codecContext, codec);
                //    if (supportedFormat != AVSampleFormat.AV_SAMPLE_FMT_NONE)
                //    {
                //        codecContext->sample_fmt = supportedFormat;
                //    }
                //    else
                //    {
                //        throw new Exception($"Encoder does not support sample format: {sampleFormat}");
                //    }
                //}   

                //var bestSampleRate = SelectBestSampleRateLegacy(codec, sampleRate);
                var bestSampleRate = SelectBestSampleRate(_codecContext, _codec, sampleRate);
                if (bestSampleRate != sampleRate)
                {
                    _codecContext->sample_rate = bestSampleRate;
                }

                //var bestChannelLayout = SelectBestChannelLayout(codecContext, codec, channels);
                //codecContext->ch_layout = bestChannelLayout;

                // bits_per_raw_sample 설정
                if (_codecContext->bits_per_raw_sample == 0)
                {
                    _codecContext->bits_per_raw_sample = ffmpeg.av_get_exact_bits_per_sample(_codecContext->codec_id);
                    if (_codecContext->bits_per_raw_sample == 0)
                    {
                        int bytes = ffmpeg.av_get_bytes_per_sample(_codecContext->sample_fmt);
                        if (bytes > 0)
                            _codecContext->bits_per_raw_sample = 8 * bytes;
                    }
                }
                if (ffmpeg.avcodec_open2(_codecContext, _codec, null) != 0)
                {
                    throw new Exception("Fail Audio Codec Open");
                }


                _packet = ffmpeg.av_packet_alloc();
            }
        }
        #endregion

        #region Properties
        public unsafe AVCodecID CodecID => _codecContext == null ? AVCodecID.AV_CODEC_ID_NONE : _codecContext->codec_id;
        #endregion

        #region Static Methods
        private static unsafe AVChannelLayout SelectBestChannelLayout(AVCodecContext* ctx, AVCodec* codec, int preferredChannels)
        {
            int num_layouts = 0;
            AVChannelLayout* layouts = null;

            int ret = ffmpeg.avcodec_get_supported_config(ctx, codec, AVCodecConfig.AV_CODEC_CONFIG_CHANNEL_LAYOUT,
                                0, (void**)&layouts, &num_layouts);

            if (ret < 0 || layouts == null || num_layouts == 0)
            {
                AVChannelLayout fallback;
                ffmpeg.av_channel_layout_default(&fallback, preferredChannels);
                return fallback;
            }

            // 선호하는 채널 수와 정확히 일치하는 것을 찾기
            for (int i = 0; i < num_layouts; i++)
            {
                if (layouts[i].nb_channels == preferredChannels)
                {
                    AVChannelLayout selected;
                    if (ffmpeg.av_channel_layout_copy(&selected, &layouts[i]) == 0)
                        return selected;
                }
            }

            // 정확히 일치하는 것이 없으면 가장 가까운 것 선택
            int bestIndex = -1;
            int bestDiff = int.MaxValue;

            for (int i = 0; i < num_layouts; i++)
            {
                int diff = Math.Abs(layouts[i].nb_channels - preferredChannels);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                AVChannelLayout selected;
                if (ffmpeg.av_channel_layout_copy(&selected, &layouts[bestIndex]) == 0)
                    return selected;
            }

            // 모두 실패하면 기본값 사용
            AVChannelLayout fallbackLayout;
            ffmpeg.av_channel_layout_default(&fallbackLayout, preferredChannels);
            return fallbackLayout;
        }

        private static unsafe int SelectBestSampleRate(AVCodecContext* ctx, AVCodec* codec, int preferredSampleRate)
        {
            int num_samplerates = 0;
            int* supported_samplerates = null;

            int ret = ffmpeg.avcodec_get_supported_config(ctx, codec, AVCodecConfig.AV_CODEC_CONFIG_SAMPLE_RATE,
                                0, (void**)&supported_samplerates, &num_samplerates);

            if (ret < 0 || supported_samplerates == null || num_samplerates == 0)
                return preferredSampleRate; // 제한이 없으면 선호하는 값 사용

            // 정확히 일치하는 것이 있는지 확인
            for (int i = 0; i < num_samplerates; i++)
            {
                if (supported_samplerates[i] == preferredSampleRate)
                    return preferredSampleRate;
            }

            // 가장 가까운 값 찾기
            int best = supported_samplerates[0];
            int bestDiff = Math.Abs(preferredSampleRate - best);

            for (int i = 1; i < num_samplerates; i++)
            {
                int diff = Math.Abs(preferredSampleRate - supported_samplerates[i]);
                if (diff < bestDiff)
                {
                    best = supported_samplerates[i];
                    bestDiff = diff;
                }
            }

            return best;
        }

        //private static unsafe int SelectBestSampleRateLegacy(AVCodec* codec, int preferredSampleRate)
        //{
        //    // 구버전 FFmpeg용: codec->supported_samplerates 직접 접근
        //    if (codec->supported_samplerates == null)
        //        return preferredSampleRate; // 제한이 없으면 선호하는 값 사용

        //    // 지원되는 샘플레이트 개수 확인 (0으로 끝나는 배열)
        //    int count = 0;
        //    int* rates = codec->supported_samplerates;
        //    while (rates[count] != 0)
        //        count++;

        //    if (count == 0)
        //        return preferredSampleRate;

        //    // 정확히 일치하는 것이 있는지 확인
        //    for (int i = 0; i < count; i++)
        //    {
        //        if (rates[i] == preferredSampleRate)
        //            return preferredSampleRate;
        //    }

        //    // 가장 가까운 값 찾기
        //    int best = rates[0];
        //    int bestDiff = Math.Abs(preferredSampleRate - best);

        //    for (int i = 1; i < count; i++)
        //    {
        //        int diff = Math.Abs(preferredSampleRate - rates[i]);
        //        if (diff < bestDiff)
        //        {
        //            best = rates[i];
        //            bestDiff = diff;
        //        }
        //    }

        //    return best;
        //}

        //private static unsafe AVSampleFormat GetBestSupportedSampleFormatLegacy(AVCodecContext* ctx, AVCodec* codec)
        //{
        //    // 구 버전에서는 codec->sample_fmts를 직접 확인
        //    if (codec->sample_fmts == null)
        //    {
        //        // sample_fmts가 null이면 모든 포맷을 지원한다고 가정
        //        return AVSampleFormat.AV_SAMPLE_FMT_S16; // 기본값 반환
        //    }

        //    // 일반적으로 선호되는 형식 순서
        //    var preferredFormats = new[] {
        //        AVSampleFormat.AV_SAMPLE_FMT_FLTP,       
        //        AVSampleFormat.AV_SAMPLE_FMT_FLT,
        //        AVSampleFormat.AV_SAMPLE_FMT_S32P,
        //        AVSampleFormat.AV_SAMPLE_FMT_S32,
        //        AVSampleFormat.AV_SAMPLE_FMT_S16P,
        //        AVSampleFormat.AV_SAMPLE_FMT_S16,
        //        AVSampleFormat.AV_SAMPLE_FMT_U8P,
        //        AVSampleFormat.AV_SAMPLE_FMT_U8
        //    };

        //    // 선호하는 포맷 중에서 지원되는 것 찾기
        //    foreach (var preferredFormat in preferredFormats)
        //    {
        //        for (int i = 0; codec->sample_fmts[i] != AVSampleFormat.AV_SAMPLE_FMT_NONE; i++)
        //        {
        //            if (codec->sample_fmts[i] == preferredFormat)
        //                return preferredFormat;
        //        }
        //    }

        //    // 선호하는 형식이 없으면 첫 번째 지원 형식 반환
        //    return codec->sample_fmts[0];
        //}

        private static unsafe AVSampleFormat GetBestSupportedSampleFormat(AVCodecContext* ctx, AVCodec* codec)
        {
            int num_sample_fmts = 0;
            AVSampleFormat* sample_fmts = null;

            int ret = ffmpeg.avcodec_get_supported_config(ctx, codec, AVCodecConfig.AV_CODEC_CONFIG_SAMPLE_FORMAT,
                                0, (void**)&sample_fmts, &num_sample_fmts);

            if (ret < 0 || sample_fmts == null || num_sample_fmts == 0)
                return AVSampleFormat.AV_SAMPLE_FMT_NONE;

            // 일반적으로 선호되는 형식 순서
            var preferredFormats = new[] {
            AVSampleFormat.AV_SAMPLE_FMT_FLTP,
            AVSampleFormat.AV_SAMPLE_FMT_FLT,
            AVSampleFormat.AV_SAMPLE_FMT_S32P,
            AVSampleFormat.AV_SAMPLE_FMT_S32,
            AVSampleFormat.AV_SAMPLE_FMT_S16P,
            AVSampleFormat.AV_SAMPLE_FMT_S16
        };

            foreach (var preferredFormat in preferredFormats)
            {
                for (int i = 0; i < num_sample_fmts; i++)
                {
                    if (sample_fmts[i] == preferredFormat)
                        return preferredFormat;
                }
            }

            // 선호하는 형식이 없으면 첫 번째 지원 형식 반환
            return sample_fmts[0];
        }

        //private static unsafe AVChannelLayout SelectChannelLayout(AVCodecContext* ctx, AVCodec* codec)
        //{
        //    int num_layouts = 0;
        //    AVChannelLayout* layouts = null;

        //    int ret = ffmpeg.avcodec_get_supported_config(ctx, codec, AVCodecConfig.AV_CODEC_CONFIG_CHANNEL_LAYOUT,
        //                        0, (void**)&layouts, &num_layouts);

        //    if (ret < 0 || layouts == null || num_layouts == 0)
        //    {
        //        AVChannelLayout fallback;
        //        ffmpeg.av_channel_layout_default(&fallback, 2);
        //        return fallback;
        //    }

        //    int bestIndex = -1;
        //    int bestChannels = 0;

        //    for (int i = 0; i < num_layouts; i++)
        //    {
        //        if (layouts[i].nb_channels > bestChannels)
        //        {
        //            bestChannels = layouts[i].nb_channels;
        //            bestIndex = i;
        //        }
        //    }

        //    AVChannelLayout selected;
        //    if (bestIndex >= 0 && ffmpeg.av_channel_layout_copy(&selected, &layouts[bestIndex]) == 0)
        //        return selected;

        //    AVChannelLayout fallbackLayout;
        //    ffmpeg.av_channel_layout_default(&fallbackLayout, 2);
        //    return fallbackLayout;
        //}

        private static unsafe bool IsSampleFormatSupported(AVCodecContext* ctx, AVCodec* codec, AVSampleFormat sample_fmt)
        {
            int num_sample_fmts = 0;
            AVSampleFormat* sample_fmts = null;

            int ret = ffmpeg.avcodec_get_supported_config(ctx, codec, AVCodecConfig.AV_CODEC_CONFIG_SAMPLE_FORMAT,
                                0, (void**)&sample_fmts, &num_sample_fmts);

            if (ret < 0 || sample_fmts == null || num_sample_fmts == 0)
                return false;

            for (int i = 0; i < num_sample_fmts; i++)
            {
                if (sample_fmts[i] == sample_fmt)
                    return true;
            }

            return false;
        }

        //private static unsafe bool IsSampleFormatSupportedLegacy(AVCodec* codec, AVSampleFormat sample_fmt)
        //{
        //    if (codec == null)
        //        return false;

        //    // codec->sample_fmts가 null이면 모든 포맷 지원
        //    if (codec->sample_fmts == null)
        //        return true;

        //    AVSampleFormat* supported_fmts = codec->sample_fmts;
        //    for (int i = 0; i < 50; i++) // 무한루프 방지
        //    {
        //        AVSampleFormat fmt = supported_fmts[i];
        //        if (fmt == AVSampleFormat.AV_SAMPLE_FMT_NONE)
        //            break;
        //        if (fmt == sample_fmt)
        //            return true;
        //    }

        //    return false;
        //}

        //private static unsafe int SelectSampleRate(AVCodecContext* ctx, AVCodec* codec)
        //{
        //    int num_samplerates = 0;
        //    int* supported_samplerates = null;

        //    int ret = ffmpeg.avcodec_get_supported_config(ctx, codec, AVCodecConfig.AV_CODEC_CONFIG_SAMPLE_RATE,
        //                        0, (void**)&supported_samplerates, &num_samplerates);

        //    if (ret < 0 || supported_samplerates == null || num_samplerates == 0)
        //        return 44100; // fallback

        //    int best = supported_samplerates[0];
        //    int target = 44100;

        //    for (int i = 0; i < num_samplerates; i++)
        //    {
        //        int sr = supported_samplerates[i];
        //        if (ctx->sample_rate == sr)
        //            return sr;

        //        if (Math.Abs(target - sr) < Math.Abs(target - best))
        //            best = sr;
        //    }

        //    return best;
        //}
        #endregion

        #region Events
        public event AudioFrameEncodedEventHandler? AudioFrameEncoded;
        #endregion

        #region Private / Protected Methods
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
                        // 1. 드레인 모드에 진입
                        //ffmpeg.avcodec_send_frame(codecContext, null);

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
        #endregion

        #region Public Methods
        public unsafe void TryEncode(AVFrame* srcFrame)
        {
            if (IsDisposed)
                return;

            AVFrame* encodeFrame = srcFrame;

            if ((AVSampleFormat)srcFrame->format != _codecContext->sample_fmt || srcFrame->sample_rate != _codecContext->sample_rate || srcFrame->ch_layout.nb_channels != _codecContext->ch_layout.nb_channels)
            {
                if (_converter == null ||
                    _converter.DstFormat != _codecContext->sample_fmt ||
                    _converter.DstSampleRate != _codecContext->sample_rate ||
                    _converter.DstChannels != _codecContext->ch_layout.nb_channels)
                {
                    _converter?.Dispose();
                    _converter = new FFmpegAudioConverter((AVSampleFormat)srcFrame->format, srcFrame->sample_rate, srcFrame->ch_layout.nb_channels,
                        _codecContext->sample_fmt, _codecContext->sample_rate, _codecContext->ch_layout.nb_channels);
                }

                if (_convertedFrame == null)
                {
                    _convertedFrame = ffmpeg.av_frame_alloc();
                }
                else
                    ffmpeg.av_frame_unref(_convertedFrame);

                if (!_converter.Convert(encodeFrame, _convertedFrame))
                {
                    return;
                }

                encodeFrame = _convertedFrame;
            }

            try
            {
                if (ffmpeg.avcodec_send_frame(_codecContext, encodeFrame) < 0)
                    return;

                if (ffmpeg.avcodec_receive_packet(_codecContext, _packet) == 0)
                {
                    AudioFrameEncoded?.Invoke(this, new AudioFrameEncodedEventArgs(_packet));
                }

                ffmpeg.av_packet_unref(_packet);
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
        }
        #endregion
    }
}
