using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellyPositionableCoverCapability : ShellyCoverCapability, IShutterDevice
{
    public ShellyPositionableCoverCapability(
        int index,
        Func<string, int, IReadOnlyDictionary<string, object>, CancellationToken, Task> setComponentState)
        : base(index, setComponentState)
    {
        SupportsPosition = true;
    }

    public decimal? PositionPercent { get; private set; }

    public bool SupportsPosition { get; private set; }

    public Task SetPositionAsync(decimal positionPercent, CancellationToken cancellationToken = default)
    {
        if (positionPercent is < 0M or > 100M)
            throw new ArgumentOutOfRangeException(nameof(positionPercent));
        if (!SupportsPosition)
            throw new InvalidOperationException("The Shelly cover does not support positioning.");

        return SetComponentStateAsync(
            new Dictionary<string, object>() { ["pos"] = positionPercent },
            cancellationToken);
    }

    public override void ApplyStatus(JsonElement status)
    {
        base.ApplyStatus(status);
        if (status.TryGetProperty("current_pos", out JsonElement position)
            && position.TryGetDecimal(out decimal positionValue)
            && positionValue is >= 0M and <= 100M)
        {
            PositionPercent = positionValue;
        }
    }
}