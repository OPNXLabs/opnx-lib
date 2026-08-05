using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Models;
using OPNX.Lib.Onvif.Services;
using OPNX.Lib.Onvif.Transport;

namespace OPNX.Lib.Onvif
{
    public sealed class OnvifClient : IOnvifClient
    {
        private readonly IOnvifSoapTransport _transport;
        private readonly OnvifClientOptions _options;

        public OnvifClient(OnvifClientOptions options, HttpClient? httpClient = null)
            : this(options, new OnvifSoapTransport(options, httpClient))
        {
        }

        public OnvifClient(OnvifClientOptions options, IOnvifSoapTransport transport)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Device = new OnvifDeviceClient(_transport, options.DeviceServiceUri);
        }

        public OnvifCapabilities Capabilities { get; } = new();
        public IOnvifDeviceClient Device { get; }
        public IOnvifMediaClient? Media { get; private set; }
        public IOnvifPtzClient? Ptz { get; private set; }
        public IOnvifImagingClient? Imaging { get; private set; }
        public IOnvifDeviceIoClient? DeviceIo { get; private set; }
        public IOnvifEventClient? Events { get; private set; }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var deviceTime = await Device.GetSystemDateAndTimeAsync(cancellationToken).ConfigureAwait(false);
            if (deviceTime.HasValue)
                _transport.ClockOffset = deviceTime.Value - DateTimeOffset.UtcNow;

            var services = await Device.GetServicesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var service in services)
                Capabilities.Set(NormalizeService(service));

            Media = Create(Capabilities.MediaUri, uri => new OnvifMediaClient(_transport, uri));
            Ptz = Create(Capabilities.PtzUri, uri => new OnvifPtzClient(_transport, uri));
            Imaging = Create(Capabilities.ImagingUri, uri => new OnvifImagingClient(_transport, uri));
            DeviceIo = Create(Capabilities.DeviceIoUri, uri => new OnvifDeviceIoClient(_transport, uri));
            Events = Create(Capabilities.EventsUri, uri => new OnvifEventClient(_transport, uri));
        }

        private OnvifService NormalizeService(OnvifService service)
        {
            var normalizedUri = _options.DevicePolicy.NormalizeServiceUri(_options.DeviceServiceUri, service.Uri);
            return service with { Uri = normalizedUri };
        }

        private static T? Create<T>(Uri? endpoint, Func<Uri, T> factory) where T : class => endpoint is null ? null : factory(endpoint);
        public ValueTask DisposeAsync() => _transport.DisposeAsync();
    }
}
