using OPNX.Lib.Network.Protocol.Abstractions;
using OPNX.Lib.Network.Transport.Tcp;
using System.Threading.Channels;

namespace OPNX.Lib.Network.Protocol.FileTransfer
{
    public sealed class FileTransferServerOptions
    {
        public int ChunkSize { get; set; } = 512 * 1024;
        public int MaximumConcurrentTransfers { get; set; } = 4;
        public TimeSpan PrepareExpiration { get; set; } = TimeSpan.FromMinutes(1);
        public IReadOnlyList<string> AllowedRootPaths { get; set; } = [];
        public Func<string, CancellationToken, ValueTask<bool>>? AuthorizeFileAsync { get; set; }
        public TcpConnectionOptions ConnectionOptions { get; set; } = TcpConnectionOptions.Default with
        {
            Common = OPNX.Lib.Network.Transport.ConnectionOptions.Default with { EnableReconnect = false }
        };
        public ProtocolOptions ProtocolOptions { get; set; } = new(
            CompressThresholdBytes: int.MaxValue,
            OutboundChannelCapacity: 8,
            InboundChannelCapacity: 8,
            ChannelFullMode: BoundedChannelFullMode.Wait);
    }

    public sealed class FileTransferClientOptions
    {
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public bool DeletePartialFileOnFailure { get; set; } = true;
        public TcpConnectionOptions ConnectionOptions { get; set; } = TcpConnectionOptions.Default with
        {
            Common = OPNX.Lib.Network.Transport.ConnectionOptions.Default with { EnableReconnect = false }
        };
        public ProtocolOptions ProtocolOptions { get; set; } = new(
            CompressThresholdBytes: int.MaxValue,
            OutboundChannelCapacity: 8,
            InboundChannelCapacity: 8,
            ChannelFullMode: BoundedChannelFullMode.Wait);
    }
}
