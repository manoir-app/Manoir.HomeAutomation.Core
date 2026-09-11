using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellyRgbCapability : IToggleSwitchDevice, IIntensityGradientDevice, IChromaticColorDevice
{
    private readonly int _index;
    private readonly Func<string, int, IReadOnlyDictionary<string, object>, CancellationToken, Task> _setComponentState;

    public ShellyRgbCapability(
        int index,
        Func<string, int, IReadOnlyDictionary<string, object>, CancellationToken, Task> setComponentState)
    {
        _index = index;
        _setComponentState = setComponentState;
    }

    public bool? IsOn { get; private set; }

    public decimal? IntensityPercent { get; private set; }

    public IReadOnlyList<ColorModel> SupportedColorModels { get; } = [ColorModel.Rgb];

    public DeviceColor CurrentColor { get; private set; }

    public Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
    {
        return _setComponentState("rgb", _index, new Dictionary<string, object>() { ["on"] = isOn }, cancellationToken);
    }

    public Task SetIntensityAsync(decimal intensityPercent, CancellationToken cancellationToken = default)
    {
        if (intensityPercent is < 0M or > 100M)
            throw new ArgumentOutOfRangeException(nameof(intensityPercent));

        return _setComponentState(
            "rgb",
            _index,
            new Dictionary<string, object>()
            {
                ["brightness"] = intensityPercent,
                ["on"] = intensityPercent > 0M
            },
            cancellationToken);
    }

    public Task SetColorAsync(DeviceColor color, CancellationToken cancellationToken = default)
    {
        if (color is not DeviceColor.Rgb rgb)
            throw new ArgumentException("Shelly RGB components require an RGB color.", nameof(color));

        return _setComponentState(
            "rgb",
            _index,
            new Dictionary<string, object>()
            {
                ["rgb"] = new[] { (int)rgb.Red, (int)rgb.Green, (int)rgb.Blue },
                ["on"] = true
            },
            cancellationToken);
    }

    public void ApplyStatus(JsonElement status)
    {
        if (status.ValueKind != JsonValueKind.Object)
            return;

        if (status.TryGetProperty("output", out JsonElement output)
            && output.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            IsOn = output.GetBoolean();
        }

        if (status.TryGetProperty("brightness", out JsonElement brightness)
            && brightness.TryGetDecimal(out decimal brightnessValue)
            && brightnessValue is >= 0M and <= 100M)
        {
            IntensityPercent = brightnessValue;
        }

        if (status.TryGetProperty("rgb", out JsonElement rgb)
            && rgb.ValueKind == JsonValueKind.Array
            && rgb.GetArrayLength() == 3
            && rgb[0].TryGetByte(out byte red)
            && rgb[1].TryGetByte(out byte green)
            && rgb[2].TryGetByte(out byte blue))
        {
            CurrentColor = new DeviceColor.Rgb(red, green, blue);
        }
    }
}
