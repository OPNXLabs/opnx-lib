using OPNX.Lib.Onvif.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif
{
    public sealed class OnvifDiscovery
    {
        private static readonly IPEndPoint DiscoveryEndpoint = new(IPAddress.Parse("239.255.255.250"), 3702);
        private readonly OnvifDiscoveryOptions _options;

        public OnvifDiscovery(OnvifDiscoveryOptions? options = null)
        {
            _options = options ?? new OnvifDiscoveryOptions();
            ValidateOptions(_options);
        }

        public Task<OnvifDiscoveryResult?> DiscoverAsync(
            string ipAddress,
            string userName,
            string password,
            CancellationToken cancellationToken = default)
        {
            if (!IPAddress.TryParse(ipAddress, out var address))
                throw new ArgumentException($"'{ipAddress}' is not a valid IP address.", nameof(ipAddress));

            return DiscoverAsync(address, userName, password, cancellationToken);
        }

        public async Task<OnvifDiscoveryResult?> DiscoverAsync(
            IPAddress address,
            string userName,
            string password,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(address);

            var candidates = new ConcurrentDictionary<string, DiscoveryCandidate>(StringComparer.OrdinalIgnoreCase);
            if (_options.UseWsDiscovery && address.AddressFamily == AddressFamily.InterNetwork)
            {
                foreach (var uri in await DiscoverDeviceServiceUrisAsync(address, cancellationToken).ConfigureAwait(false))
                    candidates.TryAdd(uri.AbsoluteUri, new(uri, OnvifDiscoveryMethod.WsDiscovery));
            }

            foreach (var uri in CreateEndpointCandidates(address))
                candidates.TryAdd(uri.AbsoluteUri, new(uri, OnvifDiscoveryMethod.EndpointProbe));

            return await ProbeCandidatesAsync(address, candidates.Values, userName ?? string.Empty,
                password ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }

        private async Task<IReadOnlyList<Uri>> DiscoverDeviceServiceUrisAsync(
            IPAddress targetAddress,
            CancellationToken cancellationToken)
        {
            using var udpClient = new UdpClient(AddressFamily.InterNetwork);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.WsDiscoveryTimeout);

            var message = Encoding.UTF8.GetBytes(CreateProbeMessage());
            await udpClient.SendAsync(message, DiscoveryEndpoint, timeoutCts.Token).ConfigureAwait(false);

            var results = new HashSet<Uri>();
            try
            {
                while (!timeoutCts.IsCancellationRequested)
                {
                    var response = await udpClient.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
                    foreach (var uri in ParseXAddrs(response.Buffer))
                    {
                        if (IsTargetAddress(uri, targetAddress))
                            results.Add(uri);
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            return results.ToList();
        }

        private async Task<OnvifDiscoveryResult?> ProbeCandidatesAsync(
            IPAddress address,
            IEnumerable<DiscoveryCandidate> candidates,
            string userName,
            string password,
            CancellationToken cancellationToken)
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var concurrency = new SemaphoreSlim(_options.MaximumConcurrency);
            var resultSource = new TaskCompletionSource<OnvifDiscoveryResult?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var tasks = candidates
                .OrderBy(candidate => candidate.Method)
                .Select(async candidate =>
                {
                    try
                    {
                        await concurrency.WaitAsync(probeCts.Token).ConfigureAwait(false);
                        try
                        {
                            var result = await ProbeAsync(address, candidate, userName, password, probeCts.Token).ConfigureAwait(false);
                            if (result != null && resultSource.TrySetResult(result))
                                probeCts.Cancel();
                        }
                        finally
                        {
                            concurrency.Release();
                        }
                    }
                    catch (OperationCanceledException) when (probeCts.IsCancellationRequested)
                    {
                    }
                })
                .ToArray();

            var allTasks = Task.WhenAll(tasks);
            var completed = await Task.WhenAny(resultSource.Task, allTasks).ConfigureAwait(false);
            if (completed == resultSource.Task)
            {
                var result = await resultSource.Task.ConfigureAwait(false);
                probeCts.Cancel();
                await allTasks.ConfigureAwait(false);
                return result;
            }

            await allTasks.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        private async Task<OnvifDiscoveryResult?> ProbeAsync(
            IPAddress address,
            DiscoveryCandidate candidate,
            string userName,
            string password,
            CancellationToken cancellationToken)
        {
            using var handler = new HttpClientHandler();
            if (_options.AllowUntrustedCertificates && candidate.Uri.Scheme == Uri.UriSchemeHttps)
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            using var httpClient = new HttpClient(handler);
            var clientOptions = new OnvifClientOptions
            {
                DeviceServiceUri = candidate.Uri,
                UserName = userName,
                Password = password,
                RequestTimeout = _options.RequestTimeout
            };

            try
            {
                await using var client = new OnvifClient(clientOptions, httpClient);
                await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
                return new OnvifDiscoveryResult(
                    address,
                    candidate.Uri,
                    candidate.Method,
                    client.Capabilities.Services.Values.ToList());
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        private IEnumerable<Uri> CreateEndpointCandidates(IPAddress address)
        {
            var host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
            foreach (var port in _options.Ports.Distinct())
            {
                foreach (var path in _options.ServicePaths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (_options.TryHttp)
                        yield return new Uri($"http://{host}:{port}{NormalizePath(path)}");

                    if (_options.TryHttps)
                        yield return new Uri($"https://{host}:{port}{NormalizePath(path)}");
                }
            }
        }

        private static IEnumerable<Uri> ParseXAddrs(byte[] response)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(Encoding.UTF8.GetString(response));
            }
            catch
            {
                yield break;
            }

            foreach (var value in document.Descendants().Where(element => element.Name.LocalName == "XAddrs").Select(element => element.Value))
            {
                foreach (var item in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Uri.TryCreate(item, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        yield return uri;
                }
            }
        }

        private static bool IsTargetAddress(Uri uri, IPAddress targetAddress) =>
            IPAddress.TryParse(uri.Host, out var address) && address.Equals(targetAddress);

        private static string CreateProbeMessage()
        {
            var messageId = Guid.NewGuid();
            return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                            xmlns:a="http://www.w3.org/2005/08/addressing"
                            xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
                            xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
                  <s:Header>
                    <a:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>
                    <a:MessageID>urn:uuid:{messageId:D}</a:MessageID>
                    <a:ReplyTo><a:Address>http://www.w3.org/2005/08/addressing/anonymous</a:Address></a:ReplyTo>
                    <a:To s:mustUnderstand="1">urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To>
                  </s:Header>
                  <s:Body>
                    <d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe>
                  </s:Body>
                </s:Envelope>
                """;
        }

        private static string NormalizePath(string path) => path.StartsWith('/') ? path : $"/{path}";

        private static void ValidateOptions(OnvifDiscoveryOptions options)
        {
            if (options.Ports.Count == 0 || options.Ports.Any(port => port is < 1 or > 65535))
                throw new ArgumentOutOfRangeException(nameof(options), "Discovery ports must be between 1 and 65535.");
            if (options.ServicePaths.Count == 0 || options.ServicePaths.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("At least one ONVIF service path is required.", nameof(options));
            if (options.WsDiscoveryTimeout <= TimeSpan.Zero || options.RequestTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Discovery timeouts must be greater than zero.");
            if (options.MaximumConcurrency < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Maximum concurrency must be greater than zero.");
            if (!options.TryHttp && !options.TryHttps)
                throw new ArgumentException("HTTP or HTTPS endpoint probing must be enabled.", nameof(options));
        }

        private sealed record DiscoveryCandidate(Uri Uri, OnvifDiscoveryMethod Method);
    }
}
