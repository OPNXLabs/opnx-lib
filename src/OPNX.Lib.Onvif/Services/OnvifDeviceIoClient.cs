using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Internal;
using OPNX.Lib.Onvif.Models;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Services
{
    public sealed class OnvifDeviceIoClient(IOnvifSoapTransport transport, Uri endpoint) : OnvifServiceClient(transport, endpoint), IOnvifDeviceIoClient
    {
        public async Task<IReadOnlyList<OnvifDigitalInput>> GetDigitalInputsAsync(CancellationToken cancellationToken = default)
        {
            XNamespace deviceIo = OnvifNamespaces.DeviceIo;
            var document = await SendAsync($"{OnvifNamespaces.DeviceIo}/GetDigitalInputs",
                new XElement(deviceIo + "GetDigitalInputs"), cancellationToken).ConfigureAwait(false);
            return document.DescendantsNamed("DigitalInputs")
                .Select(input => new OnvifDigitalInput(input.AttributeValue("token") ?? string.Empty))
                .Where(input => !string.IsNullOrWhiteSpace(input.Token))
                .ToList();
        }

        public async Task<IReadOnlyList<OnvifRelayOutput>> GetRelayOutputsAsync(CancellationToken cancellationToken = default)
        {
            XNamespace deviceIo = OnvifNamespaces.DeviceIo;
            var document = await SendAsync($"{OnvifNamespaces.DeviceIo}/GetRelayOutputs", new XElement(deviceIo + "GetRelayOutputs"), cancellationToken).ConfigureAwait(false);
            return document.DescendantsNamed("RelayOutputs").Select(x => new OnvifRelayOutput(
                x.AttributeValue("token") ?? string.Empty,
                Enum.TryParse<OnvifRelayMode>(x.Descendant("Mode")?.Value, true, out var mode) ? mode : null,
                x.Descendant("DelayTime").DurationValue(),
                Enum.TryParse<OnvifRelayIdleState>(x.Descendant("IdleState")?.Value, true, out var idleState) ? idleState : null))
                .Where(x => !string.IsNullOrEmpty(x.Token)).ToList();
        }

        public async Task SetRelayOutputSettingsAsync(string relayToken, OnvifRelaySettings settings, CancellationToken cancellationToken = default)
        {
            XNamespace deviceIo = OnvifNamespaces.DeviceIo;
            XNamespace schema = OnvifNamespaces.Schema;
            var properties = new XElement(schema + "Properties",
                new XElement(schema + "Mode", settings.Mode),
                new XElement(schema + "DelayTime", OnvifXml.Duration(settings.DelayTime ?? TimeSpan.Zero)),
                new XElement(schema + "IdleState", settings.IdleState.ToString().ToLowerInvariant()));
            _ = await SendAsync($"{OnvifNamespaces.DeviceIo}/SetRelayOutputSettings",
                new XElement(deviceIo + "SetRelayOutputSettings",
                    new XElement(deviceIo + "RelayOutput", new XAttribute("token", relayToken), properties),
                    new XElement(deviceIo + "ForcePersistence", true)), cancellationToken).ConfigureAwait(false);
        }

        public async Task SetRelayOutputStateAsync(string relayToken, OnvifRelayLogicalState state, CancellationToken cancellationToken = default)
        {
            XNamespace deviceIo = OnvifNamespaces.DeviceIo;
            _ = await SendAsync($"{OnvifNamespaces.DeviceIo}/SetRelayOutputState",
                new XElement(deviceIo + "SetRelayOutputState",
                    new XElement(deviceIo + "RelayOutputToken", relayToken),
                    new XElement(deviceIo + "LogicalState", state.ToString().ToLowerInvariant())), cancellationToken).ConfigureAwait(false);
        }
    }
}
