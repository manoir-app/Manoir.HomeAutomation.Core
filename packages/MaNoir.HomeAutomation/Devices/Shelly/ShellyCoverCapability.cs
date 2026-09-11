using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal class ShellyCoverCapability : ICoverDevice
{
    private readonly int _index;
    private readonly Func<string, int, IReadOnlyDictionary<string, object>, CancellationToken, Task> _setComponentState;

    public ShellyCoverCapability(
        int index,
        Func<string, int, IReadOnlyDictionary<string, object>, CancellationToken, Task> setComponentState)
    {
        _index = index;
        _setComponentState = setComponentState;
    }

    public string State { get; private set; }

    public Task OpenAsync(CancellationToken cancellationToken = default) => SendCommandAsync("open", cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken = default) => SendCommandAsync("close", cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) => SendCommandAsync("stop", cancellationToken);

    public virtual void ApplyStatus(JsonElement status)
    {
        if (status.ValueKind != JsonValueKind.Object)
            return;

        if (status.TryGetProperty("state", out JsonElement state)
            && state.ValueKind == JsonValueKind.String
            && state.GetString() is string stateValue
            && stateValue is "open" or "close" or "stop")
        {
            State = stateValue;
        }

    }

    protected Task SetComponentStateAsync(IReadOnlyDictionary<string, object> state, CancellationToken cancellationToken) =>
        _setComponentState("cover", _index, state, cancellationToken);

    private Task SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        return _setComponentState(
            "cover",
            _index,
            new Dictionary<string, object>() { ["command"] = command },
            cancellationToken);
    }
}
