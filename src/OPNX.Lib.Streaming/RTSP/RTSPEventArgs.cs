using OPNX.Lib.Streaming.RTSP.Commons.Interfaces;
using OPNX.Lib.Streaming.RTSP.Messages;

namespace OPNX.Lib.Streaming.RTSP
{
    public enum ChannelTypes { None, Video, Audio }
    public class StreamConfigurationDataEventArgs : EventArgs
    {
        public StreamConfigurationDataEventArgs(ChannelTypes channelType, string payloadName, IStreamConfigurationData streamConfigurationData)
        {
            ChannelType = channelType;
            PayloadName = payloadName;
            StreamConfigurationData = streamConfigurationData;
        }

        public ChannelTypes ChannelType { get; }
        public string PayloadName { get; }
        public IStreamConfigurationData StreamConfigurationData { get; }
    }

    public class NewStreamEventArgs : EventArgs
    {
        public NewStreamEventArgs(string streamType, IStreamConfigurationData streamConfigurationData)
        {
            StreamType = streamType;
            StreamConfigurationData = streamConfigurationData;
        }

        public string StreamType { get; }
        public IStreamConfigurationData StreamConfigurationData { get; }
    }

    public interface IStreamConfigurationData
    {
    }

    public record H264StreamConfigurationData : IStreamConfigurationData
    {
        public List<byte[]> OutOfBandNal { get; init; }
    }

    public record H265StreamConfigurationData : IStreamConfigurationData
    {
        public List<byte[]> OutOfBandNal { get; init; }
    }

    public record AacStreamConfigurationData : IStreamConfigurationData
    {
        public uint ObjectType { get; init; }
        public uint FrequencyIndex { get; init; }
        public uint SamplingFrequency { get; init; }
        public uint ChannelConfiguration { get; init; }
    }


    public class NalUnitDataEventArgs : EventArgs
    {
        public NalUnitDataEventArgs(ChannelTypes channelType, string payloadName, bool isKeyFrame, IEnumerable<ReadOnlyMemory<byte>> data, uint timeStamp)
        {
            ChannelType = channelType;
            PayloadName = payloadName;
            Data = data;
            TimeStamp = timeStamp;
            IsKeyFrame = isKeyFrame;
        }

        public ChannelTypes ChannelType { get; }
        public uint TimeStamp { get; }
        public IEnumerable<ReadOnlyMemory<byte>> Data { get; }
        public bool IsKeyFrame { get; }
        public string PayloadName { get; }
    }

    public class RTPDataEventArgs : EventArgs
    {
        public RTPDataEventArgs(ChannelTypes channelType, Memory<byte> data)
        {
            ChannelType = channelType;
            Data = data;
        }
        public ChannelTypes ChannelType { get; }
        public Memory<byte> Data { get; }
    }

    public class StreamStartedEventArgs : EventArgs
    {
    }

    public class StreamStoppedEventArgs : EventArgs
    {
        public StreamStoppedEventArgs(RTSPClientStopReason stopReason)
        {
            StopReason = stopReason;
        }

        public RTSPClientStopReason StopReason { get; }
    }

    public class RtspDataEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPDataEventArgs"/> class.
        /// </summary>
        /// <param name="data">Data .</param>
        public RtspDataEventArgs(RtspData data)
        {
            Data = data;
        }

        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public RtspData Data { get; set; }
    }

    public class SimpleDataEventArgs : EventArgs
    {
        public SimpleDataEventArgs(List<ReadOnlyMemory<byte>> data, DateTime clockTimeStamp, ulong rtpTimeStamp, int baseClock, int payloadType)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            ClockTimeStamp = clockTimeStamp;
            RtpTimestamp = rtpTimeStamp;
            BaseClock = baseClock;
            PayloadType = payloadType;
        }

        public int PayloadType { get; }
        public int BaseClock { get; }
        public ulong RtpTimestamp { get; }
        public DateTime ClockTimeStamp { get; }
        public List<ReadOnlyMemory<byte>> Data { get; }
    }
}
