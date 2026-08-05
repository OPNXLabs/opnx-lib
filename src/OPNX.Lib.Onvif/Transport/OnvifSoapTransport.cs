using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Models;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Transport
{
    public sealed class OnvifSoapTransport : IOnvifSoapTransport
    {
        private const string PasswordDigestType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest";
        private const string PasswordTextType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordText";
        private const string Base64EncodingType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";

        private readonly HttpClient _httpClient;
        private readonly OnvifClientOptions _options;
        private readonly bool _ownsHttpClient;

        public OnvifSoapTransport(OnvifClientOptions options, HttpClient? httpClient = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            _options = options;
            _ownsHttpClient = httpClient is null;
            _httpClient = httpClient ?? new HttpClient();
            _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        }

        public TimeSpan ClockOffset { get; set; }

        public async Task<XDocument> SendAsync(Uri endpoint, string action, XElement body, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            ArgumentNullException.ThrowIfNull(body);

            var operation = GetOperation(action);
            var timeout = _options.DevicePolicy.GetRequestTimeout(operation, _options.RequestTimeout);

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await SendCoreAsync(endpoint, action, body, timeout, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                    _options.DevicePolicy.ShouldRetry(operation, exception, attempt))
                {
                }
            }
        }

        private async Task<XDocument> SendCoreAsync(Uri endpoint, string action, XElement body, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var envelope = CreateEnvelope(endpoint, action, body);
            using var request = CreateRequest(endpoint, action, envelope);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            var responseXml = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            XDocument? document = null;

            if (!string.IsNullOrWhiteSpace(responseXml))
            {
                try
                {
                    document = XDocument.Parse(responseXml, LoadOptions.PreserveWhitespace);
                }
                catch (XmlException) when (!response.IsSuccessStatusCode)
                {
                }
            }

            var fault = document?.Descendants(XName.Get("Fault", OnvifNamespaces.Soap)).FirstOrDefault();
            if (fault is not null)
                throw CreateFault(response.StatusCode, fault, responseXml);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"ONVIF SOAP request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).", null, response.StatusCode);

            return document ?? throw new InvalidDataException("The ONVIF device returned an empty SOAP response.");
        }

        private static HttpRequestMessage CreateRequest(Uri endpoint, string action, XDocument envelope)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8)
            };

            request.Content.Headers.ContentType = new("application/soap+xml")
            {
                CharSet = "utf-8",
                Parameters = { new("action", $"\"{action}\"") }
            };
            return request;
        }

        private static OnvifOperation GetOperation(string action)
        {
            var operationName = action[(action.LastIndexOf('/') + 1)..];
            return operationName switch
            {
                "GetSystemDateAndTime" => OnvifOperation.GetSystemDateAndTime,
                "GetServices" => OnvifOperation.GetServices,
                "GetProfiles" => OnvifOperation.GetProfiles,
                "GetStreamUri" => OnvifOperation.GetStreamUri,
                "GetOptions" or "GetConfigurationOptions" => OnvifOperation.GetOptions,
                "GetStatus" => OnvifOperation.GetStatus,
                "ContinuousMove" => OnvifOperation.ContinuousMove,
                "RelativeMove" => OnvifOperation.RelativeMove,
                "AbsoluteMove" => OnvifOperation.AbsoluteMove,
                "Stop" => OnvifOperation.Stop,
                "SetHomePosition" => OnvifOperation.SetHomePosition,
                "GotoHomePosition" => OnvifOperation.GotoHomePosition,
                "GetPresets" => OnvifOperation.GetPresets,
                "SetPreset" => OnvifOperation.SetPreset,
                "GotoPreset" => OnvifOperation.GotoPreset,
                "RemovePreset" => OnvifOperation.RemovePreset,
                "Move" => OnvifOperation.MoveFocus,
                "GetImagingSettings" => OnvifOperation.GetImagingSettings,
                "SetImagingSettings" => OnvifOperation.SetImagingSettings,
                "GetDigitalInputs" => OnvifOperation.GetDigitalInputs,
                "GetRelayOutputs" => OnvifOperation.GetRelayOutputs,
                "SetRelayOutputSettings" => OnvifOperation.SetRelayOutputSettings,
                "SetRelayOutputState" => OnvifOperation.SetRelayOutputState,
                "CreatePullPointSubscriptionRequest" => OnvifOperation.CreatePullPointSubscription,
                "PullMessagesRequest" => OnvifOperation.PullMessages,
                "RenewRequest" => OnvifOperation.Renew,
                "UnsubscribeRequest" => OnvifOperation.Unsubscribe,
                _ => OnvifOperation.Unknown
            };
        }

        private XDocument CreateEnvelope(Uri endpoint, string action, XElement body)
        {
            XNamespace soap = OnvifNamespaces.Soap;
            XNamespace addressing = OnvifNamespaces.WsAddressing;
            var header = new XElement(soap + "Header",
                new XElement(addressing + "Action", new XAttribute(soap + "mustUnderstand", "1"), action),
                new XElement(addressing + "MessageID", $"urn:uuid:{Guid.NewGuid():D}"),
                new XElement(addressing + "To", new XAttribute(soap + "mustUnderstand", "1"), endpoint.AbsoluteUri),
                new XElement(addressing + "ReplyTo", new XElement(addressing + "Address", "http://www.w3.org/2005/08/addressing/anonymous")));

            if (!string.IsNullOrWhiteSpace(_options.UserName))
                header.Add(CreateSecurityHeader());

            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(soap + "Envelope",
                    new XAttribute(XNamespace.Xmlns + "s", soap),
                    new XAttribute(XNamespace.Xmlns + "wsa", addressing),
                    header,
                    new XElement(soap + "Body", body)));
        }

        private XElement CreateSecurityHeader()
        {
            XNamespace security = OnvifNamespaces.WsSecurity;
            XNamespace utility = OnvifNamespaces.WsUtility;
            var created = DateTimeOffset.UtcNow.Add(ClockOffset).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            var nonce = RandomNumberGenerator.GetBytes(20);
            var passwordType = _options.UsePasswordDigest ? PasswordDigestType : PasswordTextType;
            var password = _options.UsePasswordDigest ? CreatePasswordDigest(nonce, created, _options.Password) : _options.Password;

            return new XElement(security + "Security",
                new XAttribute(XName.Get("mustUnderstand", OnvifNamespaces.Soap), "1"),
                new XElement(security + "UsernameToken",
                    new XElement(security + "Username", _options.UserName),
                    new XElement(security + "Password", new XAttribute("Type", passwordType), password),
                    new XElement(security + "Nonce", new XAttribute("EncodingType", Base64EncodingType), Convert.ToBase64String(nonce)),
                    new XElement(utility + "Created", created)));
        }

        private static string CreatePasswordDigest(byte[] nonce, string created, string password)
        {
            var createdBytes = Encoding.UTF8.GetBytes(created);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var source = new byte[nonce.Length + createdBytes.Length + passwordBytes.Length];
            Buffer.BlockCopy(nonce, 0, source, 0, nonce.Length);
            Buffer.BlockCopy(createdBytes, 0, source, nonce.Length, createdBytes.Length);
            Buffer.BlockCopy(passwordBytes, 0, source, nonce.Length + createdBytes.Length, passwordBytes.Length);
            return Convert.ToBase64String(SHA1.HashData(source));
        }

        private static OnvifSoapFaultException CreateFault(HttpStatusCode statusCode, XElement fault, string responseXml)
        {
            XNamespace soap = OnvifNamespaces.Soap;
            var code = fault.Element(soap + "Code")?.Element(soap + "Value")?.Value;
            var reason = fault.Element(soap + "Reason")?.Elements(soap + "Text").FirstOrDefault()?.Value;
            return new OnvifSoapFaultException(reason ?? "The ONVIF device returned a SOAP fault.", statusCode, code, responseXml);
        }

        public ValueTask DisposeAsync()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
