using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Protocols.Shelly;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Shelly;

/// <summary>
/// Runtime representation of a Shelly Gen2+ device.
/// </summary>
public sealed class ShellyGen2Device : IDevice
{
    private readonly RuntimeDevice _runtimeDevice;
    private readonly Dictionary<int, ShellySwitchCapability> _switches;
    private readonly Dictionary<int, ShellyLightCapability> _lights;
    private readonly Dictionary<int, ShellyRgbCapability> _rgbs;
    private readonly Dictionary<int, ShellyCoverCapability> _covers;
    private readonly Dictionary<string, ShellySensorCapability> _sensors;
    private readonly Dictionary<string, ShellyElectricalCapability> _electricals;
    private readonly Dictionary<string, ShellyStatusSensorCapability> _statusSensors;

    private ShellyGen2Device(
        string id,
        RuntimeDevice runtimeDevice,
        Dictionary<int, ShellySwitchCapability> switches,
        Dictionary<int, ShellyLightCapability> lights,
        Dictionary<int, ShellyRgbCapability> rgbs,
        Dictionary<int, ShellyCoverCapability> covers,
        Dictionary<string, ShellySensorCapability> sensors,
        Dictionary<string, ShellyElectricalCapability> electricals,
        Dictionary<string, ShellyStatusSensorCapability> statusSensors)
    {
        Id = id;
        _runtimeDevice = runtimeDevice;
        _switches = switches;
        _lights = lights;
        _rgbs = rgbs;
        _covers = covers;
        _sensors = sensors;
        _electricals = electricals;
        _statusSensors = statusSensors;
    }

    /// <summary>
    /// Gets the Shelly device identifier used by MQTT topics.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the stable runtime identity for this Shelly device.
    /// </summary>
    public string InternalId => string.Concat("shelly-gen2:", Id.Trim().ToLowerInvariant());

    /// <summary>
    /// Gets the capabilities exposed by the device.
    /// </summary>
    public IReadOnlyList<IDeviceCapability> Capabilities => _runtimeDevice.Capabilities;

    /// <summary>
    /// Gets the elements composing the device.
    /// </summary>
    public IReadOnlyList<IDeviceElement> Elements => _runtimeDevice.Elements;

    public static ShellyGen2Device Create(string id, JsonElement status, ShellyGen2Protocol protocol)
    {
        if (protocol == null)
            throw new ArgumentNullException(nameof(protocol));

        return Create(
            id,
            status,
            async (componentType, componentIndex, parameters, cancellationToken) =>
            {
                using JsonDocument response = await protocol.SetComponentStateAsync(componentType, componentIndex, parameters, cancellationToken);
            });
    }

    /// <summary>
    /// Creates a runtime device from a Shelly status document.
    /// </summary>
    /// <param name="id">The Shelly device identifier.</param>
    /// <param name="status">The result of Shelly.GetStatus.</param>
    /// <param name="setComponentState">Callback used to send component commands.</param>
    /// <returns>A runtime device containing one element per supported component.</returns>
    public static ShellyGen2Device Create(
        string id,
        JsonElement status,
        Func<string, int, IReadOnlyDictionary<string, object>, CancellationToken, Task> setComponentState)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A Shelly device identifier is required.", nameof(id));
        if (setComponentState == null)
            throw new ArgumentNullException(nameof(setComponentState));

        Dictionary<int, ShellySwitchCapability> switches = [];
        Dictionary<int, ShellyLightCapability> lights = [];
        Dictionary<int, ShellyRgbCapability> rgbs = [];
        Dictionary<int, ShellyCoverCapability> covers = [];
        Dictionary<string, ShellySensorCapability> sensors = [];
        Dictionary<string, ShellyElectricalCapability> electricals = [];
        Dictionary<string, ShellyStatusSensorCapability> statusSensors = [];
        List<DeviceElement> elements = [];
        if (status.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in status.EnumerateObject())
            {
                if (TryParseComponent(property.Name, "switch", out int switchIndex))
                {
                    ShellySwitchCapability capability = new(switchIndex, setComponentState);
                    switches[switchIndex] = capability;
                    List<IDeviceCapability> capabilities = [capability];
                    AddElectricalCapability(property.Value, "switch", switchIndex, capabilities, electricals);
                    elements.Add(new DeviceElement(GetComponentName("Switch", switchIndex), capabilities));
                    capability.ApplyStatus(property.Value);
                    continue;
                }

                if (TryParseComponent(property.Name, "light", out int lightIndex))
                {
                    ShellyLightCapability capability = new(lightIndex, setComponentState);
                    lights[lightIndex] = capability;
                    elements.Add(new DeviceElement(GetIndexedComponentName("Light", lightIndex), [capability]));
                    capability.ApplyStatus(property.Value);
                    continue;
                }

                if (TryParseComponent(property.Name, "rgb", out int rgbIndex))
                {
                    ShellyRgbCapability capability = new(rgbIndex, setComponentState);
                    rgbs[rgbIndex] = capability;
                    elements.Add(new DeviceElement(GetIndexedComponentName("RGB", rgbIndex), [capability]));
                    capability.ApplyStatus(property.Value);
                    continue;
                }

                if (TryParseComponent(property.Name, "cover", out int coverIndex))
                {
                    ShellyCoverCapability capability = property.Value.TryGetProperty("positioning", out JsonElement positioning)
                        && positioning.ValueKind == JsonValueKind.True
                        ? new ShellyPositionableCoverCapability(coverIndex, setComponentState)
                        : new ShellyCoverCapability(coverIndex, setComponentState);
                    covers[coverIndex] = capability;
                    elements.Add(new DeviceElement(GetIndexedComponentName("Cover", coverIndex), [capability]));
                    capability.ApplyStatus(property.Value);
                    continue;
                }

                if (TryGetSensorDefinition(property.Name, out string sensorType, out int sensorIndex, out string propertyName, out RuntimeSensorDefinition definition))
                {
                    ShellySensorCapability capability = new(propertyName, definition);
                    sensors[GetSensorKey(sensorType, sensorIndex)] = capability;
                    elements.Add(new DeviceElement(GetComponentName(definition.Label, sensorIndex), [capability]));
                    capability.ApplyStatus(property.Value);
                    continue;
                }

                if (TryGetElectricalComponent(property.Name, out string electricalType, out int electricalIndex))
                {
                    string electricalKey = GetElectricalKey(electricalType, electricalIndex);
                    if (!electricals.TryGetValue(electricalKey, out ShellyElectricalCapability capability))
                    {
                        capability = new ShellyElectricalCapability();
                        electricals[electricalKey] = capability;
                        elements.Add(new DeviceElement(GetIndexedComponentName("EM", electricalIndex), [capability]));
                    }

                    capability.ApplyStatus(electricalType, property.Value);
                    continue;
                }

                if (TryGetStatusSensorDefinition(property.Name, out string statusSensorType, out int statusSensorIndex, out string statusSensorLabel, out IReadOnlyDictionary<string, ShellyStatusReadingDefinition> definitions))
                {
                    ShellyStatusSensorCapability capability = new(definitions);
                    statusSensors[GetStatusSensorKey(statusSensorType, statusSensorIndex)] = capability;
                    elements.Add(new DeviceElement(GetStatusSensorName(statusSensorLabel, statusSensorIndex), [capability]));
                    capability.ApplyStatus(property.Value);
                }
            }
        }

        RuntimeDevice runtimeDevice = new(id, elements);
        return new ShellyGen2Device(id, runtimeDevice, switches, lights, rgbs, covers, sensors, electricals, statusSensors);
    }

    /// <summary>
    /// Applies a status document received for one Shelly component.
    /// </summary>
    /// <param name="componentName">The Shelly component name, for example switch:0 or light:0.</param>
    /// <param name="status">The component status document.</param>
    public void ApplyStatus(string componentName, JsonElement status)
    {
        if (TryParseComponent(componentName, "switch", out int switchIndex)
            && _switches.TryGetValue(switchIndex, out ShellySwitchCapability switchCapability))
        {
            switchCapability.ApplyStatus(status);
            if (_electricals.TryGetValue(GetElectricalKey("switch", switchIndex), out ShellyElectricalCapability switchElectricalCapability))
                switchElectricalCapability.ApplyStatus("switch", status);
            return;
        }

        if (TryParseComponent(componentName, "light", out int lightIndex)
            && _lights.TryGetValue(lightIndex, out ShellyLightCapability lightCapability))
        {
            lightCapability.ApplyStatus(status);
            return;
        }

        if (TryParseComponent(componentName, "rgb", out int rgbIndex)
            && _rgbs.TryGetValue(rgbIndex, out ShellyRgbCapability rgbCapability))
        {
            rgbCapability.ApplyStatus(status);
            return;
        }

        if (TryParseComponent(componentName, "cover", out int coverIndex)
            && _covers.TryGetValue(coverIndex, out ShellyCoverCapability coverCapability))
        {
            coverCapability.ApplyStatus(status);
            return;
        }

        if (TryGetSensorDefinition(componentName, out string sensorType, out int sensorIndex, out _, out _)
            && _sensors.TryGetValue(GetSensorKey(sensorType, sensorIndex), out ShellySensorCapability sensorCapability))
        {
            sensorCapability.ApplyStatus(status);
            return;
        }

        if (TryGetElectricalComponent(componentName, out string electricalType, out int electricalIndex)
            && _electricals.TryGetValue(GetElectricalKey(electricalType, electricalIndex), out ShellyElectricalCapability electricalCapability))
        {
            electricalCapability.ApplyStatus(electricalType, status);
            return;
        }

        if (TryGetStatusSensorDefinition(componentName, out string statusSensorType, out int statusSensorIndex, out _, out _)
            && _statusSensors.TryGetValue(GetStatusSensorKey(statusSensorType, statusSensorIndex), out ShellyStatusSensorCapability statusSensorCapability))
        {
            statusSensorCapability.ApplyStatus(status);
        }
    }

    private static bool TryParseComponent(string component, string expectedType, out int index)
    {
        index = 0;
        return component.StartsWith(string.Concat(expectedType, ":"), StringComparison.OrdinalIgnoreCase)
            && int.TryParse(component[(expectedType.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && index >= 0;
    }

    private static bool TryGetSensorDefinition(
        string component,
        out string sensorType,
        out int sensorIndex,
        out string propertyName,
        out RuntimeSensorDefinition definition)
    {
        sensorType = null;
        sensorIndex = 0;
        propertyName = null;
        definition = null;
        foreach ((string type, string property, RuntimeSensorDefinition sensorDefinition) in new[]
        {
            ("temperature", "tC", new RuntimeSensorDefinition("temperature", "Temperature", "C")),
            ("humidity", "rh", new RuntimeSensorDefinition("humidity", "Humidity", "%")),
            ("illuminance", "lux", new RuntimeSensorDefinition("illuminance", "Illuminance", "lx"))
        })
        {
            if (!TryParseComponent(component, type, out sensorIndex))
                continue;

            sensorType = type;
            propertyName = property;
            definition = sensorDefinition;
            return true;
        }

        return false;
    }

    private static bool TryGetElectricalComponent(string component, out string componentType, out int index)
    {
        foreach (string expectedType in new[] { "em", "em1", "em1data" })
        {
            if (TryParseComponent(component, expectedType, out index))
            {
                componentType = expectedType;
                return true;
            }
        }

        componentType = null;
        index = 0;
        return false;
    }

    private static string GetSensorKey(string sensorType, int sensorIndex)
    {
        return string.Concat(sensorType, ":", sensorIndex.ToString(CultureInfo.InvariantCulture));
    }

    private static string GetElectricalKey(string componentType, int index)
    {
        return string.Concat(componentType, ":", index.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddElectricalCapability(
        JsonElement status,
        string componentType,
        int index,
        List<IDeviceCapability> capabilities,
        Dictionary<string, ShellyElectricalCapability> electricals)
    {
        if (status.ValueKind != JsonValueKind.Object
            || (!status.TryGetProperty("apower", out _)
                && !status.TryGetProperty("voltage", out _)
                && !status.TryGetProperty("current", out _)
                && !status.TryGetProperty("pf", out _)
                && !status.TryGetProperty("aenergy", out _)
                && !status.TryGetProperty("ret_aenergy", out _)))
            return;

        ShellyElectricalCapability capability = new();
        capability.ApplyStatus(componentType, status);
        electricals[GetElectricalKey(componentType, index)] = capability;
        capabilities.Add(capability);
    }

    private static bool TryGetStatusSensorDefinition(
        string component,
        out string componentType,
        out int index,
        out string label,
        out IReadOnlyDictionary<string, ShellyStatusReadingDefinition> definitions)
    {
        componentType = null;
        index = 0;
        label = null;
        definitions = null;
        foreach (string expectedType in new[] { "battery", "flood", "smoke", "motion", "presence" })
        {
            if (!TryParseComponent(component, expectedType, out index))
                continue;

            componentType = expectedType;
            label = expectedType switch
            {
                "battery" => "Battery",
                "flood" => "Flood",
                "smoke" => "Smoke",
                "motion" => "Motion",
                _ => "Presence"
            };
            definitions = expectedType switch
            {
                "battery" => new Dictionary<string, ShellyStatusReadingDefinition>()
                {
                    ["battery"] = new("percent", new RuntimeSensorDefinition("battery", "Battery", "%"))
                },
                "flood" => new Dictionary<string, ShellyStatusReadingDefinition>()
                {
                    ["water_leak"] = new("alarm", new RuntimeSensorDefinition("water_leak", "Flood", null))
                },
                "smoke" => new Dictionary<string, ShellyStatusReadingDefinition>()
                {
                    ["smoke"] = new("alarm", new RuntimeSensorDefinition("smoke", "Smoke", null))
                },
                "motion" => new Dictionary<string, ShellyStatusReadingDefinition>()
                {
                    ["occupancy"] = new("motion", new RuntimeSensorDefinition("occupancy", "Motion", null))
                },
                _ => new Dictionary<string, ShellyStatusReadingDefinition>()
                {
                    ["occupancy"] = new("presence", new RuntimeSensorDefinition("occupancy", "Presence", null))
                }
            };
            return true;
        }

        return false;
    }

    private static string GetStatusSensorKey(string componentType, int index)
    {
        return string.Concat(componentType, ":", index.ToString(CultureInfo.InvariantCulture));
    }

    private static string GetStatusSensorName(string label, int index)
    {
        return index == 0 ? label : string.Concat(label, " ", index.ToString(CultureInfo.InvariantCulture));
    }

    private static string GetComponentName(string label, int index)
    {
        return index == 0 ? label : string.Concat(label, " ", index.ToString(CultureInfo.InvariantCulture));
    }

    private static string GetIndexedComponentName(string label, int index)
    {
        return string.Concat(label, " ", index.ToString(CultureInfo.InvariantCulture));
    }
}
