using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Models;

namespace OPNX.Lib.Onvif.Testing
{
    public sealed class SimulatedOnvifCamera : IOnvifPtzClient, IOnvifImagingClient, IOnvifDeviceIoClient
    {
        private readonly Dictionary<string, OnvifPreset> _presets = [];
        private readonly Dictionary<string, OnvifRelayLogicalState> _relays = [];

        public OnvifVector Position { get; private set; } = new(0, 0, 0);
        public float FocusSpeed { get; private set; }
        public float Iris { get; private set; }
        public OnvifPtzOptions PtzOptions { get; init; } = new(new(-1, 1), new(-1, 1), new(-1, 1));
        public OnvifImagingOptions ImagingOptions { get; init; } = new(new(-1, 1), new(0, 20));

        Task<OnvifPtzOptions> IOnvifPtzClient.GetOptionsAsync(string configurationToken, CancellationToken cancellationToken) =>
            Task.FromResult(PtzOptions);

        public Task<OnvifPtzStatus> GetStatusAsync(string profileToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OnvifPtzStatus(Position, "IDLE", "IDLE", null));

        public Task ContinuousMoveAsync(string profileToken, float pan, float tilt, float zoom, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Position = new(Clamp(pan, PtzOptions.Pan), Clamp(tilt, PtzOptions.Tilt), Clamp(zoom, PtzOptions.Zoom));
            return Task.CompletedTask;
        }

        public Task RelativeMoveAsync(string profileToken, OnvifVector translation, OnvifVector? speed = null, CancellationToken cancellationToken = default)
        {
            Position = new((Position.Pan ?? 0) + (translation.Pan ?? 0), (Position.Tilt ?? 0) + (translation.Tilt ?? 0), (Position.Zoom ?? 0) + (translation.Zoom ?? 0));
            return Task.CompletedTask;
        }

        public Task AbsoluteMoveAsync(string profileToken, OnvifVector position, OnvifVector? speed = null, CancellationToken cancellationToken = default)
        {
            Position = position;
            return Task.CompletedTask;
        }

        public Task StopAsync(string profileToken, bool panTilt = true, bool zoom = true, CancellationToken cancellationToken = default)
        {
            Position = new(panTilt ? 0 : Position.Pan, panTilt ? 0 : Position.Tilt, zoom ? 0 : Position.Zoom);
            return Task.CompletedTask;
        }

        public Task SetHomePositionAsync(string profileToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task GotoHomePositionAsync(string profileToken, OnvifVector? speed = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OnvifPreset>> GetPresetsAsync(string profileToken, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OnvifPreset>>(_presets.Values.ToList());

        public Task<string?> SetPresetAsync(string profileToken, string presetName, string? presetToken = null, CancellationToken cancellationToken = default)
        {
            var token = string.IsNullOrWhiteSpace(presetToken) ? Guid.NewGuid().ToString("N") : presetToken;
            _presets[token] = new(token, presetName, Position);
            return Task.FromResult<string?>(token);
        }

        public Task GotoPresetAsync(string profileToken, string presetToken, float? speed = null, CancellationToken cancellationToken = default)
        {
            if (!_presets.TryGetValue(presetToken, out var preset))
                throw new KeyNotFoundException($"Preset token '{presetToken}' was not found.");
            Position = preset.Position ?? Position;
            return Task.CompletedTask;
        }

        public Task RemovePresetAsync(string profileToken, string presetToken, CancellationToken cancellationToken = default)
        {
            _presets.Remove(presetToken);
            return Task.CompletedTask;
        }

        Task<OnvifImagingOptions> IOnvifImagingClient.GetOptionsAsync(string videoSourceToken, CancellationToken cancellationToken) =>
            Task.FromResult(ImagingOptions);

        public Task<OnvifImagingSettings> GetSettingsAsync(string videoSourceToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OnvifImagingSettings("MANUAL", null, "MANUAL", Iris));

        public Task SetFocusModeAsync(string videoSourceToken, OnvifAutoMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task MoveFocusAsync(string videoSourceToken, float speed, CancellationToken cancellationToken = default)
        {
            FocusSpeed = Clamp(speed, ImagingOptions.FocusSpeed) ?? 0;
            return Task.CompletedTask;
        }

        public Task MoveFocusAbsoluteAsync(string videoSourceToken, float position, float? speed = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MoveFocusRelativeAsync(string videoSourceToken, float distance, float? speed = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopFocusAsync(string videoSourceToken, CancellationToken cancellationToken = default)
        {
            FocusSpeed = 0;
            return Task.CompletedTask;
        }

        public Task SetIrisAsync(string videoSourceToken, float iris, CancellationToken cancellationToken = default)
        {
            Iris = Clamp(iris, ImagingOptions.Iris) ?? iris;
            return Task.CompletedTask;
        }

        public Task SetIrisModeAsync(string videoSourceToken, OnvifAutoMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OnvifDigitalInput>> GetDigitalInputsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OnvifDigitalInput>>([]);

        public Task<IReadOnlyList<OnvifRelayOutput>> GetRelayOutputsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OnvifRelayOutput>>(_relays.Keys.Select(x => new OnvifRelayOutput(x, OnvifRelayMode.Bistable, null)).ToList());

        public Task SetRelayOutputStateAsync(string relayToken, OnvifRelayLogicalState state, CancellationToken cancellationToken = default)
        {
            _relays[relayToken] = state;
            return Task.CompletedTask;
        }

        public Task SetRelayOutputSettingsAsync(string relayToken, OnvifRelaySettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public OnvifRelayLogicalState? GetRelayState(string relayToken) => _relays.TryGetValue(relayToken, out var state) ? state : null;
        private static float? Clamp(float value, OnvifFloatRange? range) => range is null ? value : Math.Clamp(value, range.Minimum, range.Maximum);
    }
}
