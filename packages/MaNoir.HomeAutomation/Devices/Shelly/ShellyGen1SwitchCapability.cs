using MaNoir.HomeAutomation.Devices;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellyGen1SwitchCapability : IToggleSwitchDevice
{
    private readonly Func<bool, CancellationToken, Task> _setState;

    public ShellyGen1SwitchCapability(Func<bool, CancellationToken, Task> setState)
    {
        _setState = setState ?? throw new ArgumentNullException(nameof(setState));
    }

    public bool? IsOn { get; private set; }

    public Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
    {
        return _setState(isOn, cancellationToken);
    }

    public void ApplyStatus(string payload)
    {
        if (string.Equals(payload?.Trim(), "on", StringComparison.OrdinalIgnoreCase))
            IsOn = true;
        else if (string.Equals(payload?.Trim(), "off", StringComparison.OrdinalIgnoreCase))
            IsOn = false;
    }
}
