using System.Collections.Concurrent;

namespace OPNX.Lib.Streaming.RTSP.Commons.Interfaces
{
    public delegate void VideoSourceHandler(VideoSource videoSource);

    public interface IVideoSourceStorage
    {
        //BlockingCollection<VideoSource> VideoSources { get; }
        ConcurrentDictionary<int, VideoSource> VideoSources { get; }
        //VideoSource GetVideoSourceById(Guid videoSourceId);               
        VideoSource CreateVideoSource(VideoSource videoSource);
        void UpdateVideoSource(VideoSource videoSource);
        void DeleteVideoSource(int videoSourceId);

        event VideoSourceHandler OnVideoSourceCreated;
        event VideoSourceHandler OnVideoSourceUpdated;
        event VideoSourceHandler OnVideoSourceDeleted;
    }
}
