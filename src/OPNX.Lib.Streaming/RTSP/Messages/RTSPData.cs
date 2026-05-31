using System.Buffers;
using System.Text;

namespace OPNX.Lib.Streaming.RTSP.Messages
{
    /// <summary>
    /// Message wich represent data. ($ limited message)
    /// </summary>
    public sealed class RtspData : RtspChunk, IDisposable
    {

        private IMemoryOwner<byte>? reservedData;
        private bool disposedValue;

        public RtspData() { }

        public RtspData(IMemoryOwner<byte> reservedData, int size)
        {
            this.reservedData = reservedData;
            base.Data = reservedData.Memory[..size];
        }

        public override Memory<byte> Data
        {
            get
            {
                return base.Data;
            }
            set
            {
                reservedData?.Dispose();
                reservedData = null;
                base.Data = value;
            }
        }

        /// <summary>
        /// Create a string of the message for debug.
        /// </summary>
        public override string ToString()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("Data message");
            stringBuilder.AppendLine(Data.IsEmpty ? "Data : null" : $"Data length :-{Data.Length}-");

            return stringBuilder.ToString();
        }

        public int Channel { get; set; }

        /// <summary>
        /// Clones this instance.
        /// <remarks>Listner is not cloned</remarks>
        /// </summary>
        /// <returns>a clone of this instance</returns>
        public override object Clone() => new RtspData
        {
            Channel = Channel,
            SourcePort = SourcePort,
            Data = Data,
        };

        private void Dispose(bool disposing)
        {
            if (disposedValue) return;

            if (disposing)
            {
                reservedData?.Dispose();
            }
            Data = Memory<byte>.Empty;
            disposedValue = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
