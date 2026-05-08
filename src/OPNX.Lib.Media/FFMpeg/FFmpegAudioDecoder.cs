using FFmpeg.AutoGen;
using OPNX.Lib.Common.LifeCycle;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Common.Platform.Windows;
using OPNX.Lib.Media.FFMpeg.EventHandlers;
using OPNX.Lib.Media.FFMpeg.RawFrames.Audio;
using System.Buffers;

namespace OPNX.Lib.Media.FFMpeg
{
    public sealed class FFmpegAudioDecoder : DisposableBase
    {
        #region Fields
        private unsafe readonly AVCodec* _codec;
        //private unsafe readonly AVCodecParserContext* parser;
        private unsafe AVCodecContext* _codecContext = null;

        private unsafe AVPacket* _packet = ffmpeg.av_packet_alloc();
        private unsafe AVFrame* _decodedFrame = ffmpeg.av_frame_alloc();
        private unsafe AVFrame* _convertedFrame = ffmpeg.av_frame_alloc();

        private int _extraDataLength = 0;

        private FFmpegAudioConverter? _converter = null;

        private readonly ArrayPool<byte> _audioBufferPool = ArrayPool<byte>.Shared;

        //private byte[] pooledBuffer;
        //private int pooledBufferSize = 0;
        #endregion

        #region Constructors
        public FFmpegAudioDecoder(AVCodecID codecID)
        {
            try
            {
                unsafe
                {
                    _codec = ffmpeg.avcodec_find_decoder(codecID);
                    if (_codec->id <= 0)
                    {
                        throw new Exception("Failed to find the audio codec.");
                    }

                    _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
                    ArgumentNullException.ThrowIfNull(_codecContext);

                    _codecContext->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
                    _codecContext->request_sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16; // 일반적으로 S16 사용
                    _codecContext->ch_layout = new AVChannelLayout();

                    switch (codecID)
                    {
                        case AVCodecID.AV_CODEC_ID_PCM_MULAW:
                        case AVCodecID.AV_CODEC_ID_PCM_ALAW:
                            _codecContext->sample_rate = 8000;
                            _codecContext->ch_layout.nb_channels = 1; // mono
                            break;

                        case AVCodecID.AV_CODEC_ID_PCM_S16LE:
                            _codecContext->sample_rate = 44100; // 필요 시 조정
                            _codecContext->ch_layout.nb_channels = 2; // stereo
                            break;

                        case AVCodecID.AV_CODEC_ID_AAC:
                            _codecContext->sample_rate = 48000; // SDP/스트림에서 가져오는 게 가장 정확                            
                            _codecContext->ch_layout.nb_channels = 2; // stereo 기본값

                            //if (config != null && config.Length > 0)
                            //{
                            //    codecContext->extradata = (byte*)ffmpeg.av_malloc((nint)config.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE);
                            //    Marshal.Copy(config, 0, (IntPtr)codecContext->extradata, config.Length);
                            //    codecContext->extradata_size = config.Length;
                            //}
                            break;
                    }

                    ffmpeg.av_channel_layout_default(&_codecContext->ch_layout, _codecContext->ch_layout.nb_channels);

                    int ret = ffmpeg.avcodec_open2(_codecContext, _codec, null);
                    if (ret < 0)
                    {
                        var _codecContect = _codecContext;
                        ffmpeg.avcodec_free_context(&_codecContect);
                        throw new ApplicationException($"Failed to open the codec. CodecID={codecID}, Error={FFmpegHelper.Av_strerror(ret)}.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }

        }
        #endregion

        #region Events
        public event AudioFrameDecodedEventHandler? AudioFrameDecoded;
        #endregion

        #region Public Methods
        public unsafe bool TryDecode(RawAudioFrame rawAudioFrame,
                              AVSampleFormat targetFmt = AVSampleFormat.AV_SAMPLE_FMT_S16,
                              int targetSampleRate = 8000,
                              int targetChannels = 1)
        {
            if (IsDisposed || rawAudioFrame == null)
                return false;

            try
            {
                if (rawAudioFrame is RawAACFrame rawAACFrame)
                {
                    var config = rawAACFrame.ConfigSegment;
                    int configLength = config.Length;

                    if (configLength > 0 && configLength != _extraDataLength)
                    {
                        _extraDataLength = configLength;
                        fixed (byte* configPtr = config.Span)
                        {
                            Set_audio_decoder_extradata((IntPtr)configPtr, _extraDataLength);
                        }
                    }
                }

                ReadOnlyMemory<byte> frameData = rawAudioFrame.FrameData;
                int frameSize = frameData.Length;
                long timestamp = rawAudioFrame.Timestamp;

                if (frameSize <= 0)
                {
                    return false;
                }

                ffmpeg.av_packet_unref(_packet);

                fixed (byte* rawDataPtr = frameData.Span)
                {
                    _packet->data = rawDataPtr;
                    _packet->size = frameSize;
                    _packet->pts = timestamp;
                    _packet->dts = timestamp;

                    int sendRet = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                    if (sendRet < 0)
                    {
                        return false;
                    }
                }

                ffmpeg.av_frame_unref(_decodedFrame);
                int receiveRet = ffmpeg.avcodec_receive_frame(_codecContext, _decodedFrame);

                if (receiveRet == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    return false;
                }

                if (receiveRet == ffmpeg.AVERROR_EOF)
                {
                    return false;
                }

                if (receiveRet < 0)
                {
                    return false;
                }

                AVFrame* resultFrame = _decodedFrame;
                bool needConversion = targetFmt != (AVSampleFormat)_decodedFrame->format ||
                                      targetSampleRate != _decodedFrame->sample_rate ||
                                      targetChannels != _decodedFrame->ch_layout.nb_channels;

                if (needConversion)
                {
                    // 컨버터 초기화 (처음 또는 포맷 변경 시)
                    if (_converter == null ||
                        _converter.SrcFormat != (AVSampleFormat)_decodedFrame->format ||
                        _converter.SrcSampleRate != _decodedFrame->sample_rate ||
                        _converter.SrcChannels != _decodedFrame->ch_layout.nb_channels ||
                        _converter.DstFormat != targetFmt ||
                        _converter.DstSampleRate != targetSampleRate ||
                        _converter.DstChannels != targetChannels)
                    {
                        _converter?.Dispose();
                        _converter = new FFmpegAudioConverter(
                            (AVSampleFormat)_decodedFrame->format,
                            _decodedFrame->sample_rate,
                            _decodedFrame->ch_layout.nb_channels,
                            targetFmt,
                            targetSampleRate,
                            targetChannels);
                    }

                    ffmpeg.av_frame_unref(_convertedFrame);

                    int convertRet = _converter.TryConvert(_decodedFrame, _convertedFrame);
                    if (convertRet < 0)
                    {
                        return false;
                    }

                    if (_decodedFrame->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        _convertedFrame->pts = ffmpeg.av_rescale_q(
                            _decodedFrame->pts,
                            new AVRational { num = 1, den = _decodedFrame->sample_rate },
                            new AVRational { num = 1, den = targetSampleRate });
                    }

                    resultFrame = _convertedFrame;
                }

                AudioFrameDecoded?.Invoke(this, new AudioFrameDecodedEventArgs(_codec->id, resultFrame));
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Error($"An exception occurred while decoding audio. Error={ex.Message}.");
                return false;
            }
        }
        #endregion

        #region Private / Protected Methods
        //private unsafe void RaiseDecodedEvent(byte[] pcmData, int length, int channels, int sampleRate, long pts)
        //{
        //    // 실제 사용할 길이만 복사한 새 byte[] 전달
        //    var finalData = new byte[length];

        //    //Buffer.BlockCopy(pcmData, 0, finalData, 0, length);

        //    fixed (byte* srcPtr = pcmData)
        //    fixed (byte* destPtr = finalData)
        //    {
        //        //Unsafe.CopyBlock(destPtr, srcPtr, (uint)length);
        //        //Win32.memcpy((IntPtr)destPtr, (IntPtr)srcPtr, (UIntPtr)length);
        //        Unsafe.CopyBlockUnaligned(destPtr, srcPtr, (uint)length);
        //    }

        //    audioBufferPool.Return(pcmData); // ArrayPool로부터 반환

        //    AudioFrameDecoded?.Invoke(this, new AudioFrameDecodedEventArgs(finalData, channels, sampleRate, pts));
        //}

        private unsafe int Set_audio_decoder_extradata(IntPtr extradata, int extradataLength)
        {
            try
            {
                byte* newExtradata = null;

                if (_codecContext->extradata == null || _codecContext->extradata_size < extradataLength)
                {
                    if (_codecContext->extradata != null)
                    {
                        ffmpeg.av_free(_codecContext->extradata);
                    }

                    newExtradata = (byte*)ffmpeg.av_malloc((ulong)(extradataLength + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
                    if (newExtradata == null)
                    {
                        return -2;
                    }
                }

                if (newExtradata != null)
                {
                    Win32.MemCopy((IntPtr)newExtradata, extradata, (UIntPtr)extradataLength);
                    //Unsafe.CopyBlock(newExtradata, (void*)extradata, (uint)extradataLength);
                    //Unsafe.CopyBlockUnaligned(newExtradata, (void*)extradata, (uint)extradataLength);

                    var oldContext = _codecContext;
                    ffmpeg.avcodec_free_context(&oldContext);

                    _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
                    if (_codecContext == null)
                    {
                        ffmpeg.av_free(newExtradata);
                        return -4;
                    }

                    _codecContext->extradata = newExtradata;
                    _codecContext->extradata_size = extradataLength;

                    // AAC 기본 세팅 - 필요시 샘플레이트, 채널 등 수동 설정 가능
                    _codecContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP; // AAC 디코더의 기본 포맷
                    _codecContext->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;

                    if (ffmpeg.avcodec_open2(_codecContext, _codec, null) < 0)
                    {
                        return -3;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }

            return 0;
        }

        protected override void OnDispose()
        {
            base.OnDispose();

            try
            {
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
                            frame = (AVFrame*)IntPtr.Zero;
                        }

                        var codecContext = _codecContext;
                        ffmpeg.avcodec_free_context(&codecContext);
                    }

                    //if (parser != null)
                    //{
                    //    ffmpeg.av_parser_close(parser);
                    //}

                    FFmpegHelper.FreeFrame(ref _decodedFrame);
                    FFmpegHelper.FreeFrame(ref _convertedFrame);

                    FFmpegHelper.FreePacket(ref _packet);

                    _converter?.Dispose();
                    _converter = null;
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
