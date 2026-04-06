using System.Threading.Channels;

namespace OPNX.Lib.Network.Protocol.Abstractions
{
    public readonly record struct ProtocolOptions(
        int CompressThresholdBytes = 2 * 1024 * 1024,
        int OutboundChannelCapacity = 1024,
        int InboundChannelCapacity = 1024,
        BoundedChannelFullMode ChannelFullMode = BoundedChannelFullMode.Wait,
        bool EnableDiagnostics = false)
    {
        public static ProtocolOptions Default => new();
    }
}
