using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Internal;
using OPNX.Lib.Onvif.Models;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Services
{
    public sealed class OnvifDeviceClient(IOnvifSoapTransport transport, Uri endpoint) : OnvifServiceClient(transport, endpoint), IOnvifDeviceClient
    {
        public async Task<DateTimeOffset?> GetSystemDateAndTimeAsync(CancellationToken cancellationToken = default)
        {
            XNamespace device = OnvifNamespaces.Device;
            var document = await SendAsync($"{OnvifNamespaces.Device}/GetSystemDateAndTime", new XElement(device + "GetSystemDateAndTime"), cancellationToken).ConfigureAwait(false);
            var utc = document.Descendant("UTCDateTime");
            if (utc is null)
                return null;

            var date = utc.Descendant("Date");
            var time = utc.Descendant("Time");
            if (!int.TryParse(date?.Descendant("Year")?.Value, out var year) ||
                !int.TryParse(date?.Descendant("Month")?.Value, out var month) ||
                !int.TryParse(date?.Descendant("Day")?.Value, out var day) ||
                !int.TryParse(time?.Descendant("Hour")?.Value, out var hour) ||
                !int.TryParse(time?.Descendant("Minute")?.Value, out var minute) ||
                !int.TryParse(time?.Descendant("Second")?.Value, out var second))
                return null;

            return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
        }

        public async Task<IReadOnlyList<OnvifService>> GetServicesAsync(CancellationToken cancellationToken = default)
        {
            XNamespace device = OnvifNamespaces.Device;
            var document = await SendAsync($"{OnvifNamespaces.Device}/GetServices",
                new XElement(device + "GetServices", new XElement(device + "IncludeCapability", false)), cancellationToken).ConfigureAwait(false);

            return document.DescendantsNamed("Service").Select(ParseService).Where(x => x is not null).Cast<OnvifService>().ToList();
        }

        private static OnvifService? ParseService(XElement element)
        {
            var serviceNamespace = element.Descendant("Namespace")?.Value;
            var xaddr = element.Descendant("XAddr")?.Value;
            if (string.IsNullOrWhiteSpace(serviceNamespace) || !Uri.TryCreate(xaddr, UriKind.Absolute, out var uri))
                return null;

            var major = element.Descendant("Major")?.Value;
            var minor = element.Descendant("Minor")?.Value;
            var version = major is null ? null : $"{major}.{minor ?? "0"}";
            return new OnvifService(serviceNamespace, uri, version);
        }
    }
}
