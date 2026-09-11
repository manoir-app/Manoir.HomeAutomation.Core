using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellyGen1InputCapability : IRuntimeActionDevice
{
    public ShellyGen1InputCapability()
    {
        AvailableActions =
        [
            new RuntimeDeviceAction("button", "single_push", "S", new Dictionary<string, string>()),
            new RuntimeDeviceAction("button", "double_push", "SS", new Dictionary<string, string>()),
            new RuntimeDeviceAction("button", "triple_push", "SSS", new Dictionary<string, string>()),
            new RuntimeDeviceAction("button", "long_push", "L", new Dictionary<string, string>())
        ];
    }

    public IReadOnlyList<RuntimeDeviceAction> AvailableActions { get; }

    public RuntimeDeviceAction LastAction { get; private set; }

    public void ApplyAction(string rawAction, IReadOnlyDictionary<string, string> attributes)
    {
        if (string.IsNullOrWhiteSpace(rawAction))
            return;

        string action = rawAction.Trim().ToUpperInvariant() switch
        {
            "S" => "single_push",
            "SS" => "double_push",
            "SSS" => "triple_push",
            "L" => "long_push",
            _ => rawAction.Trim()
        };

        LastAction = new RuntimeDeviceAction("button", action, rawAction.Trim(), attributes ?? new Dictionary<string, string>());
    }
}