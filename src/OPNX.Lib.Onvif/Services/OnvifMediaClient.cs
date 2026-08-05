using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Internal;
using OPNX.Lib.Onvif.Models;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Services
{
    public sealed class OnvifMediaClient(IOnvifSoapTransport transport, Uri endpoint) : OnvifServiceClient(transport, endpoint), IOnvifMediaClient
    {
        public async Task<IReadOnlyList<OnvifProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
        {
            XNamespace media = OnvifNamespaces.Media;
            var document = await SendAsync($"{OnvifNamespaces.Media}/GetProfiles", new XElement(media + "GetProfiles"), cancellationToken).ConfigureAwait(false);
            return document.DescendantsNamed("Profiles").Select(x => new OnvifProfile(
                x.AttributeValue("token") ?? string.Empty,
                x.Descendant("Name")?.Value,
                x.Descendant("VideoSourceConfiguration")?.Descendant("SourceToken")?.Value,
                x.Descendant("PTZConfiguration")?.AttributeValue("token"))).Where(x => !string.IsNullOrEmpty(x.Token)).ToList();
        }

        public async Task<Uri?> GetStreamUriAsync(string profileToken, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(profileToken);
            XNamespace media = OnvifNamespaces.Media;
            XNamespace schema = OnvifNamespaces.Schema;
            var body = new XElement(media + "GetStreamUri",
                new XElement(media + "StreamSetup",
                    new XElement(schema + "Stream", "RTP-Unicast"),
                    new XElement(schema + "Transport", new XElement(schema + "Protocol", "RTSP"))),
                new XElement(media + "ProfileToken", profileToken));
            var document = await SendAsync($"{OnvifNamespaces.Media}/GetStreamUri", body, cancellationToken).ConfigureAwait(false);
            return Uri.TryCreate(document.Descendant("Uri")?.Value, UriKind.Absolute, out var uri) ? uri : null;
        }
    }
}
