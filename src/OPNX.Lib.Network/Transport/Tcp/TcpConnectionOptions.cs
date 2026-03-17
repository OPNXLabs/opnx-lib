namespace OPNX.Lib.Network.Transport.Tcp
{
    public readonly record struct TcpConnectionOptions(
        ConnectionOptions Common,
        int StreamReadTimeoutMs,
        int StreamWriteTimeoutMs,
        int SocketBufferSize,
        bool LingerEnabled,
        int LingerTimeSec,
        bool NoDelay,
        bool ReuseAddress,
        TcpKeepAliveOptions KeepAlive)
    {
        public static TcpConnectionOptions Default => new(
            Common: ConnectionOptions.Default with { EnableReconnect = true },
            StreamReadTimeoutMs: 3000,
            StreamWriteTimeoutMs: 3000,
            SocketBufferSize: 1024 * 1024,
            LingerEnabled: true,
            LingerTimeSec: 0,
            NoDelay: true,
            ReuseAddress: true,
            KeepAlive: TcpKeepAliveOptions.Default);
    }
}
