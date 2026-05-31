using OPNX.Lib.Streaming.RTSP.Commons.Interfaces;
using OPNX.Lib.Streaming.RTSP.Messages;

namespace OPNX.Lib.Streaming.RTSP
{
    public enum ChannelTypes { None, Video, Audio }
    public class StreamConfigurationDataEventArgs(ChannelTypes channelType, string payloadName, IStreamConfigurationData? streamConfigurationData) : EventArgs
    {
        public ChannelTypes ChannelType { get; } = channelType;
        public string PayloadName { get; } = payloadName;
        public IStreamConfigurationData? StreamConfigurationData { get; } = streamConfigurationData;
    }

    public class NewStreamEventArgs(string streamType, IStreamConfigurationData streamConfigurationData) : EventArgs
    {
        public string StreamType { get; } = streamType;
        public IStreamConfigurationData StreamConfigurationData { get; } = streamConfigurationData;
    }

    public interface IStreamConfigurationData
    {
    }

    public record H264StreamConfigurationData : IStreamConfigurationData
    {
        public List<byte[]>? OutOfBandNal { get; init; }
    }

    public record H265StreamConfigurationData : IStreamConfigurationData
    {
        public List<byte[]>? OutOfBandNal { get; init; }
    }

    public record AacStreamConfigurationData : IStreamConfigurationData
    {
        public uint ObjectType { get; init; }
        public uint FrequencyIndex { get; init; }
        public uint SamplingFrequency { get; init; }
        public uint ChannelConfiguration { get; init; }
    }


    public class NalUnitDataEventArgs(ChannelTypes channelType, string payloadName, bool isKeyFrame, IEnumerable<ReadOnlyMemory<byte>> data, uint timeStamp) : EventArgs
    {
        public ChannelTypes ChannelType { get; } = channelType;
        public uint TimeStamp { get; } = timeStamp;
        public IEnumerable<ReadOnlyMemory<byte>> Data { get; } = data;
        public bool IsKeyFrame { get; } = isKeyFrame;
        public string PayloadName { get; } = payloadName;
    }

    public class RTPDataEventArgs(ChannelTypes channelType, Memory<byte> data) : EventArgs
    {
        public ChannelTypes ChannelType { get; } = channelType;
        public Memory<byte> Data { get; } = data;
    }

    public class StreamStartedEventArgs : EventArgs
    {
    }

    public class StreamStoppedEventArgs(RTSPClientStopReason stopReason) : EventArgs
    {
        public RTSPClientStopReason StopReason { get; } = stopReason;
    }

    public class RtspDataEventArgs(RtspData data) : EventArgs
    {
        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public RtspData Data { get; set; } = data;
    }

    public class SimpleDataEventArgs(List<ReadOnlyMemory<byte>> data, DateTime clockTimeStamp, ulong rtpTimeStamp, int baseClock, int payloadType) : EventArgs
    {
        public int PayloadType { get; } = payloadType;
        public int BaseClock { get; } = baseClock;
        public ulong RtpTimestamp { get; } = rtpTimeStamp;
        public DateTime ClockTimeStamp { get; } = clockTimeStamp;
        public List<ReadOnlyMemory<byte>> Data { get; } = data;
    }
}
