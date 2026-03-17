namespace OPNX.Lib.Network.Transport.NamedPipe
{
    public readonly record struct NamedPipeConnectionOptions(ConnectionOptions Common)
    {
        public static NamedPipeConnectionOptions Default => new(
            Common: ConnectionOptions.Default);
    }

}
