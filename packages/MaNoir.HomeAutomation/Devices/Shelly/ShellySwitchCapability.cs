using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellySwitchCapability : IToggleSwitchDevice
{
    private readonly int _index;
    private readonly Func<string, int, IReadOnlyDictionary<string, object>, CancellationToken, Task> _setComponentState;

    public ShellySwitchCapability(
        int index,
        Func<string, int, IReadOnlyDictionary<string, object>, CancellationToken, Task> setComponentState)
    {
        _index = index;
        _setComponentState = setComponentState;
    }

    public bool? IsOn { get; private set; }

    public Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
    {
        return _setComponentState("switch", _index, new Dictionary<string, object>() { ["on"] = isOn }, cancellationToken);
    }

    public void ApplyStatus(JsonElement status)
    {
        if (status.ValueKind == JsonValueKind.Object
            && status.TryGetProperty("output", out JsonElement output)
            && output.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            IsOn = output.GetBoolean();
        }
    }
}
