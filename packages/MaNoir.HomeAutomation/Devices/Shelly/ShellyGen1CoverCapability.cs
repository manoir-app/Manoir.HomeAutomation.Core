using MaNoir.HomeAutomation.Devices;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal class ShellyGen1CoverCapability : ICoverDevice
{
    private readonly Func<string, CancellationToken, Task> _sendCommand;

    public ShellyGen1CoverCapability(Func<string, CancellationToken, Task> sendCommand)
    {
        _sendCommand = sendCommand ?? throw new ArgumentNullException(nameof(sendCommand));
    }

    public string State { get; private set; }

    public Task OpenAsync(CancellationToken cancellationToken = default) => _sendCommand("open", cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken = default) => _sendCommand("close", cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) => _sendCommand("stop", cancellationToken);

    protected Task SendCommandAsync(string command, CancellationToken cancellationToken) => _sendCommand(command, cancellationToken);

    public virtual void ApplyStatus(string property, string payload)
    {
        if (string.IsNullOrEmpty(property))
        {
            string state = payload?.Trim().ToLowerInvariant();
            if (state is "open" or "close" or "stop")
                State = state;
            return;
        }

    }
}
