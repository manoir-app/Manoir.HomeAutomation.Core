using MaNoir.HomeAutomation.Devices;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellyGen1PositionableCoverCapability : ShellyGen1CoverCapability, IShutterDevice
{
    private readonly Func<CancellationToken, Task<bool?>> _getPositioning;
    public ShellyGen1PositionableCoverCapability(
        Func<string, CancellationToken, Task> sendCommand,
        Func<CancellationToken, Task<bool?>> getPositioning = null)
        : base(sendCommand)
    {
        _getPositioning = getPositioning;
        SupportsPosition = true;
    }

    public decimal? PositionPercent { get; private set; }

    public bool SupportsPosition { get; private set; }

    public async Task SetPositionAsync(decimal positionPercent, CancellationToken cancellationToken = default)
    {
        if (positionPercent is < 0M or > 100M)
            throw new ArgumentOutOfRangeException(nameof(positionPercent));

        if (!SupportsPosition && _getPositioning != null)
        {
            bool? positioning = await _getPositioning(cancellationToken);
            if (positioning.HasValue)
                ApplyStatus("positioning", positioning.Value.ToString());
        }

        if (!SupportsPosition)
            throw new InvalidOperationException("The Shelly Gen1 cover does not support positioning.");

        await base.SendCommandAsync(positionPercent.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
    }

    public override void ApplyStatus(string property, string payload)
    {
        base.ApplyStatus(property, payload);
        if (string.Equals(property, "positioning", StringComparison.OrdinalIgnoreCase)
            && bool.TryParse(payload, out bool positioning))
        {
            if (!positioning)
                PositionPercent = null;
            return;
        }

        if (string.Equals(property, "pos", StringComparison.OrdinalIgnoreCase)
            && decimal.TryParse(payload, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal position))
        {
            PositionPercent = SupportsPosition && position is >= 0M and <= 100M ? position : null;
        }
    }
}