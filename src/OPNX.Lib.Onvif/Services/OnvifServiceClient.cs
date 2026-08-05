using OPNX.Lib.Onvif.Abstractions;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Services
{
    public abstract class OnvifServiceClient
    {
        protected OnvifServiceClient(IOnvifSoapTransport transport, Uri endpoint)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        protected IOnvifSoapTransport Transport { get; }
        protected Uri Endpoint { get; }

        protected Task<XDocument> SendAsync(string action, XElement body, CancellationToken cancellationToken) =>
            Transport.SendAsync(Endpoint, action, body, cancellationToken);
    }
}
