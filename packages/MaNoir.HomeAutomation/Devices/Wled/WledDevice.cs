using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Wled;

public sealed class WledDevice : IDevice
{
    private readonly RuntimeDevice _runtimeDevice;
    private readonly List<WledSegment> _segments = [];
    private readonly Func<int, bool?, decimal?, DeviceColor, CancellationToken, Task> _setStateAsync;
    private readonly Func<int, LightAnimationRequest, CancellationToken, Task> _setAnimationAsync;
    private readonly IReadOnlyList<LightAnimationDefinition> _supportedAnimations;
    private readonly WledPowerCapability _power;

    private WledDevice(
        string id,
        Func<int, bool?, decimal?, DeviceColor, CancellationToken, Task> setStateAsync,
        Func<int, LightAnimationRequest, CancellationToken, Task> setAnimationAsync,
        IReadOnlyList<LightAnimationDefinition> supportedAnimations,
        Func<bool, CancellationToken, Task> setPowerAsync)
    {
        Id = id;
        _setStateAsync = setStateAsync;
        _setAnimationAsync = setAnimationAsync;
        _supportedAnimations = supportedAnimations ?? [];
        _power = setPowerAsync == null ? null : new WledPowerCapability(setPowerAsync);
        _runtimeDevice = new RuntimeDevice(id, [], _power == null ? [] : [_power]);
    }

    public string Id { get; }

    public string InternalId => string.Concat("wled:", Id.Trim().ToLowerInvariant());

    public IReadOnlyList<IDeviceCapability> Capabilities => _runtimeDevice.Capabilities;

    public IReadOnlyList<IDeviceElement> Elements => _runtimeDevice.Elements;

    public static WledDevice Create(
        string id,
        Func<bool?, decimal?, DeviceColor, CancellationToken, Task> setStateAsync)
    {
        return Create(
            id,
            (segmentId, isOn, intensity, color, cancellationToken) => setStateAsync(isOn, intensity, color, cancellationToken),
            null,
            null,
            null);
    }

    public static WledDevice Create(
        string id,
        Func<bool?, decimal?, DeviceColor, CancellationToken, Task> setStateAsync,
        Func<LightAnimationRequest, CancellationToken, Task> setAnimationAsync,
        IReadOnlyList<LightAnimationDefinition> supportedAnimations)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A WLED device identifier is required.", nameof(id));
        _ = setStateAsync ?? throw new ArgumentNullException(nameof(setStateAsync));

        return Create(
            id,
            (segmentId, isOn, intensity, color, cancellationToken) => setStateAsync(isOn, intensity, color, cancellationToken),
            setAnimationAsync == null
                ? null
                : (segmentId, request, cancellationToken) => setAnimationAsync(request, cancellationToken),
            supportedAnimations,
            null);
    }

    public static WledDevice Create(
        string id,
        Func<int, bool?, decimal?, DeviceColor, CancellationToken, Task> setStateAsync,
        Func<int, LightAnimationRequest, CancellationToken, Task> setAnimationAsync,
        IReadOnlyList<LightAnimationDefinition> supportedAnimations,
        Func<bool, CancellationToken, Task> setPowerAsync = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A WLED device identifier is required.", nameof(id));
        _ = setStateAsync ?? throw new ArgumentNullException(nameof(setStateAsync));
        return new WledDevice(id, setStateAsync, setAnimationAsync, supportedAnimations, setPowerAsync);
    }

    public void ApplyState(JsonElement state)
    {
        bool? isOn = null;
        decimal? intensity = null;
        DeviceColor.Rgb color = null;

        if (state.TryGetProperty("on", out JsonElement on)
            && on.ValueKind is JsonValueKind.True or JsonValueKind.False)
            isOn = on.GetBoolean();
        if (state.TryGetProperty("bri", out JsonElement brightness)
            && brightness.ValueKind == JsonValueKind.Number
            && brightness.TryGetInt32(out int brightnessValue))
            intensity = brightnessValue * 100M / 255M;
        _power?.ApplyState(isOn);
        if (!state.TryGetProperty("seg", out JsonElement segments)
            || segments.ValueKind != JsonValueKind.Array)
            return;

        List<WledSegment> nextSegments = [];
        foreach (JsonElement segment in segments.EnumerateArray())
        {
            if (!segment.TryGetProperty("id", out JsonElement id)
                || !id.TryGetInt32(out int segmentId)
                || segmentId < 0)
                segmentId = nextSegments.Count;

            color = ReadColor(segment);
            int? effectIndex = segment.TryGetProperty("fx", out JsonElement effect)
                && effect.TryGetInt32(out int parsedEffect)
                ? parsedEffect
                : null;
            WledSegment runtimeSegment = new(
                segmentId,
                _setStateAsync,
                _setAnimationAsync,
                _supportedAnimations);
            runtimeSegment.ApplyState(isOn, intensity, color, effectIndex);
            nextSegments.Add(runtimeSegment);
        }

        _segments.Clear();
        _segments.AddRange(nextSegments);
        _runtimeDevice.ReplaceElements(_segments.Select(segment => segment.Element));
    }

    private static DeviceColor.Rgb ReadColor(JsonElement segment)
    {
        if (!segment.TryGetProperty("col", out JsonElement colors)
            || colors.ValueKind != JsonValueKind.Array
            || colors.GetArrayLength() == 0
            || colors[0].ValueKind != JsonValueKind.Array
            || colors[0].GetArrayLength() < 3
            || !colors[0][0].TryGetByte(out byte red)
            || !colors[0][1].TryGetByte(out byte green)
            || !colors[0][2].TryGetByte(out byte blue))
            return null;

        return new DeviceColor.Rgb(red, green, blue);
    }

    private sealed class WledSegment
    {
        private readonly WledStateCapability _state;
        private readonly WledAnimationCapability _animation;

        public WledSegment(
            int id,
            Func<int, bool?, decimal?, DeviceColor, CancellationToken, Task> setStateAsync,
            Func<int, LightAnimationRequest, CancellationToken, Task> setAnimationAsync,
            IReadOnlyList<LightAnimationDefinition> supportedAnimations)
        {
            Id = id;
            _state = new WledStateCapability(
                (isOn, intensity, color, cancellationToken) => setStateAsync(id, isOn, intensity, color, cancellationToken));
            _animation = setAnimationAsync == null
                ? null
                : new WledAnimationCapability(
                    (request, cancellationToken) => setAnimationAsync(id, request, cancellationToken),
                    supportedAnimations);
            List<IDeviceCapability> capabilities = [_state];
            if (_animation != null)
                capabilities.Add(_animation);
            Element = new DeviceElement(string.Concat("Segment ", id), capabilities);
        }

        public int Id { get; }

        public DeviceElement Element { get; }

        public void ApplyState(bool? isOn, decimal? intensity, DeviceColor color, int? effectIndex)
        {
            _state.ApplyState(isOn, intensity, color);
            if (_animation != null && effectIndex.HasValue)
                _animation.ApplyState(isOn == false ? null : _animation.GetCode(effectIndex.Value));
        }
    }

    private sealed class WledPowerCapability : IToggleSwitchDevice
    {
        private readonly Func<bool, CancellationToken, Task> _setPowerAsync;

        public WledPowerCapability(Func<bool, CancellationToken, Task> setPowerAsync)
        {
            _setPowerAsync = setPowerAsync;
        }

        public bool? IsOn { get; private set; }

        public Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
        {
            return _setPowerAsync(isOn, cancellationToken);
        }

        public void ApplyState(bool? isOn)
        {
            if (isOn.HasValue)
                IsOn = isOn;
        }
    }

    private sealed class WledAnimationCapability : ILightAnimationDevice
    {
        private readonly Func<LightAnimationRequest, CancellationToken, Task> _setAnimationAsync;

        public WledAnimationCapability(
            Func<LightAnimationRequest, CancellationToken, Task> setAnimationAsync,
            IReadOnlyList<LightAnimationDefinition> supportedAnimations)
        {
            _setAnimationAsync = setAnimationAsync;
            SupportedAnimations = supportedAnimations;
        }

        public IReadOnlyList<LightAnimationDefinition> SupportedAnimations { get; }

        public LightAnimationState CurrentAnimation { get; private set; }

        public Task StartAnimationAsync(LightAnimationRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Code))
                throw new ArgumentException("An animation code is required.", nameof(request));
            return _setAnimationAsync(request, cancellationToken);
        }

        public Task StopAnimationAsync(CancellationToken cancellationToken = default)
        {
            return _setAnimationAsync(new LightAnimationRequest("wled.none"), cancellationToken);
        }

        public void ApplyState(string code)
        {
            CurrentAnimation = string.IsNullOrWhiteSpace(code)
                ? null
                : new LightAnimationState(
                    true,
                    code,
                    SupportedAnimations.FirstOrDefault(animation => animation.Code == code)?.Kind);
        }

        public string GetCode(int effectIndex)
        {
            return effectIndex >= 0 && effectIndex < SupportedAnimations.Count
                ? SupportedAnimations[effectIndex].Code
                : null;
        }
    }

    private sealed class WledStateCapability : IToggleSwitchDevice, IIntensityGradientDevice, IChromaticColorDevice
    {
        private readonly Func<bool?, decimal?, DeviceColor, CancellationToken, Task> _setStateAsync;

        public WledStateCapability(Func<bool?, decimal?, DeviceColor, CancellationToken, Task> setStateAsync)
        {
            _setStateAsync = setStateAsync;
            SupportedColorModels = [ColorModel.Rgb];
        }

        public bool? IsOn { get; private set; }

        public decimal? IntensityPercent { get; private set; }

        public IReadOnlyList<ColorModel> SupportedColorModels { get; }

        public DeviceColor CurrentColor { get; private set; }

        public Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
        {
            return _setStateAsync(isOn, null, null, cancellationToken);
        }

        public Task SetIntensityAsync(decimal intensityPercent, CancellationToken cancellationToken = default)
        {
            if (intensityPercent is < 0M or > 100M)
                throw new ArgumentOutOfRangeException(nameof(intensityPercent));
            return _setStateAsync(null, intensityPercent, null, cancellationToken);
        }

        public Task SetColorAsync(DeviceColor color, CancellationToken cancellationToken = default)
        {
            if (color == null)
                throw new ArgumentNullException(nameof(color));
            return _setStateAsync(null, null, color, cancellationToken);
        }

        public void ApplyState(bool? isOn, decimal? intensity, DeviceColor color)
        {
            if (isOn.HasValue)
                IsOn = isOn;
            if (intensity.HasValue)
                IntensityPercent = intensity;
            if (color != null)
                CurrentColor = color;
        }
    }
}
