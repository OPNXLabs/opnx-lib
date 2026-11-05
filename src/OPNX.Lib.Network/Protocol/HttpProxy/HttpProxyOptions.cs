using System.Net;

namespace OPNX.Lib.Network.Protocol.HttpProxy
{
    public sealed class HttpProxyOptions
    {
        #region Properties
        public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;
        public int MaximumConcurrentConnections { get; init; } = 128;
        public int MaximumHeaderBytes { get; init; } = 32 * 1024;
        public int MaximumRequestBodyBytes { get; init; } = 8 * 1024 * 1024;
        public TimeSpan HeaderTimeout { get; init; } = TimeSpan.FromSeconds(15);
        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
        public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>Optional proxy authentication. Failure returns 407 with AuthenticationChallenge.</summary>
        public Func<HttpProxyRequestContext, CancellationToken, ValueTask<bool>>? AuthenticateAsync { get; init; }
        public string AuthenticationChallenge { get; init; } = "Basic realm=\"Proxy\"";

        /// <summary>Required access policy. Requests are denied when no policy is supplied.</summary>
        public Func<HttpProxyRequestContext, CancellationToken, ValueTask<bool>>? AuthorizeRequestAsync { get; init; }
        #endregion
    }

    /// <summary>ProxyAuthorization is supplied only to the caller's policy and is never forwarded.</summary>
    public sealed record HttpProxyRequestContext(
        IPEndPoint ClientEndPoint,
        string Method,
        Uri Destination,
        string? ProxyAuthorization);
}
