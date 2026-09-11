using Home.Common.Model;
using Home.Common.Messages;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Protocols.Zigbee2Mqtt;
using MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;
using MQTTnet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class ZigbeeDeviceTests
{
    [TestMethod]
    public async Task Create_ShouldComposeActionableCapabilitiesAndPublishCommands()
    {
        MqttApplicationMessage message = null;
        using JsonDocument discovery = JsonDocument.Parse("""
        {
            "definition": {
                "exposes": [
                    { "property": "state" },
                    { "property": "brightness" },
                    { "property": "color", "features": [{ "property": "x" }, { "property": "y" }] },
                    { "property": "color_temp" }
                ]
            }
        }
        """);

        ZigbeeDevice device = ZigbeeDevice.Create(
            "kitchen-light",
            discovery.RootElement,
            new Zigbee2MqttProtocol((publishedMessage, _) =>
            {
                message = publishedMessage;
                return Task.CompletedTask;
            }));

        IDeviceElement element = device.Elements[0];
        IToggleSwitchDevice toggle = (IToggleSwitchDevice)element.Capabilities[0];
        IIntensityGradientDevice intensity = (IIntensityGradientDevice)element.Capabilities[1];
        IChromaticColorDevice color = (IChromaticColorDevice)element.Capabilities[2];
        IColorTemperatureDevice temperature = (IColorTemperatureDevice)element.Capabilities[3];

        Assert.AreEqual("Device", element.Name);
        Assert.HasCount(5, element.Capabilities);
        IRuntimeAvailabilityDevice availability = element.Capabilities.OfType<IRuntimeAvailabilityDevice>().Single();
        device.ApplyState(JsonSerializer.Deserialize<JsonElement>("""
        {
            "state": "ON",
            "brightness": 127,
            "color": { "x": 0.25, "y": 0.4 },
            "color_temp": 333
        }
        """));
        Assert.IsTrue(toggle.IsOn);
        Assert.AreEqual(50M, intensity.IntensityPercent);
        Assert.AreEqual(new DeviceColor.Xy(0.25, 0.4), color.CurrentColor);
        Assert.AreEqual(3003, temperature.CurrentKelvin);
        Assert.IsTrue(availability.IsAvailable);
        Assert.IsNotNull(availability.LastSeenUtc);

        List<DeviceStateChangedMessage.DeviceStateValue> changes = device.GetStateChanges();
        Assert.AreEqual("on", changes.Single(change => change.Name == "Switch").Value);
        Assert.AreEqual("50", changes.Single(change => change.Name == "Brightness").Value);
        Assert.AreEqual("{\"x\":0.25,\"y\":0.4}", changes.Single(change => change.Name == "Color").Value);
        Assert.AreEqual("3003", changes.Single(change => change.Name == "Color temperature").Value);

        await toggle.SetSwitchStateAsync(true);
        Assert.AreEqual("zigbee2mqtt/kitchen-light/set", message.Topic);
        Assert.AreEqual("ON", JsonDocument.Parse(message.Payload).RootElement.GetProperty("state").GetString());

        await intensity.SetIntensityAsync(50M);
        Assert.AreEqual(127, JsonDocument.Parse(message.Payload).RootElement.GetProperty("brightness").GetInt32());

        await color.SetColorAsync(new DeviceColor.Xy(0.31, 0.33));
        JsonElement colorPayload = JsonDocument.Parse(message.Payload).RootElement.GetProperty("color");
        Assert.AreEqual(0.31, colorPayload.GetProperty("x").GetDouble());
        Assert.AreEqual(0.33, colorPayload.GetProperty("y").GetDouble());

        await temperature.SetColorTemperatureAsync(3000);
        Assert.AreEqual(333, JsonDocument.Parse(message.Payload).RootElement.GetProperty("color_temp").GetInt32());
    }

    [TestMethod]
    public void Create_ShouldExposeNormalizedSensorReadings()
    {
        using JsonDocument discovery = JsonDocument.Parse("""
        {
            "definition": {
                "exposes": [
                    { "property": "temperature", "unit": "F" },
                    { "property": "pressure", "unit": "kPa" },
                    { "property": "humidity", "unit": "%" },
                    { "property": "occupancy" },
                    { "property": "battery", "unit": "%" }
                ]
            }
        }
        """);

        ZigbeeDevice device = ZigbeeDevice.Create(
            "hall-sensor",
            discovery.RootElement,
            new Zigbee2MqttProtocol((_, _) => Task.CompletedTask));
        ISensorDevice sensor = (ISensorDevice)device.Elements[0].Capabilities[0];

        device.ApplyState(JsonSerializer.Deserialize<JsonElement>("""
        {
            "temperature": 69.8,
            "pressure": 100.82,
            "humidity": 47.8,
            "occupancy": true,
            "battery": 86
        }
        """));

        Assert.AreEqual(21M, (decimal)sensor.Readings["temperature"].Value);
        Assert.AreEqual("temperature", sensor.Readings["temperature"].Definition.Type);
        Assert.AreEqual("Temperature", sensor.Readings["temperature"].Definition.Label);
        Assert.AreEqual("C", sensor.Readings["temperature"].Definition.CanonicalUnit);
        Assert.AreEqual(100820M, (decimal)sensor.Readings["pressure"].Value);
        Assert.AreEqual("Pa", sensor.Readings["pressure"].Definition.CanonicalUnit);
        Assert.AreEqual(47.8M, (decimal)sensor.Readings["humidity"].Value);
        Assert.IsTrue((bool)sensor.Readings["occupancy"].Value);
        Assert.AreEqual(86M, (decimal)sensor.Readings["battery"].Value);
    }

    [TestMethod]
    public void Create_ShouldExposeStableZigbeeMetadata()
    {
        using JsonDocument discovery = JsonDocument.Parse("""
        {
            "friendly_name": "hall-sensor",
            "ieee_address": "0x00124b0024cafe01",
            "manufacturer": "Acme",
            "model_id": "MS-100",
            "network_address": 41234,
            "power_source": "Battery",
            "software_build_id": "1.2.3",
            "supported": true,
            "interview_completed": true,
            "type": "EndDevice",
            "definition": { "model": "Motion Sensor", "vendor": "Acme" }
        }
        """);

        ZigbeeDevice device = ZigbeeDevice.Create(
            "hall-sensor",
            discovery.RootElement,
            new Zigbee2MqttProtocol((_, _) => Task.CompletedTask));

        Assert.AreEqual("zigbee:ieee:0x00124b0024cafe01", device.InternalId);
        Assert.AreEqual(device.InternalId, device.StableIdentity);
        Assert.AreEqual("hall-sensor", device.Metadata.FriendlyName);
        Assert.AreEqual("Acme", device.Metadata.Manufacturer);
        Assert.AreEqual("MS-100", device.Metadata.ModelId);
        Assert.AreEqual("Motion Sensor", device.Metadata.Model);
        Assert.AreEqual("EndDevice", device.Metadata.NetworkType);
        Assert.AreEqual(41234, device.Metadata.NetworkAddress);
        Assert.IsTrue(device.Metadata.Supported);
        Assert.IsTrue(device.Metadata.InterviewCompleted);

        RuntimeDeviceRegistry registry = new();
        registry.ApplySnapshot("zigbee2mqtt", [device]);
        Assert.AreSame(device, registry.GetById(device.InternalId));
        Assert.AreSame(device, registry.GetById(device.Id));
    }

    [TestMethod]
    public void Create_ShouldNormalizeRuntimeActions()
    {
        using JsonDocument discovery = JsonDocument.Parse("""
        {
            "definition": {
                "exposes": [
                    { "property": "action", "values": ["single", "rotate_left", "rotate_right"] }
                ]
            }
        }
        """);

        ZigbeeDevice device = ZigbeeDevice.Create(
            "living-room-dial",
            discovery.RootElement,
            new Zigbee2MqttProtocol((_, _) => Task.CompletedTask));

        Assert.IsTrue(device.TryApplyAction(JsonSerializer.Deserialize<JsonElement>("""
        {
            "action": "rotate_left",
            "action_angle": 15
        }
        """), out RuntimeDeviceAction action));

        Assert.AreEqual("rotary", action.Kind);
        Assert.AreEqual("rotate", action.Action);
        Assert.AreEqual("rotate_left", action.RawAction);
        Assert.AreEqual("left", action.Attributes["direction"]);
        Assert.AreEqual("-1", action.Attributes["delta"]);
        Assert.AreEqual("15", action.Attributes["action_angle"]);
    }

    [TestMethod]
    public void Zigbee2MqttBridge_ShouldExposeDiscoveredDevicesAsChildren()
    {
        using JsonDocument discovery = JsonDocument.Parse("""
        {
            "ieee_address": "0x00124b0024cafe01",
            "definition": { "exposes": [{ "property": "state" }] }
        }
        """);
        ZigbeeDevice child = ZigbeeDevice.Create(
            "kitchen-light",
            discovery.RootElement,
            new Zigbee2MqttProtocol((_, _) => Task.CompletedTask));

        Zigbee2MqttBridgeDevice bridge = Zigbee2MqttBridgeDevice.Create("/zigbee2mqtt/", [child]);
        IHubDevice hub = bridge.Capabilities.OfType<IHubDevice>().Single();

        Assert.AreEqual("zigbee2mqtt-bridge", bridge.Id);
        Assert.AreEqual("zigbee2mqtt:bridge:zigbee2mqtt", bridge.InternalId);
        Assert.AreEqual("zigbee2mqtt", bridge.TopicRoot);
        Assert.AreEqual("kitchen-light", hub.ChildDevices.Single().Id);
        Assert.HasCount(0, bridge.Elements);
    }
}