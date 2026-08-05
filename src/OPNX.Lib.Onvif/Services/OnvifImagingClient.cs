using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Internal;
using OPNX.Lib.Onvif.Models;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Services
{
    public sealed class OnvifImagingClient(IOnvifSoapTransport transport, Uri endpoint) : OnvifServiceClient(transport, endpoint), IOnvifImagingClient
    {
        public async Task<OnvifImagingOptions> GetOptionsAsync(string videoSourceToken, CancellationToken cancellationToken = default)
        {
            XNamespace imaging = OnvifNamespaces.Imaging;
            var document = await SendAsync($"{OnvifNamespaces.Imaging}/GetOptions",
                new XElement(imaging + "GetOptions", new XElement(imaging + "VideoSourceToken", videoSourceToken)), cancellationToken).ConfigureAwait(false);
            var focus = document.Descendant("Focus");
            var exposure = document.Descendant("Exposure");
            OnvifFloatRange? continuousSpeed = ParseRange(focus?.Descendant("Speed"));
            OnvifFloatRange? absolutePosition = null;
            OnvifFloatRange? relativeDistance = null;
            try
            {
                var moveOptions = await SendAsync($"{OnvifNamespaces.Imaging}/GetMoveOptions",
                    new XElement(imaging + "GetMoveOptions", new XElement(imaging + "VideoSourceToken", videoSourceToken)), cancellationToken).ConfigureAwait(false);
                continuousSpeed = ParseRange(moveOptions.Descendant("Continuous")?.Descendant("Speed")) ?? continuousSpeed;
                absolutePosition = ParseRange(moveOptions.Descendant("Absolute")?.Descendant("Position"));
                relativeDistance = ParseRange(moveOptions.Descendant("Relative")?.Descendant("Distance"));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }

            var supportsAutoFocus = focus?.Descendants().Any(element =>
                element.Name.LocalName.Contains("AutoFocus", StringComparison.OrdinalIgnoreCase) &&
                element.Value.Equals("AUTO", StringComparison.OrdinalIgnoreCase)) == true;
            var supportsAutoIris = exposure?.Descendants().Any(element =>
                element.Name.LocalName == "Mode" && element.Value.Equals("AUTO", StringComparison.OrdinalIgnoreCase)) == true;
            return new(continuousSpeed, ParseRange(exposure?.Descendant("Iris")), absolutePosition, relativeDistance, supportsAutoFocus, supportsAutoIris);
        }

        public async Task<OnvifImagingSettings> GetSettingsAsync(string videoSourceToken, CancellationToken cancellationToken = default)
        {
            XNamespace imaging = OnvifNamespaces.Imaging;
            var document = await SendAsync($"{OnvifNamespaces.Imaging}/GetImagingSettings",
                new XElement(imaging + "GetImagingSettings", new XElement(imaging + "VideoSourceToken", videoSourceToken)), cancellationToken).ConfigureAwait(false);
            var settings = document.Descendant("ImagingSettings");
            var focus = settings?.Descendant("Focus");
            var exposure = settings?.Descendant("Exposure");
            float? focusPosition = null;
            try
            {
                var status = await SendAsync($"{OnvifNamespaces.Imaging}/GetStatus",
                    new XElement(imaging + "GetStatus", new XElement(imaging + "VideoSourceToken", videoSourceToken)), cancellationToken).ConfigureAwait(false);
                focusPosition = status.Descendant("FocusStatus20")?.Descendant("Position")?.FloatValue();
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
            return new(
                focus?.Descendant("AutoFocusMode")?.Value,
                focusPosition,
                exposure?.Descendant("Mode")?.Value,
                exposure?.Descendant("Iris")?.FloatValue());
        }

        public Task SetFocusModeAsync(string videoSourceToken, OnvifAutoMode mode, CancellationToken cancellationToken = default)
        {
            XNamespace schema = OnvifNamespaces.Schema;
            return SetImagingSettingsAsync(videoSourceToken,
                new XElement(schema + "Focus", new XElement(schema + "AutoFocusMode", Mode(mode))), cancellationToken);
        }

        public Task MoveFocusAsync(string videoSourceToken, float speed, CancellationToken cancellationToken = default)
        {
            XNamespace imaging = OnvifNamespaces.Imaging;
            XNamespace schema = OnvifNamespaces.Schema;
            return SendNoResultAsync($"{OnvifNamespaces.Imaging}/Move",
                new XElement(imaging + "Move",
                    new XElement(imaging + "VideoSourceToken", videoSourceToken),
                    new XElement(imaging + "Focus", new XElement(schema + "Continuous", new XElement(schema + "Speed", OnvifXml.Number(speed))))), cancellationToken);
        }

        public Task MoveFocusAbsoluteAsync(string videoSourceToken, float position, float? speed = null, CancellationToken cancellationToken = default) =>
            MoveFocusAsync(videoSourceToken, "Absolute", "Position", position, speed, cancellationToken);

        public Task MoveFocusRelativeAsync(string videoSourceToken, float distance, float? speed = null, CancellationToken cancellationToken = default) =>
            MoveFocusAsync(videoSourceToken, "Relative", "Distance", distance, speed, cancellationToken);

        public Task StopFocusAsync(string videoSourceToken, CancellationToken cancellationToken = default)
        {
            XNamespace imaging = OnvifNamespaces.Imaging;
            return SendNoResultAsync($"{OnvifNamespaces.Imaging}/Stop",
                new XElement(imaging + "Stop", new XElement(imaging + "VideoSourceToken", videoSourceToken)), cancellationToken);
        }

        public Task SetIrisAsync(string videoSourceToken, float iris, CancellationToken cancellationToken = default)
        {
            XNamespace schema = OnvifNamespaces.Schema;
            return SetImagingSettingsAsync(videoSourceToken,
                new XElement(schema + "Exposure", new XElement(schema + "Mode", "MANUAL"), new XElement(schema + "Iris", OnvifXml.Number(iris))), cancellationToken);
        }

        public Task SetIrisModeAsync(string videoSourceToken, OnvifAutoMode mode, CancellationToken cancellationToken = default)
        {
            XNamespace schema = OnvifNamespaces.Schema;
            return SetImagingSettingsAsync(videoSourceToken,
                new XElement(schema + "Exposure", new XElement(schema + "Mode", Mode(mode))), cancellationToken);
        }

        private Task MoveFocusAsync(string videoSourceToken, string moveType, string valueName, float value, float? speed, CancellationToken cancellationToken)
        {
            XNamespace imaging = OnvifNamespaces.Imaging;
            XNamespace schema = OnvifNamespaces.Schema;
            var move = new XElement(schema + moveType, new XElement(schema + valueName, OnvifXml.Number(value)));
            if (speed.HasValue)
                move.Add(new XElement(schema + "Speed", OnvifXml.Number(speed.Value)));
            return SendNoResultAsync($"{OnvifNamespaces.Imaging}/Move",
                new XElement(imaging + "Move",
                    new XElement(imaging + "VideoSourceToken", videoSourceToken),
                    new XElement(imaging + "Focus", move)), cancellationToken);
        }

        private Task SetImagingSettingsAsync(string videoSourceToken, XElement settings, CancellationToken cancellationToken)
        {
            XNamespace imaging = OnvifNamespaces.Imaging;
            return SendNoResultAsync($"{OnvifNamespaces.Imaging}/SetImagingSettings",
                new XElement(imaging + "SetImagingSettings",
                    new XElement(imaging + "VideoSourceToken", videoSourceToken),
                    new XElement(imaging + "ImagingSettings", settings),
                    new XElement(imaging + "ForcePersistence", true)), cancellationToken);
        }

        private static string Mode(OnvifAutoMode mode) => mode == OnvifAutoMode.Auto ? "AUTO" : "MANUAL";

        private async Task SendNoResultAsync(string action, XElement body, CancellationToken cancellationToken) =>
            _ = await SendAsync(action, body, cancellationToken).ConfigureAwait(false);

        private static OnvifFloatRange? ParseRange(XElement? element)
        {
            var minimum = element?.Descendant("Min").FloatValue();
            var maximum = element?.Descendant("Max").FloatValue();
            return minimum.HasValue && maximum.HasValue ? new(minimum.Value, maximum.Value) : null;
        }
    }
}
