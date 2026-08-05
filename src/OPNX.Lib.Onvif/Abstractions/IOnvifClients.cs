using OPNX.Lib.Onvif.Models;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Abstractions
{
    public interface IOnvifSoapTransport : IAsyncDisposable
    {
        TimeSpan ClockOffset { get; set; }
        Task<XDocument> SendAsync(Uri endpoint, string action, XElement body, CancellationToken cancellationToken = default);
    }

    public interface IOnvifDeviceClient
    {
        Task<DateTimeOffset?> GetSystemDateAndTimeAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<OnvifService>> GetServicesAsync(CancellationToken cancellationToken = default);
    }

    public interface IOnvifMediaClient
    {
        Task<IReadOnlyList<OnvifProfile>> GetProfilesAsync(CancellationToken cancellationToken = default);
        Task<Uri?> GetStreamUriAsync(string profileToken, CancellationToken cancellationToken = default);
    }

    public interface IOnvifPtzClient
    {
        Task<OnvifPtzOptions> GetOptionsAsync(string configurationToken, CancellationToken cancellationToken = default);
        Task<OnvifPtzStatus> GetStatusAsync(string profileToken, CancellationToken cancellationToken = default);
        Task ContinuousMoveAsync(string profileToken, float pan, float tilt, float zoom, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
        Task RelativeMoveAsync(string profileToken, OnvifVector translation, OnvifVector? speed = null, CancellationToken cancellationToken = default);
        Task AbsoluteMoveAsync(string profileToken, OnvifVector position, OnvifVector? speed = null, CancellationToken cancellationToken = default);
        Task StopAsync(string profileToken, bool panTilt = true, bool zoom = true, CancellationToken cancellationToken = default);
        Task SetHomePositionAsync(string profileToken, CancellationToken cancellationToken = default);
        Task GotoHomePositionAsync(string profileToken, OnvifVector? speed = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<OnvifPreset>> GetPresetsAsync(string profileToken, CancellationToken cancellationToken = default);
        Task<string?> SetPresetAsync(string profileToken, string presetName, string? presetToken = null, CancellationToken cancellationToken = default);
        Task GotoPresetAsync(string profileToken, string presetToken, float? speed = null, CancellationToken cancellationToken = default);
        Task RemovePresetAsync(string profileToken, string presetToken, CancellationToken cancellationToken = default);
    }

    public interface IOnvifImagingClient
    {
        Task<OnvifImagingOptions> GetOptionsAsync(string videoSourceToken, CancellationToken cancellationToken = default);
        Task<OnvifImagingSettings> GetSettingsAsync(string videoSourceToken, CancellationToken cancellationToken = default);
        Task SetFocusModeAsync(string videoSourceToken, OnvifAutoMode mode, CancellationToken cancellationToken = default);
        Task MoveFocusAsync(string videoSourceToken, float speed, CancellationToken cancellationToken = default);
        Task MoveFocusAbsoluteAsync(string videoSourceToken, float position, float? speed = null, CancellationToken cancellationToken = default);
        Task MoveFocusRelativeAsync(string videoSourceToken, float distance, float? speed = null, CancellationToken cancellationToken = default);
        Task StopFocusAsync(string videoSourceToken, CancellationToken cancellationToken = default);
        Task SetIrisModeAsync(string videoSourceToken, OnvifAutoMode mode, CancellationToken cancellationToken = default);
        Task SetIrisAsync(string videoSourceToken, float iris, CancellationToken cancellationToken = default);
    }

    public interface IOnvifDeviceIoClient
    {
        Task<IReadOnlyList<OnvifDigitalInput>> GetDigitalInputsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<OnvifRelayOutput>> GetRelayOutputsAsync(CancellationToken cancellationToken = default);
        Task SetRelayOutputSettingsAsync(string relayToken, OnvifRelaySettings settings, CancellationToken cancellationToken = default);
        Task SetRelayOutputStateAsync(string relayToken, OnvifRelayLogicalState state, CancellationToken cancellationToken = default);
    }

    public interface IOnvifEventSubscription : IAsyncDisposable
    {
        Uri SubscriptionUri { get; }
        DateTimeOffset? TerminationTime { get; }
        Task<IReadOnlyList<OnvifNotification>> PullMessagesAsync(TimeSpan timeout, int messageLimit = 100, CancellationToken cancellationToken = default);
        Task RenewAsync(TimeSpan duration, CancellationToken cancellationToken = default);
        Task UnsubscribeAsync(CancellationToken cancellationToken = default);
    }

    public interface IOnvifEventClient
    {
        Task<IOnvifEventSubscription> CreatePullPointSubscriptionAsync(TimeSpan initialTerminationTime, CancellationToken cancellationToken = default);
    }

    public interface IOnvifClient : IAsyncDisposable
    {
        OnvifCapabilities Capabilities { get; }
        IOnvifDeviceClient Device { get; }
        IOnvifMediaClient? Media { get; }
        IOnvifPtzClient? Ptz { get; }
        IOnvifImagingClient? Imaging { get; }
        IOnvifDeviceIoClient? DeviceIo { get; }
        IOnvifEventClient? Events { get; }
        Task InitializeAsync(CancellationToken cancellationToken = default);
    }
}
