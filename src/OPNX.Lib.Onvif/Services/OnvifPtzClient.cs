using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Internal;
using OPNX.Lib.Onvif.Models;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Services
{
    public sealed class OnvifPtzClient(IOnvifSoapTransport transport, Uri endpoint) : OnvifServiceClient(transport, endpoint), IOnvifPtzClient
    {
        public async Task<OnvifPtzOptions> GetOptionsAsync(string configurationToken, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            var document = await SendAsync($"{OnvifNamespaces.Ptz}/GetConfigurationOptions",
                new XElement(ptz + "GetConfigurationOptions", new XElement(ptz + "ConfigurationToken", configurationToken)), cancellationToken).ConfigureAwait(false);
            var panTilt = document.Descendant("ContinuousPanTiltVelocitySpace");
            var zoom = document.Descendant("ContinuousZoomVelocitySpace");
            var relativePanTilt = document.Descendant("RelativePanTiltTranslationSpace");
            var relativeZoom = document.Descendant("RelativeZoomTranslationSpace");
            var absolutePanTilt = document.Descendant("AbsolutePanTiltPositionSpace");
            var absoluteZoom = document.Descendant("AbsoluteZoomPositionSpace");
            return new(
                ParseRange(panTilt?.Descendant("XRange")),
                ParseRange(panTilt?.Descendant("YRange")),
                ParseRange(zoom?.Descendant("XRange")),
                ParseRange(relativePanTilt?.Descendant("XRange")),
                ParseRange(relativePanTilt?.Descendant("YRange")),
                ParseRange(relativeZoom?.Descendant("XRange")),
                ParseRange(absolutePanTilt?.Descendant("XRange")),
                ParseRange(absolutePanTilt?.Descendant("YRange")),
                ParseRange(absoluteZoom?.Descendant("XRange")));
        }

        public async Task<OnvifPtzStatus> GetStatusAsync(string profileToken, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            var document = await SendAsync($"{OnvifNamespaces.Ptz}/GetStatus",
                new XElement(ptz + "GetStatus", new XElement(ptz + "ProfileToken", profileToken)), cancellationToken).ConfigureAwait(false);
            var status = document.Descendant("PTZStatus");
            return new(
                ParseVector(status?.Descendant("Position")),
                status?.Descendant("MoveStatus")?.Descendant("PanTilt")?.Value,
                status?.Descendant("MoveStatus")?.Descendant("Zoom")?.Value,
                status?.Descendant("Error")?.Value);
        }

        public Task ContinuousMoveAsync(string profileToken, float pan, float tilt, float zoom, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            var body = new XElement(ptz + "ContinuousMove",
                new XElement(ptz + "ProfileToken", profileToken),
                CreateVector(ptz + "Velocity", new OnvifVector(pan, tilt, zoom)));
            if (timeout.HasValue)
                body.Add(new XElement(ptz + "Timeout", OnvifXml.Duration(timeout.Value)));
            return SendNoResultAsync($"{OnvifNamespaces.Ptz}/ContinuousMove",
                body, cancellationToken);
        }

        public Task RelativeMoveAsync(string profileToken, OnvifVector translation, OnvifVector? speed = null, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            var body = new XElement(ptz + "RelativeMove",
                new XElement(ptz + "ProfileToken", profileToken),
                CreateVector(ptz + "Translation", translation));
            if (speed != null)
                body.Add(CreateVector(ptz + "Speed", speed));
            return SendNoResultAsync($"{OnvifNamespaces.Ptz}/RelativeMove", body, cancellationToken);
        }

        public Task AbsoluteMoveAsync(string profileToken, OnvifVector position, OnvifVector? speed = null, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            var body = new XElement(ptz + "AbsoluteMove",
                new XElement(ptz + "ProfileToken", profileToken),
                CreateVector(ptz + "Position", position));
            if (speed != null)
                body.Add(CreateVector(ptz + "Speed", speed));
            return SendNoResultAsync($"{OnvifNamespaces.Ptz}/AbsoluteMove", body, cancellationToken);
        }

        public Task StopAsync(string profileToken, bool panTilt = true, bool zoom = true, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            return SendNoResultAsync($"{OnvifNamespaces.Ptz}/Stop",
                new XElement(ptz + "Stop", new XElement(ptz + "ProfileToken", profileToken), new XElement(ptz + "PanTilt", panTilt), new XElement(ptz + "Zoom", zoom)), cancellationToken);
        }

        public Task SetHomePositionAsync(string profileToken, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            return SendNoResultAsync($"{OnvifNamespaces.Ptz}/SetHomePosition",
                new XElement(ptz + "SetHomePosition", new XElement(ptz + "ProfileToken", profileToken)), cancellationToken);
        }

        public Task GotoHomePositionAsync(string profileToken, OnvifVector? speed = null, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            var body = new XElement(ptz + "GotoHomePosition", new XElement(ptz + "ProfileToken", profileToken));
            if (speed != null)
                body.Add(CreateVector(ptz + "Speed", speed));
            return SendNoResultAsync($"{OnvifNamespaces.Ptz}/GotoHomePosition", body, cancellationToken);
        }

        public async Task<IReadOnlyList<OnvifPreset>> GetPresetsAsync(string profileToken, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            var document = await SendAsync($"{OnvifNamespaces.Ptz}/GetPresets",
                new XElement(ptz + "GetPresets", new XElement(ptz + "ProfileToken", profileToken)), cancellationToken).ConfigureAwait(false);
            return document.DescendantsNamed("Preset").Select(x => new OnvifPreset(
                x.AttributeValue("token") ?? string.Empty,
                x.Descendant("Name")?.Value,
                ParseVector(x.Descendant("PTZPosition")))).Where(x => !string.IsNullOrEmpty(x.Token)).ToList();
        }

        public async Task<string?> SetPresetAsync(string profileToken, string presetName, string? presetToken = null, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            var body = new XElement(ptz + "SetPreset", new XElement(ptz + "ProfileToken", profileToken), new XElement(ptz + "PresetName", presetName));
            if (!string.IsNullOrWhiteSpace(presetToken))
                body.Add(new XElement(ptz + "PresetToken", presetToken));
            var document = await SendAsync($"{OnvifNamespaces.Ptz}/SetPreset", body, cancellationToken).ConfigureAwait(false);
            return document.Descendant("PresetToken")?.Value;
        }

        public Task GotoPresetAsync(string profileToken, string presetToken, float? speed = null, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            XNamespace schema = OnvifNamespaces.Schema;
            var body = new XElement(ptz + "GotoPreset", new XElement(ptz + "ProfileToken", profileToken), new XElement(ptz + "PresetToken", presetToken));
            if (speed.HasValue)
                body.Add(new XElement(ptz + "Speed", new XElement(schema + "PanTilt", new XAttribute("x", OnvifXml.Number(speed.Value)), new XAttribute("y", OnvifXml.Number(speed.Value))), new XElement(schema + "Zoom", new XAttribute("x", OnvifXml.Number(speed.Value)))));
            return SendNoResultAsync($"{OnvifNamespaces.Ptz}/GotoPreset", body, cancellationToken);
        }

        public Task RemovePresetAsync(string profileToken, string presetToken, CancellationToken cancellationToken = default)
        {
            XNamespace ptz = OnvifNamespaces.Ptz;
            return SendNoResultAsync($"{OnvifNamespaces.Ptz}/RemovePreset",
                new XElement(ptz + "RemovePreset", new XElement(ptz + "ProfileToken", profileToken), new XElement(ptz + "PresetToken", presetToken)), cancellationToken);
        }

        private async Task SendNoResultAsync(string action, XElement body, CancellationToken cancellationToken) =>
            _ = await SendAsync(action, body, cancellationToken).ConfigureAwait(false);

        private static OnvifFloatRange? ParseRange(XElement? element)
        {
            var minimum = element?.Descendant("Min").FloatValue();
            var maximum = element?.Descendant("Max").FloatValue();
            return minimum.HasValue && maximum.HasValue ? new(minimum.Value, maximum.Value) : null;
        }

        private static OnvifVector? ParseVector(XElement? element)
        {
            if (element is null)
                return null;
            var panTilt = element.Descendant("PanTilt");
            var zoom = element.Descendant("Zoom");
            return new(ParseAttribute(panTilt, "x"), ParseAttribute(panTilt, "y"), ParseAttribute(zoom, "x"));
        }

        private static float? ParseAttribute(XElement? element, string name) =>
            float.TryParse(element?.AttributeValue(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

        private static XElement CreateVector(XName name, OnvifVector vector)
        {
            XNamespace schema = OnvifNamespaces.Schema;
            var element = new XElement(name);
            if (vector.Pan.HasValue || vector.Tilt.HasValue)
                element.Add(new XElement(schema + "PanTilt",
                    new XAttribute("x", OnvifXml.Number(vector.Pan ?? 0)),
                    new XAttribute("y", OnvifXml.Number(vector.Tilt ?? 0))));
            if (vector.Zoom.HasValue)
                element.Add(new XElement(schema + "Zoom", new XAttribute("x", OnvifXml.Number(vector.Zoom.Value))));
            return element;
        }
    }
}
