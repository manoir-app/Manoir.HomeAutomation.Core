using MaNoir.HomeAutomation.Devices;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellyGen1LightCapability : IToggleSwitchDevice, IIntensityGradientDevice
{
    private readonly Func<bool?, decimal?, CancellationToken, Task> _setState;

    public ShellyGen1LightCapability(Func<bool?, decimal?, CancellationToken, Task> setState)
    {
        _setState = setState ?? throw new ArgumentNullException(nameof(setState));
    }

    public bool? IsOn { get; private set; }

    public decimal? IntensityPercent { get; private set; }

    public Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
    {
        return _setState(isOn, null, cancellationToken);
    }

    public Task SetIntensityAsync(decimal intensityPercent, CancellationToken cancellationToken = default)
    {
        if (intensityPercent is < 0M or > 100M)
            throw new ArgumentOutOfRangeException(nameof(intensityPercent));

        return _setState(intensityPercent > 0M, intensityPercent, cancellationToken);
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
            {
                IsOn = isOn.GetBoolean();
            }

            if (status.TryGetProperty("brightness", out JsonElement brightness)
                && brightness.TryGetDecimal(out decimal brightnessValue)
                && brightnessValue is >= 0M and <= 100M)
            {
                IntensityPercent = brightnessValue;
            }
        }
        catch (JsonException)
        {
        }
    }
}
