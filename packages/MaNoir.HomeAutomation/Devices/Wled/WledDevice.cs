using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Wled;

public sealed class WledDevice : IDevice
{
    private readonly RuntimeDevice _runtimeDevice;
    private readonly WledStateCapability _state;

    private WledDevice(string id, RuntimeDevice runtimeDevice, WledStateCapability state)
    {
        Id = id;
        _runtimeDevice = runtimeDevice;
        _state = state;
    }

    public string Id { get; }

    public string InternalId => string.Concat("wled:", Id.Trim().ToLowerInvariant());

    public IReadOnlyList<IDeviceCapability> Capabilities => _runtimeDevice.Capabilities;

    public IReadOnlyList<IDeviceElement> Elements => _runtimeDevice.Elements;

    public static WledDevice Create(
        string id,
        Func<bool?, decimal?, DeviceColor, CancellationToken, Task> setStateAsync)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A WLED device identifier is required.", nameof(id));
        _ = setStateAsync ?? throw new ArgumentNullException(nameof(setStateAsync));

        WledStateCapability state = new(setStateAsync);
        RuntimeDevice runtimeDevice = new(id, [new DeviceElement("LED strip", [state])]);
        return new WledDevice(id, runtimeDevice, state);
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
        if (state.TryGetProperty("seg", out JsonElement segments)
            && segments.ValueKind == JsonValueKind.Array
            && segments.GetArrayLength() > 0
            && segments[0].TryGetProperty("col", out JsonElement colors)
            && colors.ValueKind == JsonValueKind.Array
            && colors.GetArrayLength() > 0
            && colors[0].ValueKind == JsonValueKind.Array
            && colors[0].GetArrayLength() >= 3
            && colors[0][0].TryGetByte(out byte red)
            && colors[0][1].TryGetByte(out byte green)
            && colors[0][2].TryGetByte(out byte blue))
            color = new DeviceColor.Rgb(red, green, blue);

        _state.ApplyState(isOn, intensity, color);
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
