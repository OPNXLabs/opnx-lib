using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Policies;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Models
{
    public sealed class OnvifClientOptions
    {
        public required Uri DeviceServiceUri { get; init; }
        public string UserName { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
        public bool UsePasswordDigest { get; init; } = true;
        public IOnvifDevicePolicy DevicePolicy { get; init; } = DefaultOnvifDevicePolicy.Instance;
    }

    public enum OnvifOperation
    {
        Unknown,
        GetSystemDateAndTime,
        GetServices,
        GetProfiles,
        GetStreamUri,
        GetOptions,
        GetStatus,
        ContinuousMove,
        RelativeMove,
        AbsoluteMove,
        Stop,
        SetHomePosition,
        GotoHomePosition,
        GetPresets,
        SetPreset,
        GotoPreset,
        RemovePreset,
        MoveFocus,
        GetImagingSettings,
        SetImagingSettings,
        GetDigitalInputs,
        GetRelayOutputs,
        SetRelayOutputSettings,
        SetRelayOutputState,
        CreatePullPointSubscription,
        PullMessages,
        Renew,
        Unsubscribe
    }

    public sealed record OnvifService(string Namespace, Uri Uri, string? Version = null);

    public sealed class OnvifCapabilities
    {
        private readonly Dictionary<string, OnvifService> _services = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, OnvifService> Services => _services;
        public Uri? MediaUri => Find(OnvifNamespaces.Media);
        public Uri? PtzUri => Find(OnvifNamespaces.Ptz);
        public Uri? ImagingUri => Find(OnvifNamespaces.Imaging);
        public Uri? DeviceIoUri => Find(OnvifNamespaces.DeviceIo);
        public Uri? EventsUri => Find(OnvifNamespaces.Events);

        public void Set(OnvifService service) => _services[service.Namespace] = service;
        public Uri? Find(string serviceNamespace) => _services.TryGetValue(serviceNamespace, out var service) ? service.Uri : null;
    }

    public sealed record OnvifProfile(string Token, string? Name, string? VideoSourceToken, string? PtzConfigurationToken);
    public sealed record OnvifPreset(string Token, string? Name, OnvifVector? Position);
    public sealed record OnvifVector(float? Pan, float? Tilt, float? Zoom);
    public sealed record OnvifFloatRange(float Minimum, float Maximum);
    public sealed record OnvifPtzOptions(
        OnvifFloatRange? Pan,
        OnvifFloatRange? Tilt,
        OnvifFloatRange? Zoom,
        OnvifFloatRange? RelativePan = null,
        OnvifFloatRange? RelativeTilt = null,
        OnvifFloatRange? RelativeZoom = null,
        OnvifFloatRange? AbsolutePan = null,
        OnvifFloatRange? AbsoluteTilt = null,
        OnvifFloatRange? AbsoluteZoom = null);
    public sealed record OnvifPtzStatus(OnvifVector? Position, string? PanTiltMoveStatus, string? ZoomMoveStatus, string? Error);
    public sealed record OnvifImagingOptions(
        OnvifFloatRange? FocusSpeed,
        OnvifFloatRange? Iris,
        OnvifFloatRange? FocusPosition = null,
        OnvifFloatRange? FocusDistance = null,
        bool SupportsAutoFocus = false,
        bool SupportsAutoIris = false);
    public sealed record OnvifImagingSettings(string? FocusMode, float? FocusPosition, string? ExposureMode, float? Iris);

    public enum OnvifAutoMode
    {
        Auto,
        Manual
    }

    public enum OnvifRelayLogicalState
    {
        Inactive,
        Active
    }

    public enum OnvifRelayMode
    {
        Monostable,
        Bistable
    }

    public enum OnvifRelayIdleState
    {
        Open,
        Closed
    }

    public sealed record OnvifDigitalInput(string Token);
    public sealed record OnvifRelaySettings(OnvifRelayMode Mode, TimeSpan? DelayTime, OnvifRelayIdleState IdleState);
    public sealed record OnvifRelayOutput(string Token, OnvifRelayMode? Mode, TimeSpan? DelayTime, OnvifRelayIdleState? IdleState = null);
    public sealed record OnvifNotification(
        string? Topic,
        DateTimeOffset? UtcTime,
        IReadOnlyDictionary<string, string> Sources,
        IReadOnlyDictionary<string, string> Data,
        XElement Raw);
}
