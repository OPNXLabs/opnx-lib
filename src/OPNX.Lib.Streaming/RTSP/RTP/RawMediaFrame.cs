using System.Buffers;

namespace OPNX.Lib.Streaming.RTSP.RTP
{
    public class RawMediaFrame(IEnumerable<ReadOnlyMemory<byte>> data, IEnumerable<IMemoryOwner<byte>> owners) : IDisposable
    {
        #region Fields
        private bool disposedValue;
        private readonly IEnumerable<ReadOnlyMemory<byte>> _data = data;
        private readonly IEnumerable<IMemoryOwner<byte>> _owners = owners;
        #endregion        

        #region Properties
        public IEnumerable<ReadOnlyMemory<byte>> Data
        {
            get
            {
                ObjectDisposedException.ThrowIf(disposedValue, this);
                return _data;
            }
        }

        public DateTime ClockTimestamp { get; init; }
        public uint RtpTimestamp { get; init; }
        public bool IsKeyFrame { get; init; }

        public bool Any() => Data.Any();

        public static RawMediaFrame Empty => new([], []) { RtpTimestamp = 0, ClockTimestamp = DateTime.MinValue };
        #endregion

        #region Public Methods
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Private / Protected Methods
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    foreach (var owner in _owners)
                    {
                        owner?.Dispose();
                    }
                }
                disposedValue = true;
            }
        }
        #endregion
    }
}