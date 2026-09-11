using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MaNoir.HomeAutomation.Devices.Hue;

public sealed class HueBridgeDevice : IDevice
{
    private readonly RuntimeDevice _runtimeDevice;

    private HueBridgeDevice(string id, string bridgeAddress, RuntimeDevice runtimeDevice)
    {
        Id = id;
        BridgeAddress = bridgeAddress;
        _runtimeDevice = runtimeDevice;
    }

    public string Id { get; }

    public string BridgeAddress { get; }

    public IReadOnlyList<IDeviceCapability> Capabilities => _runtimeDevice.Capabilities;

    public IReadOnlyList<IDeviceElement> Elements => _runtimeDevice.Elements;

    public static HueBridgeDevice Create(
        string bridgeAddress,
        IEnumerable<IDevice> childDevices)
    {
        if (string.IsNullOrWhiteSpace(bridgeAddress))
            throw new ArgumentException("A Hue bridge address is required.", nameof(bridgeAddress));

        DeviceReference[] childReferences = (childDevices ?? Enumerable.Empty<IDevice>())
            .Where(device => device != null && !string.IsNullOrWhiteSpace(device.Id))
            .Select(device => new DeviceReference(device.Id))
            .ToArray();
        RuntimeDevice runtimeDevice = new(
            "hue-bridge",
            [],
            [new HueHubCapability(childReferences)]);

        return new HueBridgeDevice("hue-bridge", bridgeAddress.Trim(), runtimeDevice);
    }

    private sealed class HueHubCapability : IHubDevice
    {
        public HueHubCapability(IReadOnlyList<DeviceReference> childDevices)
        {
            ChildDevices = childDevices;
        }

        public IReadOnlyList<DeviceReference> ChildDevices { get; }
    }
}