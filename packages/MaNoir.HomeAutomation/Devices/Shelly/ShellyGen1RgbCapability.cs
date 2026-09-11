using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellyGen1RgbCapability : IToggleSwitchDevice, IIntensityGradientDevice, IChromaticColorDevice
{
    private readonly Func<bool?, decimal?, DeviceColor, CancellationToken, Task> _setState;

    public ShellyGen1RgbCapability(Func<bool?, decimal?, DeviceColor, CancellationToken, Task> setState)
    {
        _setState = setState ?? throw new ArgumentNullException(nameof(setState));
    }

    public bool? IsOn { get; private set; }

    public decimal? IntensityPercent { get; private set; }

    public IReadOnlyList<ColorModel> SupportedColorModels { get; } = [ColorModel.Rgb];

    public DeviceColor CurrentColor { get; private set; }

    public Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
    {
        return _setState(isOn, null, null, cancellationToken);
    }

    public Task SetIntensityAsync(decimal intensityPercent, CancellationToken cancellationToken = default)
    {
        if (intensityPercent is < 0M or > 100M)
            throw new ArgumentOutOfRangeException(nameof(intensityPercent));

        return _setState(intensityPercent > 0M, intensityPercent, null, cancellationToken);
    }

    public Task SetColorAsync(DeviceColor color, CancellationToken cancellationToken = default)
    {
        if (color is not DeviceColor.Rgb)
            throw new ArgumentException("Shelly Gen1 RGB components require an RGB color.", nameof(color));

        return _setState(true, null, color, cancellationToken);
    }

    public void ApplyStatus(string property, string payload)
    {
        if (!string.Equals(property, "status", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            if (status.TryGetProperty("ison", out JsonElement isOn)
                && isOn.ValueKind is JsonValueKind.True or JsonValueKind.False)
                IsOn = isOn.GetBoolean();
            if (status.TryGetProperty("brightness", out JsonElement brightness)
                && brightness.TryGetDecimal(out decimal brightnessValue)
                && brightnessValue is >= 0M and <= 100M)
                IntensityPercent = brightnessValue;
            if (status.TryGetProperty("red", out JsonElement red) && red.TryGetByte(out byte redValue)
                && status.TryGetProperty("green", out JsonElement green) && green.TryGetByte(out byte greenValue)
                && status.TryGetProperty("blue", out JsonElement blue) && blue.TryGetByte(out byte blueValue))
                CurrentColor = new DeviceColor.Rgb(redValue, greenValue, blueValue);
        }
        catch (JsonException)
        {
        }
    }
}
