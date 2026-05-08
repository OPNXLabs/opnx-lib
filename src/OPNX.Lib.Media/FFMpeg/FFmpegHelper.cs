using FFmpeg.AutoGen;
using Microsoft.Win32;
using OpenCvSharp;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Common.Platform.Windows;
using SkiaSharp;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OPNX.Lib.Media.FFMpeg
{
    public static class FFmpegHelper
    {
        public static unsafe void FreePacket(ref AVPacket* packet)
        {
            if (packet != null)
            {
                ffmpeg.av_packet_unref(packet);
                AVPacket* freePacket = packet;
                ffmpeg.av_packet_free(&freePacket);
                packet = null;
            }
        }

        public static unsafe void FreeFrame(ref AVFrame* frame)
        {
            if (frame != null)
            {
                ffmpeg.av_frame_unref(frame);
                AVFrame* freeFrame = frame;
                ffmpeg.av_frame_free(&freeFrame);
                frame = null;
            }
        }

        public static unsafe void UpdateAVFrameData(AVFrame* videoFrame, Mat mat)
        {
            int width = mat.Width;
            int height = mat.Height;
            Mat? yuvMat = null; // Mat을 함수 스코프에서 유지
            IntPtr yuvDataPtr = IntPtr.Zero;

            try
            {

                if (mat.Type() == MatType.CV_8UC1 && mat.Rows == height + height / 2)
                {
                    yuvDataPtr = (IntPtr)mat.DataPointer;
                }
                else
                {
                    yuvMat = new Mat(height + height / 2, width, MatType.CV_8UC1);
                    Cv2.CvtColor(mat, yuvMat, ColorConversionCodes.BGR2YUV_I420);
                    yuvDataPtr = (IntPtr)yuvMat.DataPointer;
                }

                int ySize = width * height;
                int uvSize = ySize / 4; // U, V 채널 크기

                //Unsafe.CopyBlockUnaligned(destination: (void*)videoFrame->data[0], source: (void*)yuvDataPtr, byteCount: (uint)ySize);
                //Unsafe.CopyBlockUnaligned(destination: (void*)videoFrame->data[1], source: (void*)(yuvDataPtr + ySize), byteCount: (uint)uvSize);
                //Unsafe.CopyBlockUnaligned(destination: (void*)videoFrame->data[2], source: (void*)(yuvDataPtr + ySize + uvSize), byteCount: (uint)uvSize);

                Win32.MemCopy((IntPtr)videoFrame->data[0], yuvDataPtr, (UIntPtr)ySize);         // Y 채널
                Win32.MemCopy((IntPtr)videoFrame->data[1], yuvDataPtr + ySize, (UIntPtr)uvSize); // U 채널
                Win32.MemCopy((IntPtr)videoFrame->data[2], yuvDataPtr + ySize + uvSize, (UIntPtr)uvSize); // V 채널

                //Unsafe.CopyBlock(destination: (void*)videoFrame->data[0], source: (void*)yuvDataPtr, byteCount: (uint)ySize);
                //Unsafe.CopyBlock(destination: (void*)videoFrame->data[1], source: (void*)(yuvDataPtr + ySize), byteCount: (uint)uvSize);
                //Unsafe.CopyBlock(destination: (void*)videoFrame->data[2], source: (void*)(yuvDataPtr + ySize + uvSize), byteCount: (uint)uvSize);

                // AVFrame 정보 설정
                videoFrame->linesize[0] = width;
                videoFrame->linesize[1] = width / 2;
                videoFrame->linesize[2] = width / 2;

                videoFrame->width = width;
                videoFrame->height = height;
                videoFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
            finally
            {
                yuvMat?.Dispose();
            }
        }

        public static unsafe AVFrame* ToAVFrame(Mat image, int width, int height)
        {
            if (image.Empty())
                return null;

            AVFrame* frame = ffmpeg.av_frame_alloc();
            if (frame == null)
                throw new Exception("Failed to allocate the AVFrame.");

            frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            frame->width = width;
            frame->height = height;

            // AVFrame에 필요한 버퍼 할당 (32-byte alignment)
            if (ffmpeg.av_frame_get_buffer(frame, 32) < 0)
            {
                ffmpeg.av_frame_free(&frame);
                throw new Exception("Failed to allocate the AVFrame buffer.");
            }

            IntPtr matData;
            bool needsConversion = image.Channels() > 1; // YUV420P이면 변환 불필요

            if (needsConversion)
            {
                using var yuvMat = new Mat();
                // BGR 또는 BGRA를 YUV420로 변환
                Cv2.CvtColor(image, yuvMat, ColorConversionCodes.BGR2YUV_I420);
                matData = (IntPtr)yuvMat.DataPointer;
            }
            else
            {
                // 이미 YUV420P인 경우 직접 사용
                matData = (IntPtr)image.DataPointer;
            }

            int ySize = width * height;
            int uvSize = ySize >> 2; // ySize / 4;

            // AVFrame의 YUV420P 데이터 영역에 직접 복사

            //Unsafe.CopyBlockUnaligned(frame->data[0], (void*)matData, (uint)ySize);
            //Unsafe.CopyBlockUnaligned(frame->data[1], (void*)(matData + ySize), (uint)uvSize);
            //Unsafe.CopyBlockUnaligned(frame->data[2], (void*)(matData + ySize + uvSize), (uint)uvSize);

            Win32.MemCopy((IntPtr)frame->data[0], matData, (UIntPtr)ySize);                   // Y 채널
            Win32.MemCopy((IntPtr)frame->data[1], matData + ySize, (UIntPtr)uvSize);          // U 채널
            Win32.MemCopy((IntPtr)frame->data[2], matData + ySize + uvSize, (UIntPtr)uvSize); // V 채널

            //Unsafe.CopyBlock(frame->data[0], (void*)matData, (uint)ySize);
            //Unsafe.CopyBlock(frame->data[1], (void*)(matData + ySize), (uint)uvSize);
            //Unsafe.CopyBlock(frame->data[2], (void*)(matData + ySize + uvSize), (uint)uvSize);


            //using (Mat? yuvMat = needsConversion ? new Mat() : null)
            //{
            //    if (needsConversion)
            //    {
            //        // BGR 또는 BGRA를 YUV420로 변환
            //        Cv2.CvtColor(image, yuvMat, ColorConversionCodes.BGR2YUV_I420);
            //        matData = (IntPtr)yuvMat.DataPointer;
            //    }
            //    else
            //    {

            //    }

            //    int ySize = width * height;
            //    int uvSize = ySize / 4;

            //    // AVFrame의 YUV420P 데이터 영역에 직접 복사

            //    //Unsafe.CopyBlockUnaligned(frame->data[0], (void*)matData, (uint)ySize);
            //    //Unsafe.CopyBlockUnaligned(frame->data[1], (void*)(matData + ySize), (uint)uvSize);
            //    //Unsafe.CopyBlockUnaligned(frame->data[2], (void*)(matData + ySize + uvSize), (uint)uvSize);

            //    Win32.MemCopy((IntPtr)frame->data[0], matData, (UIntPtr)ySize);                   // Y 채널
            //    Win32.MemCopy((IntPtr)frame->data[1], matData + ySize, (UIntPtr)uvSize);          // U 채널
            //    Win32.MemCopy((IntPtr)frame->data[2], matData + ySize + uvSize, (UIntPtr)uvSize); // V 채널

            //    //Unsafe.CopyBlock(frame->data[0], (void*)matData, (uint)ySize);
            //    //Unsafe.CopyBlock(frame->data[1], (void*)(matData + ySize), (uint)uvSize);
            //    //Unsafe.CopyBlock(frame->data[2], (void*)(matData + ySize + uvSize), (uint)uvSize);
            //}

            return frame;
        }


        public static unsafe SKBitmap? ToSKBitmap(AVFrame* videoFrame)
        {
            using Mat? mat = ToMat(videoFrame);

            if (mat is null || mat.Empty())
                return null;

            // SkiaSharp에서 권장하는 BGRA8888 포맷 사용
            var info = new SKImageInfo(mat.Width, mat.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var skBitmap = new SKBitmap(info);

            IntPtr destPtr = skBitmap.GetPixels();
            if (destPtr == IntPtr.Zero)
            {
                skBitmap.Dispose();
                return null;
            }

            switch ((mat.Depth(), mat.Channels()))
            {
                case (MatType.CV_8U, 3): // CV_8UC3
                    using (var converted = new Mat())
                    {
                        Cv2.CvtColor(mat, converted, ColorConversionCodes.BGR2BGRA);
                        //Buffer.MemoryCopy((void*)converted.Data, (void*)destPtr,
                        //                  skBitmap.ByteCount, (long)(converted.Rows * converted.Cols * 4));
                        Unsafe.CopyBlockUnaligned((byte*)destPtr, (byte*)converted.Data,
                                  (uint)(converted.Rows * converted.Cols * 4));
                    }
                    break;

                case (MatType.CV_8U, 4): // CV_8UC4
                    //Buffer.MemoryCopy((void*)mat.Data, (void*)destPtr,
                    //                  skBitmap.ByteCount, (long)(mat.Rows * mat.Cols * 4));
                    Unsafe.CopyBlockUnaligned((byte*)destPtr, (byte*)mat.Data,
                                                      (uint)(mat.Rows * mat.Cols * 4));
                    break;

                default:
                    skBitmap.Dispose();
                    return null; // 지원하지 않는 포맷
            }

            return skBitmap;
        }
        //public static unsafe SKBitmap ToSKBitmap(AVFrame* videoFrame)
        //{
        //    using Mat mat = ToMat(videoFrame);
        //    if (mat.Empty())
        //        return null;

        //    var info = new SKImageInfo(mat.Width, mat.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        //    var skBitmap = new SKBitmap(info);
        //    IntPtr destPtr = skBitmap.GetPixels();

        //    if (destPtr == IntPtr.Zero)
        //    {
        //        skBitmap.Dispose();
        //        return null;
        //    }

        //    Mat converted = null;
        //    try
        //    {
        //        // 항상 BGRA로 변환
        //        if (mat.Channels() == 3)
        //        {
        //            converted = new Mat();
        //            Cv2.CvtColor(mat, converted, ColorConversionCodes.BGR2BGRA);
        //        }
        //        else if (mat.Channels() == 4)
        //        {
        //            converted = mat.Clone();
        //        }
        //        else
        //        {
        //            skBitmap.Dispose();
        //            return null;
        //        }

        //        // stride를 고려한 복사
        //        int matStride = (int)converted.Step();  // OpenCV의 실제 행 간격
        //        int skStride = skBitmap.RowBytes;        // SKBitmap의 행 간격
        //        int copyWidth = mat.Width * 4;           // 실제 복사할 바이트 수

        //        byte* srcPtr = (byte*)converted.Data;
        //        byte* dstPtr = (byte*)destPtr;

        //        // 행 단위로 복사 (stride 차이 고려)
        //        for (int y = 0; y < mat.Height; y++)
        //        {
        //            Unsafe.CopyBlockUnaligned(
        //                dstPtr + (y * skStride),
        //                srcPtr + (y * matStride),
        //                (uint)copyWidth
        //            );
        //        }

        //        return skBitmap;
        //    }
        //    catch
        //    {
        //        skBitmap.Dispose();
        //        return null;
        //    }
        //    finally
        //    {
        //        if (converted != null && converted != mat)
        //            converted.Dispose();
        //    }
        //}


        public static unsafe Mat? ToMat(AVFrame* videoFrame)
        {
            Mat? result = null;

            switch ((AVPixelFormat)videoFrame->format)
            {
                case AVPixelFormat.AV_PIX_FMT_BGR24:
                    {
                        result = Mat.FromPixelData(videoFrame->height, videoFrame->width, MatType.CV_8UC3, (IntPtr)videoFrame->data[0], videoFrame->linesize[0]);
                    }
                    break;

                case AVPixelFormat.AV_PIX_FMT_YUVJ420P:
                case AVPixelFormat.AV_PIX_FMT_YUV420P:
                    {
                        int width = videoFrame->width;
                        int height = videoFrame->height;

                        // linesize가 width와 같으면 padding 없음 - 빠른 경로
                        if (videoFrame->linesize[0] == width &&
                            videoFrame->linesize[1] == width / 2 &&
                            videoFrame->linesize[2] == width / 2)
                        {
                            int ySize = width * height;
                            int uvSize = ySize / 4;
                            int totalSize = ySize + uvSize * 2;

                            IntPtr dataPtr = Marshal.AllocHGlobal(totalSize);
                            try
                            {
                                Win32.MemCopy(dataPtr, (IntPtr)videoFrame->data[0], (UIntPtr)ySize);
                                Win32.MemCopy(dataPtr + ySize, (IntPtr)videoFrame->data[1], (UIntPtr)uvSize);
                                Win32.MemCopy(dataPtr + ySize + uvSize, (IntPtr)videoFrame->data[2], (UIntPtr)uvSize);

                                using Mat tmpMat = Mat.FromPixelData(height + height / 2, width, MatType.CV_8UC1, dataPtr);
                                result = new Mat();
                                Cv2.CvtColor(tmpMat, result, ColorConversionCodes.YUV2BGR_I420);
                            }
                            catch (Exception ex)
                            {
                                LogManager.Error(ex);
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(dataPtr);
                            }
                        }
                        else // padding이 있는 경우 - 행 단위 복사
                        {
                            int yLinesize = videoFrame->linesize[0];
                            int uLinesize = videoFrame->linesize[1];
                            int vLinesize = videoFrame->linesize[2];

                            int ySize = width * height;
                            int uvSize = ySize / 4;
                            int totalSize = ySize + uvSize * 2;

                            IntPtr dataPtr = Marshal.AllocHGlobal(totalSize);
                            try
                            {
                                byte* dest = (byte*)dataPtr;
                                byte* srcY = videoFrame->data[0];
                                byte* srcU = videoFrame->data[1];
                                byte* srcV = videoFrame->data[2];

                                // Y plane
                                for (int y = 0; y < height; y++)
                                {
                                    Win32.MemCopy((IntPtr)(dest + y * width), (IntPtr)(srcY + y * yLinesize), (UIntPtr)width);
                                }

                                // U plane
                                int uvHeight = height / 2;
                                int uvWidth = width / 2;
                                dest += ySize;
                                for (int y = 0; y < uvHeight; y++)
                                {
                                    Win32.MemCopy((IntPtr)(dest + y * uvWidth), (IntPtr)(srcU + y * uLinesize), (UIntPtr)uvWidth);
                                }

                                // V plane
                                dest += uvSize;
                                for (int y = 0; y < uvHeight; y++)
                                {
                                    Win32.MemCopy((IntPtr)(dest + y * uvWidth), (IntPtr)(srcV + y * vLinesize), (UIntPtr)uvWidth);
                                }

                                using Mat tmpMat = Mat.FromPixelData(height + height / 2, width, MatType.CV_8UC1, dataPtr);
                                result = new Mat();
                                Cv2.CvtColor(tmpMat, result, ColorConversionCodes.YUV2BGR_I420);
                            }
                            catch (Exception ex)
                            {
                                LogManager.Error(ex);
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(dataPtr);
                            }
                        }
                    }
                    break;

                case AVPixelFormat.AV_PIX_FMT_NV12:
                    {
                        int width = videoFrame->width;
                        int height = videoFrame->height;

                        // linesize 체크
                        if (videoFrame->linesize[0] == width && videoFrame->linesize[1] == width)
                        {
                            int ySize = width * height;
                            int uvSize = ySize / 2;
                            int totalSize = ySize + uvSize;

                            IntPtr dataPtr = Marshal.AllocHGlobal(totalSize);
                            try
                            {
                                Win32.MemCopy(dataPtr, (IntPtr)videoFrame->data[0], (UIntPtr)ySize);
                                Win32.MemCopy(dataPtr + ySize, (IntPtr)videoFrame->data[1], (UIntPtr)uvSize);

                                using Mat tmpMat = Mat.FromPixelData(height + height / 2, width, MatType.CV_8UC1, dataPtr);
                                result = new Mat();
                                Cv2.CvtColor(tmpMat, result, ColorConversionCodes.YUV2BGR_NV12);
                            }
                            catch (Exception ex)
                            {
                                LogManager.Error(ex);
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(dataPtr);
                            }
                        }
                        else // padding 있음
                        {
                            int yLinesize = videoFrame->linesize[0];
                            int uvLinesize = videoFrame->linesize[1];
                            int ySize = width * height;
                            int uvSize = ySize / 2;
                            int totalSize = ySize + uvSize;

                            IntPtr dataPtr = Marshal.AllocHGlobal(totalSize);
                            try
                            {
                                byte* dest = (byte*)dataPtr;
                                byte* srcY = videoFrame->data[0];
                                byte* srcUV = videoFrame->data[1];

                                for (int y = 0; y < height; y++)
                                {
                                    Win32.MemCopy((IntPtr)(dest + y * width), (IntPtr)(srcY + y * yLinesize), (UIntPtr)width);
                                }

                                dest += ySize;
                                int uvHeight = height / 2;
                                for (int y = 0; y < uvHeight; y++)
                                {
                                    Win32.MemCopy((IntPtr)(dest + y * width), (IntPtr)(srcUV + y * uvLinesize), (UIntPtr)width);
                                }

                                using Mat tmpMat = Mat.FromPixelData(height + height / 2, width, MatType.CV_8UC1, dataPtr);
                                result = new Mat();
                                Cv2.CvtColor(tmpMat, result, ColorConversionCodes.YUV2BGR_NV12);
                            }
                            catch (Exception ex)
                            {
                                LogManager.Error(ex);
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(dataPtr);
                            }
                        }
                    }
                    break;
            }

            return result;
        }
        public static (string Level, int MinBitrate, int MaxBitrate) GetCodecSettings(AVCodecID codecID, int width, int height)
        {
            // 기준 해상도 및 비트레이트 범위 설정
            var h264Resolutions = new (int Width, int Height, string Level, int MinBitrate, int MaxBitrate)[]
            {
                (426, 240, "2.0", 300, 750),     // 240p: 저대역폭 환경
                (640, 360, "2.1", 500, 1000),    // 360p: 저품질 스트리밍
                (854, 480, "3.0", 750, 1500),    // 480p: 기본 SD
                (1280, 720, "3.1", 1500, 3000),  // 720p: HD
                (1920, 1080, "4.0", 3500, 6000), // 1080p: Full HD
                (2560, 1440, "4.2", 6000, 12000), // 1440p: QHD
                (3840, 2160, "5.0", 15000, 35000) // 4K: UHD
            };

            var h265Resolutions = new (int Width, int Height, string Level, int MinBitrate, int MaxBitrate)[]
            {
                (720, 480, "3.0", 500, 1200),      // SD - H.265는 H.264 대비 30-50% 효율적
                (1280, 720, "3.1", 1000, 2000),    // HD 720p
                (1920, 1080, "4.0", 2000, 4000),   // Full HD 1080p
                (2560, 1440, "4.1", 4000, 8000),   // QHD 1440p
                (3840, 2160, "5.0", 8000, 20000)   // 4K UHD
            };

            var av1Resolutions = new (int Width, int Height, string Level, int MinBitrate, int MaxBitrate)[]
            {
                (720, 480, "2.0", 500, 1000),   // SD
                (1280, 720, "3.0", 1000, 2500), // HD
                (1920, 1080, "4.0", 2500, 5000), // Full HD
                (3840, 2160, "5.0", 10000, 25000) // 4K
            };

            // 선택된 코덱에 따라 해상도 배열을 설정합니다.
            var resolutionArray = codecID switch
            {
                AVCodecID.AV_CODEC_ID_H264 => h264Resolutions,
                AVCodecID.AV_CODEC_ID_HEVC => h265Resolutions,
                AVCodecID.AV_CODEC_ID_AV1 => av1Resolutions,
                _ => throw new ArgumentException("Unsupported codec")
            };

            // 입력 해상도와 가장 가까운 해상도를 찾습니다.
            //var closest = resolutionArray
            //    .OrderBy(r => Math.Sqrt(Math.Pow(r.Width - width, 2) + Math.Pow(r.Height - height, 2)))
            //    .First();

            //return (closest.Level, closest.MinBitrate, closest.MaxBitrate);

            (int Width, int Height, string Level, int MinBitrate, int MaxBitrate) closest = resolutionArray[0];
            int bestDistance = int.MaxValue;

            foreach (var r in resolutionArray)
            {
                int dx = r.Width - width;
                int dy = r.Height - height;
                int distance = dx * dx + dy * dy; // sqrt, pow 없이 거리 비교

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = r;
                }
            }

            return (closest.Level, closest.MinBitrate, closest.MaxBitrate);
        }

        public static string GetCodecLevel(AVCodecID codecID, int width, int height)
        {
            return GetCodecSettings(codecID, width, height).Level;
        }

        public static (int MinBitrate, int MaxBitrate) GetBitrateRange(AVCodecID codecID, int width, int height)
        {
            //var settings = GetCodecSettings(codecID, width, height);
            //return (settings.MinBitrate, settings.MaxBitrate);
            var (_, min, max) = GetCodecSettings(codecID, width, height);
            return (min, max);
        }

        public static int CalculateMiddleBitrate(AVCodecID codecID, int width, int height)
        {
            var (minBitrate, maxBitrate) = GetBitrateRange(codecID, width, height);
            return (minBitrate + maxBitrate) / 2;
        }

        public static unsafe TimeSpan GetVideoDuration(string fileName)
        {
            AVFormatContext* formatContext = null;

            try
            {
                // AVFormatContext를 초기화하고 파일을 엽니다.

                if (ffmpeg.avformat_open_input(&formatContext, fileName, null, null) != 0)
                {
                    throw new ApplicationException("Failed to open the input file.");
                }

                if (ffmpeg.avformat_find_stream_info(formatContext, null) != 0)
                {
                    throw new ApplicationException("Failed to find the stream information.");
                }


                // 전체 비디오의 지속 시간 (AVFormatContext->duration)
                long durationInMicroseconds = formatContext->duration;

                // 만약 duration 값이 0인 경우
                if (durationInMicroseconds == 0)
                {
                    // 스트림의 duration 값을 사용하여 계산합니다.
                    durationInMicroseconds = CalculateTotalDurationFromStreams(formatContext);
                }

                // 마이크로초 단위의 지속 시간을 초 단위로 변환
                double durationInSeconds = durationInMicroseconds / 1000000.0;

                return TimeSpan.FromSeconds(durationInSeconds);
            }
            finally
            {
                // 포맷 컨텍스트를 닫습니다.
                if (formatContext != null)
                {
                    ffmpeg.avformat_close_input(&formatContext);
                }
            }
        }

        private static unsafe long CalculateTotalDurationFromStreams(AVFormatContext* formatContext)
        {
            long maxDuration = 0;

            for (uint i = 0; i < formatContext->nb_streams; i++)
            {
                AVStream* stream = formatContext->streams[i];
                if (stream->duration != 0)
                {
                    long streamDuration = stream->duration * (1000000 * stream->time_base.num) / stream->time_base.den;
                    if (streamDuration > maxDuration)
                    {
                        maxDuration = streamDuration;
                    }
                }
            }

            return maxDuration;
        }

        public static bool FFmpegLib_Initialize(string libPath)
        {
            try
            {
                if (Directory.Exists(libPath))
                {
                    ffmpeg.RootPath = libPath;

                    ffmpeg.avformat_network_init();
                    ffmpeg.avdevice_register_all();

                    return true;
                }
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
            }
            return false;
        }

        public static unsafe string? Av_strerror(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            return message;
        }

        public static int ThrowExceptionIfError(this int error)
        {
            if (error < 0) throw new ApplicationException(Av_strerror(error));
            return error;
        }

        public static unsafe AVFrame* DeepCopy(AVFrame* srcFrame)
        {
            if (srcFrame == null || srcFrame == (AVFrame*)IntPtr.Zero)
                return null;

            AVFrame* dstFrame = ffmpeg.av_frame_alloc();

            if (dstFrame == null)
                return null;

            dstFrame->format = srcFrame->format;
            dstFrame->width = srcFrame->width;
            dstFrame->height = srcFrame->height;
            dstFrame->ch_layout = srcFrame->ch_layout;
            dstFrame->nb_samples = srcFrame->nb_samples;

            if (ffmpeg.av_frame_get_buffer(dstFrame, 32) == 0 &&
                ffmpeg.av_frame_copy(dstFrame, srcFrame) >= 0 &&
                ffmpeg.av_frame_copy_props(dstFrame, srcFrame) >= 0)
            {
                return dstFrame;
            }

            ffmpeg.av_frame_unref(dstFrame);
            ffmpeg.av_frame_free(&dstFrame);

            return null;
        }

        public static unsafe AVFrame* ByteArrayToAVFrame(byte[] imageData, int width, int height, AVPixelFormat format)
        {
            // AVFrame 생성
            AVFrame* frame = ffmpeg.av_frame_alloc();

            // 이미지의 형식, 너비, 높이 설정
            frame->format = (int)format;
            frame->width = width;
            frame->height = height;

            fixed (byte* srcPtr = imageData)
            {
                byte_ptrArray4* data = (byte_ptrArray4*)&frame->data;
                int_array4* lineSize = (int_array4*)&frame->linesize;
                //byte_ptr4* data = (byte_ptr4*)&frame->data;
                //int4* linesize = (int4*)&frame->linesize;

                // AVFrame에 이미지 데이터 할당
                ffmpeg.av_image_fill_arrays(ref *data, ref *lineSize, srcPtr, format, width, height, 1);
            }

            return frame;
        }

        public static async Task<byte[]> AVFrameToByteArrayAsync(AVFrame frame)
        {
            int width = frame.width;
            int height = frame.height;
            AVPixelFormat format = (AVPixelFormat)frame.format;

            // 이미지 데이터의 바이트 수 계산
            int dataSize = ffmpeg.av_image_get_buffer_size(format, width, height, 1);

            // 이미지 데이터를 담을 바이트 배열 생성
            byte[] imageData = new byte[dataSize];

            // 비동기적으로 각 행을 병렬로 처리하여 이미지 데이터를 바이트 배열로 복사
            await Task.Run(() =>
            {
                Parallel.For(0, height, y =>
                {
                    unsafe
                    {
                        fixed (byte* ptr = imageData)
                        {
                            byte* dest = ptr + y * frame.linesize[0];
                            byte* src = frame.data[0] + y * frame.linesize[0];
                            //Buffer.MemoryCopy(src, dest, frame.linesize[0], frame.linesize[0]);
                            Unsafe.CopyBlockUnaligned(dest, src, (uint)frame.linesize[0]);
                        }
                    }
                });
            });

            return imageData;
        }

        private readonly static ArrayPool<byte> bufferPool = ArrayPool<byte>.Shared;

        //public static unsafe byte[] AVFrameToByteArray(AVFrame* frame)
        // {
        //     int dataSize = ffmpeg.av_image_get_buffer_size((AVPixelFormat)frame->format, frame->width, frame->height, 1);

        //     byte[] imageData = bufferPool.Rent(dataSize); // 재사용 가능한 배열 할당

        //     unsafe
        //     {
        //         byte_ptrArray4* ptrFrameData = (byte_ptrArray4*)&frame->data;
        //         int_array4* ptrLineSize = (int_array4*)&frame->linesize;

        //         fixed (byte* ptr = imageData)
        //         {
        //             ffmpeg.av_image_copy_to_buffer(ptr, dataSize, *ptrFrameData, *ptrLineSize, (AVPixelFormat)frame->format, frame->width, frame->height, 1);
        //         }
        //     }

        //     return imageData;
        // }

        public static unsafe byte[] AVFrameToByteArray(AVFrame* frame)
        {
            // 이미지 데이터의 바이트 수 계산
            int dataSize = ffmpeg.av_image_get_buffer_size((AVPixelFormat)frame->format, frame->width, frame->height, 1);

            // 이미지 데이터를 담을 바이트 배열 생성
            byte[] imageData = new byte[dataSize];

            // AVFrame의 이미지 데이터를 바이트 배열로 복사
            unsafe
            {
                //byte_ptr4* ptrFrameData = (byte_ptr4*)&frame->data;
                //int4* ptrLineSize = (int4*)&frame->linesize;

                byte_ptrArray4* ptrFrameData = (byte_ptrArray4*)&frame->data;
                int_array4* ptrLineSize = (int_array4*)&frame->linesize;

                fixed (byte* ptr = imageData)
                {
                    ffmpeg.av_image_copy_to_buffer(ptr, dataSize, *ptrFrameData, *ptrLineSize, (AVPixelFormat)frame->format, frame->width, frame->height, 1);
                }
            }

            return imageData;



            //byte[] result = null;
            //int size = ffmpeg.av_image_get_buffer_size((AVPixelFormat)frame->format, frame->width, frame->height, 1);
            //if (size > 0)
            //{
            //    result = new byte[size];
            //    IntPtr imgPtr = IntPtr.Zero;
            //    try
            //    {

            //        byte_ptr4* ptrFrameData = (byte_ptr4*)&frame->data;
            //        int4* ptrLineSize = (int4*)&frame->linesize;

            //        fixed (byte* ptr = result)
            //        {
            //            ffmpeg.av_image_copy_to_buffer(ptr, size, *ptrFrameData, *ptrLineSize, (AVPixelFormat)frame->format, frame->width, frame->height, 1);
            //        }

            //        imgPtr = Marshal.AllocHGlobal(result.Length);  // 메모리 할당

            //        Marshal.Copy(result, 0, imgPtr, result.Length);        // byte[]에서 할당된 메모리로 데이터 복사
            //    }
            //    catch (Exception ex)
            //    {
            //        Console.WriteLine(ex);
            //    }
            //    finally
            //    {
            //        if (imgPtr != IntPtr.Zero)
            //        {
            //            Marshal.FreeHGlobal(imgPtr);
            //        }
            //    }

            //}

            //return result;
        }

        public static bool IsCUDAInstalled()
        {
            string? cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            return !string.IsNullOrEmpty(cudaPath);
        }

        public static bool IsDirectXInstalled()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
                return key != null;
            }
            else
            {
                return false;
            }
        }

        public static AVCodecID GetCodecIDFromName(string codecName)
        {
            // Implement the conversion logic here
            // This is a placeholder and should be replaced with actual logic
            return codecName.ToUpper() switch
            {
                "H264" => AVCodecID.AV_CODEC_ID_H264,
                "H265" => AVCodecID.AV_CODEC_ID_HEVC,
                "HEVC" => AVCodecID.AV_CODEC_ID_HEVC,
                "AV1" => AVCodecID.AV_CODEC_ID_AV1,
                "MJPEG" => AVCodecID.AV_CODEC_ID_MJPEG,
                "JPEG" => AVCodecID.AV_CODEC_ID_MJPEG,
                "RAWVIDEO" => AVCodecID.AV_CODEC_ID_RAWVIDEO,
                "PCMU" => AVCodecID.AV_CODEC_ID_PCM_MULAW,
                "PCMA" => AVCodecID.AV_CODEC_ID_PCM_ALAW,
                "PCM" => AVCodecID.AV_CODEC_ID_PCM_S16LE,
                "AAC" => AVCodecID.AV_CODEC_ID_AAC,

                _ => throw new ArgumentException("Unsupported codec name", nameof(codecName)),
            };
        }

        public static unsafe List<string> GetDeviceList()
        {
            var devices = new List<string>();

            AVInputFormat* inputFormat = ffmpeg.av_find_input_format("dshow");
            if (inputFormat == null)
            {
                Console.WriteLine("dshow input format not found");
                return devices;  // 빈 리스트 반환
            }

            AVDeviceInfoList* deviceList = null;
            int ret = ffmpeg.avdevice_list_input_sources(inputFormat, null, null, &deviceList);
            if (ret < 0)
            {
                Console.WriteLine("Failed to list input sources.");
                return devices;  // 빈 리스트 반환
            }

            for (int i = 0; i < deviceList->nb_devices; i++)
            {
                AVDeviceInfo* device = deviceList->devices[i];
                string name = Marshal.PtrToStringUTF8((IntPtr)device->device_name) ?? "(null)";
                string desc = Marshal.PtrToStringUTF8((IntPtr)device->device_description) ?? "(no description)";
                Console.WriteLine($"[{i}] {desc} ({name})");
                devices.Add(name);  // 이름을 리스트에 추가
            }

            ffmpeg.avdevice_free_list_devices(&deviceList);

            return devices;
        }
    }
}
