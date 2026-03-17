using OPNX.Lib.Streaming.RTSP.Commons.Interfaces;
using System.Collections.Concurrent;

namespace OPNX.Lib.Streaming.RTSP.Commons
{
    public class VideoSourceStorage : IVideoSourceStorage
    {
        //protected readonly BlockingCollection<VideoSource> _videoSources = new BlockingCollection<VideoSource>();
        protected readonly ConcurrentDictionary<int, VideoSource> _videoSources = new ConcurrentDictionary<int, VideoSource>();
        //public BlockingCollection<VideoSource> VideoSources
        //{
        //    get
        //    {
        //        return _videoSources;
        //    }
        //}

        public ConcurrentDictionary<int, VideoSource> VideoSources
        {
            get
            {
                return _videoSources;
            }
        }
        public event VideoSourceHandler OnVideoSourceCreated;
        public event VideoSourceHandler OnVideoSourceUpdated;
        public event VideoSourceHandler OnVideoSourceDeleted;

        public VideoSourceStorage()
        {

        }

        //public bool Remove<T>(BlockingCollection<T> items, T itemToRemove)
        //{
        //    lock (items)
        //    {
        //        T comparedItem;
        //        var itemsList = new List<T>();
        //        do
        //        {
        //            var result = items.TryTake(out comparedItem);
        //            if (!result)
        //                return false;
        //            if (!comparedItem.Equals(itemToRemove))
        //            {
        //                itemsList.Add(comparedItem);
        //            }
        //        } while (!(comparedItem.Equals(itemToRemove)));
        //        Parallel.ForEach(itemsList, t => items.Add(t));
        //    }
        //    return true;
        //}

        private void Clear<T>(BlockingCollection<T> blockingCollection)
        {
            if (blockingCollection == null)
            {
                throw new ArgumentNullException("blockingCollection");
            }

            while (blockingCollection.Count > 0)
            {
                T item;
                blockingCollection.TryTake(out item);
            }
        }

        public VideoSource CreateVideoSource(VideoSource videoSource)
        {
            if (_videoSources.ContainsKey(videoSource.EntityID))
                return _videoSources[videoSource.EntityID];

            //if (_videoSources.Any(x => x.Value.RtspURL == videoSource.RtspURL && x.Value.RequestURL == videoSource.RequestURL))
            //{
            //    return _videoSources.FirstOrDefault(x => x.Value.RtspURL == videoSource.RtspURL && x.Value.RequestURL == videoSource.RequestURL).Value;
            //}

            //if (_videoSources.Any(vs => videoSource.Id == vs.Id))
            //{
            //    return _videoSources.FirstOrDefault(x => x.Id == videoSource.Id);
            //}

            //if (_videoSources.Any(vs => videoSource.Caption == vs.Caption))
            //{
            //    return _videoSources.FirstOrDefault(x => x.Caption == videoSource.Caption);
            //}

            //if (_videoSources.Any(x => x.Url == videoSource.Url))
            //{
            //    return _videoSources.FirstOrDefault(x => x.Url == videoSource.Url);
            //}

            //_videoSources.Add(videoSource);
            _videoSources.TryAdd(videoSource.EntityID, videoSource);

            if (OnVideoSourceCreated != null)
            {
                OnVideoSourceCreated(videoSource);
            }

            return videoSource;
        }

        public void UpdateVideoSource(VideoSource videoSource)
        {
            if (!_videoSources.ContainsKey(videoSource.EntityID))
            {
                throw new Exception("Duplicate Url");
            }

            if (_videoSources.ContainsKey(videoSource.EntityID))
            {
                _videoSources[videoSource.EntityID] = videoSource;
                if (OnVideoSourceUpdated != null)
                {
                    OnVideoSourceUpdated(videoSource);
                }
            }
        }

        public void DeleteVideoSource(int videoSourceId)
        {
            if (_videoSources.TryRemove(videoSourceId, out var removeItem))
            {
                if (OnVideoSourceDeleted != null)
                {
                    OnVideoSourceDeleted(removeItem);
                }
            }
            //VideoSource findItem = VideoSources.FirstOrDefault(x => x.Id == videoSourceId);
            //if (findItem != null)
            //{
            //    if (Remove<VideoSource>(VideoSources, findItem))
            //    {
            //        if (OnVideoSourceDeleted != null)
            //        {
            //            OnVideoSourceDeleted(findItem);
            //        }
            //    }
            //}
        }

        public VideoSource GetVideoSourceById(int videoSourceId)
        {
            if (_videoSources.ContainsKey(videoSourceId))
                return _videoSources[videoSourceId];
            return null;
        }

        //public VideoSource GetVideoSourceByRequestURL(string requestUrl)
        //{
        //    var findItem = _videoSources.FirstOrDefault(x => x.Value.RequestURL == requestUrl);

        //    if (!findItem.Equals(new KeyValuePair<Guid, VideoSource>()))
        //    {
        //        return findItem.Value;
        //    }
        //    return null;
        //}

        //public VideoSource GetVideoSourceByRtspURL(string rtspURL)
        //{
        //    var findItem = _videoSources.FirstOrDefault(x => x.Value.RtspURL == rtspURL);

        //    if (!findItem.Equals(new KeyValuePair<Guid, VideoSource>()))
        //    {
        //        return findItem.Value;
        //    }
        //    return null;
        //}
    }
}
