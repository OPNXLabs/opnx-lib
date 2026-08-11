using System.Net;

namespace OPNX.Lib.Onvif.Models
{
    public sealed class OnvifDiscoveryOptions
    {
        public IReadOnlyList<int> Ports { get; init; } = [80, 443, 8000, 8080, 8081, 8899];
        public IReadOnlyList<string> ServicePaths { get; init; } = ["/onvif/device_service", "/onvif/Device_service"];
        public TimeSpan WsDiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(2);
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(2);
        public int MaximumConcurrency { get; init; } = 1;
        public bool UseWsDiscovery { get; init; } = true;
        public bool TryHttp { get; init; } = true;
        public bool TryHttps { get; init; } = true;
        public bool AllowUntrustedCertificates { get; init; } = true;
    }

    public enum OnvifDiscoveryMethod
    {
        WsDiscovery,
        EndpointProbe
    }

    public sealed record OnvifDiscoveryResult(
        IPAddress Address,
        Uri DeviceServiceUri,
        OnvifDiscoveryMethod DiscoveryMethod,
        IReadOnlyList<OnvifService> Services)
    {
        public Uri? MediaServiceUri => Find(OnvifNamespaces.Media);
        public Uri? PtzServiceUri => Find(OnvifNamespaces.Ptz);
        public Uri? ImagingServiceUri => Find(OnvifNamespaces.Imaging);
        public Uri? DeviceIoServiceUri => Find(OnvifNamespaces.DeviceIo);
        public Uri? EventServiceUri => Find(OnvifNamespaces.Events);

        private Uri? Find(string serviceNamespace) =>
            Services.FirstOrDefault(service => service.Namespace == serviceNamespace)?.Uri;
    }
}
