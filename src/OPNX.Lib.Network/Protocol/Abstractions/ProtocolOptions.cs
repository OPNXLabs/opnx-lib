namespace OPNX.Lib.Network.Protocol.Abstractions
{
    public readonly record struct ProtocolOptions(int CompressThresholdBytes = 2 * 1024 * 1024)
    {
        public static ProtocolOptions Default => new();
    }
}
