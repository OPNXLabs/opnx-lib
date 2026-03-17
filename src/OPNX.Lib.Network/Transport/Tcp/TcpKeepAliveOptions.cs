namespace OPNX.Lib.Network.Transport.Tcp
{
    public readonly record struct TcpKeepAliveOptions(
        bool Enabled,
        int TimeMs,
        int IntervalMs,
        int RetryCount)
    {
        public static TcpKeepAliveOptions Default => new(
            Enabled: true,
            TimeMs: 2000,
            IntervalMs: 1000,
            RetryCount: 3);
    }
}
