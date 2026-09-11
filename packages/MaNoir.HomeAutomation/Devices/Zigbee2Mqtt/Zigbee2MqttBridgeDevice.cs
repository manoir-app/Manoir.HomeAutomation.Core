using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;

public sealed class Zigbee2MqttBridgeDevice : IDevice
{
    private readonly RuntimeDevice _runtimeDevice;

    private Zigbee2MqttBridgeDevice(
        string id,
        string topicRoot,
        string internalId,
        RuntimeDevice runtimeDevice)
    {
        Id = id;
        TopicRoot = topicRoot;
        InternalId = internalId;
        _runtimeDevice = runtimeDevice;
    }

    public string Id { get; }

    public string InternalId { get; }

    public string TopicRoot { get; }

    public IReadOnlyList<IDeviceCapability> Capabilities => _runtimeDevice.Capabilities;

    public IReadOnlyList<IDeviceElement> Elements => _runtimeDevice.Elements;

    public static Zigbee2MqttBridgeDevice Create(
        string topicRoot,
        IEnumerable<IDevice> childDevices)
    {
        if (string.IsNullOrWhiteSpace(topicRoot))
            throw new ArgumentException("A Zigbee2MQTT topic root is required.", nameof(topicRoot));

        string normalizedTopicRoot = topicRoot.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedTopicRoot))
            throw new ArgumentException("A Zigbee2MQTT topic root is required.", nameof(topicRoot));

        DeviceReference[] childReferences = (childDevices ?? Enumerable.Empty<IDevice>())
            .Where(device => device != null && !string.IsNullOrWhiteSpace(device.Id))
            .Select(device => new DeviceReference(device.Id))
            .ToArray();
        RuntimeDevice runtimeDevice = new(
            "zigbee2mqtt-bridge",
            [],
            [new ZigbeeHubCapability(childReferences)]);

        Zigbee2MqttBridgeDevice bridge = new(
            "zigbee2mqtt-bridge",
            normalizedTopicRoot,
            string.Concat("zigbee2mqtt:bridge:", normalizedTopicRoot.ToLowerInvariant()),
            runtimeDevice);
        return bridge;
    }

    private sealed class ZigbeeHubCapability : IHubDevice
    {
        public ZigbeeHubCapability(IReadOnlyList<DeviceReference> childDevices)
        {
            ChildDevices = childDevices;
        }

        public IReadOnlyList<DeviceReference> ChildDevices { get; }
    }
}
