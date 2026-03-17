namespace OPNX.Lib.Network.Transport
{
    public readonly record struct ConnectionOptions(
        int ConnectTimeoutMs,
        int ReceiveTimeoutMs,
        int SendTimeoutMs,
        bool EnableReconnect,
        int ReconnectDelayMs)
    {
        public static ConnectionOptions Default => new(
            ConnectTimeoutMs: 3000,
            ReceiveTimeoutMs: 5000,
            SendTimeoutMs: 3000,
            EnableReconnect: false,
            ReconnectDelayMs: 1000);
    }
}
