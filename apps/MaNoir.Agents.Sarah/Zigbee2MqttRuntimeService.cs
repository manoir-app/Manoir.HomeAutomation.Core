using Home.Common;
using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.HomeAutomation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah;

public sealed class Zigbee2MqttRuntimeService : BackgroundService
{
    // TODO: Promote Zigbee metadata (IEEE address, manufacturer, model, network type, power source and last activity) from discovery JSON to device fields.
    // TODO: Model exposes as structured discovery data, including numeric ranges, enum values, units and writable access.
    // TODO: Support additional Zigbee2MQTT commands such as locks, covers, climate, groups, identify and native Zigbee scenes.
    // TODO: Handle bridge diagnostics and command failures, including last received message and bridge health information.
    private readonly ILogger<Zigbee2MqttRuntimeService> _logger;

    public Zigbee2MqttRuntimeService(ILogger<Zigbee2MqttRuntimeService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string topicRoot = GetTopicRoot();
        (string host, int port) = ResolveMqttEndpoint();
        MqttFactory factory = new MqttFactory();
        using IMqttClient client = factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += args => HandleMessageAsync(args.ApplicationMessage.Topic, Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()), stoppingToken);

        try
        {
            await client.ConnectAsync(new MqttClientOptionsBuilder()
                .WithClientId("manoir-sarah-zigbee2mqtt")
                .WithTcpServer(host, port)
                .Build(), stoppingToken);
            await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(string.Concat(topicRoot, "/#"))
                .Build(), stoppingToken);
            _logger.LogInformation("Subscribed to Zigbee2MQTT topic root {TopicRoot} at {Host}:{Port}.", topicRoot, host, port);

            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None);
        }
    }

    public async Task HandleMessageAsync(string topic, string payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        if (TryGetAvailabilityDeviceId(topic, out string availabilityDeviceId))
        {
            string availability = payload.Trim();
            if (string.Equals(availability, "online", StringComparison.OrdinalIgnoreCase)
                || string.Equals(availability, "offline", StringComparison.OrdinalIgnoreCase))
            {
                await new DeviceLogic().ChangeStatusAsync("zigbee2mqtt", availabilityDeviceId, availability, cancellationToken);
            }

            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (IsBridgeDevicesTopic(topic))
            {
                await HandleBridgeDevicesAsync(document.RootElement, cancellationToken);
                return;
            }

            if (!TryGetDeviceId(topic, out string deviceId))
                return;

            DeviceLogic deviceLogic = new DeviceLogic();
            Device device = await deviceLogic.GetByIdAsync(deviceId, cancellationToken)
                ?? await deviceLogic.GetByInternalNameAndPlatformAsync(deviceId, "zigbee2mqtt", cancellationToken);
            DeviceActionTriggeredMessage actionMessage = CreateDeviceAction(document.RootElement, device);
            if (actionMessage != null)
                NatsInterprocess.Push(actionMessage);

            List<DeviceStateChangedMessage.DeviceStateValue> changes = GetDataChanges(document.RootElement, GetExposedUnits(device?.ConfigurationData));
            if (changes.Count == 0)
                return;

            string role = changes.Exists(change => change.StandardDataType == DeviceData.DataTypeSwitch)
                ? Device.HomeAutomationRoleSwitch
                : Device.HomeAutomationMainRoleSensors;

            await deviceLogic.OnDeviceStateChangedAsync(
                "zigbee2mqtt",
                deviceId,
                role,
                "online",
                changes,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Ignoring invalid Zigbee2MQTT payload on {Topic}.", topic);
        }
    }

    private static List<DeviceStateChangedMessage.DeviceStateValue> GetDataChanges(JsonElement payload, Dictionary<string, string> exposedUnits)
    {
        List<DeviceStateChangedMessage.DeviceStateValue> changes = [];
        AddSwitchState(payload, changes);
        AddBrightness(payload, changes);
        AddColor(payload, changes);
        AddData(payload, "occupancy", "Occupancy", DeviceDataCategory.DeviceState, DeviceData.DataTypeOccupancy, null, exposedUnits, changes, isMainData: true);
        AddData(payload, "contact", "Contact", DeviceDataCategory.DeviceState, DeviceData.DataTypeContact, null, exposedUnits, changes, isMainData: true);
        AddData(payload, "water_leak", "WaterLeak", DeviceDataCategory.DeviceState, DeviceData.DataTypeWaterLeak, null, exposedUnits, changes, isMainData: true);
        AddData(payload, "smoke", "Smoke", DeviceDataCategory.DeviceState, DeviceData.DataTypeSmoke, null, exposedUnits, changes, isMainData: true);
        AddData(payload, "carbon_monoxide", "CarbonMonoxide", DeviceDataCategory.DeviceState, DeviceData.DataTypeCarbonMonoxide, null, exposedUnits, changes, isMainData: true);
        AddData(payload, "tamper", "Tamper", DeviceDataCategory.DeviceState, DeviceData.DataTypeTamper, null, exposedUnits, changes, isMainData: true);
        AddData(payload, "vibration", "Vibration", DeviceDataCategory.DeviceState, DeviceData.DataTypeVibration, null, exposedUnits, changes, isMainData: true);

        AddData(payload, "battery", "Battery", DeviceDataCategory.DeviceHealth, DeviceData.DataTypeBatteryPercentage, "%", exposedUnits, changes, isMainData: true);
        AddData(payload, "battery_low", "BatteryLow", DeviceDataCategory.DeviceHealth, DeviceData.DataTypeBatteryLow, null, exposedUnits, changes);
        AddData(payload, "linkquality", "LinkQuality", DeviceDataCategory.DeviceHealth, DeviceData.DataTypeLinkSignalStrength, null, exposedUnits, changes);

        AddData(payload, "temperature", "Temperature", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorTemperature, "C", exposedUnits, changes);
        AddData(payload, "humidity", "Humidity", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorHumidity, "%", exposedUnits, changes);
        AddData(payload, "pressure", "Pressure", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorPressure, "Pa", exposedUnits, changes);
        AddData(payload, "illuminance", "Illuminance", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorIlluminance, "lx", exposedUnits, changes);
        AddData(payload, "illuminance_lux", "Illuminance", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorIlluminance, "lx", exposedUnits, changes);
        AddData(payload, "co2", "CO2", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorCo2, "ppm", exposedUnits, changes);
        AddData(payload, "voc", "VOC", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorVoc, "ug/m3", exposedUnits, changes);
        AddData(payload, "pm25", "PM2.5", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorPm25, "ug/m3", exposedUnits, changes);
        AddData(payload, "pm10", "PM10", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorPm10, "ug/m3", exposedUnits, changes);
        AddData(payload, "soil_moisture", "SoilMoisture", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorSoilMoisture, "%", exposedUnits, changes);
        AddData(payload, "noise", "Noise", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorNoise, "dB", exposedUnits, changes);
        AddData(payload, "formaldehyde", "Formaldehyde", DeviceDataCategory.SensorReading, DeviceData.DataTypeSensorFormaldehyde, "ug/m3", exposedUnits, changes);
        AddData(payload, "power", "Power", DeviceDataCategory.SensorReading, DeviceData.DataTypePowerCurrentConsumption, "W", exposedUnits, changes);
        AddData(payload, "energy", "Energy", DeviceDataCategory.SensorReading, DeviceData.DataTypePowerTotal, "kWh", exposedUnits, changes);
        return changes;
    }

    private static DeviceActionTriggeredMessage CreateDeviceAction(JsonElement payload, Device device)
    {
        if (device == null
            || !payload.TryGetProperty("action", out JsonElement action)
            || action.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(action.GetString()))
        {
            return null;
        }

        string rawAction = action.GetString().Trim();
        DeviceAvailableAction availableAction = NormalizeAvailableAction(rawAction);
        DeviceActionTriggeredMessage message = new DeviceActionTriggeredMessage()
        {
            DeviceId = device.Id,
            DeviceInternalName = device.DeviceInternalName,
            DevicePlatform = "zigbee2mqtt",
            ActionKind = availableAction.ActionKind,
            Action = availableAction.Action,
            RawAction = availableAction.RawAction,
            Attributes = new Dictionary<string, string>(availableAction.Attributes)
        };

        foreach (JsonProperty property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, "action", StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array)
            {
                continue;
            }

            message.Attributes[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => property.Value.GetRawText()
            };
        }

        return message;
    }

    private static void AddColor(JsonElement payload, List<DeviceStateChangedMessage.DeviceStateValue> changes)
    {
        if (!payload.TryGetProperty("color", out JsonElement color) || color.ValueKind != JsonValueKind.Object)
            return;

        changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
        {
            Name = "Color",
            Value = color.GetRawText(),
            Category = DeviceDataCategory.DeviceState,
            StandardDataType = DeviceData.DataTypeColor,
            IsMainData = true
        });
    }

    private static void AddBrightness(JsonElement payload, List<DeviceStateChangedMessage.DeviceStateValue> changes)
    {
        if (!payload.TryGetProperty("brightness", out JsonElement brightness) || brightness.ValueKind != JsonValueKind.Number || !brightness.TryGetDecimal(out decimal rawBrightness))
            return;

        decimal clampedBrightness = Math.Clamp(rawBrightness, 0M, 254M);
        int brightnessPercent = (int)Math.Ceiling(clampedBrightness * 100M / 254M);
        changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
        {
            Name = "Brightness",
            Value = brightnessPercent.ToString(CultureInfo.InvariantCulture),
            Category = DeviceDataCategory.DeviceState,
            StandardDataType = DeviceData.DataTypeGradient,
            IsMainData = true
        });
    }

    private static void AddSwitchState(JsonElement payload, List<DeviceStateChangedMessage.DeviceStateValue> changes)
    {
        if (!payload.TryGetProperty("state", out JsonElement state) || state.ValueKind != JsonValueKind.String)
            return;

        string value = state.GetString();
        if (!string.Equals(value, "ON", StringComparison.OrdinalIgnoreCase) && !string.Equals(value, "OFF", StringComparison.OrdinalIgnoreCase))
            return;

        changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
        {
            Name = "Switch",
            Value = value.ToLowerInvariant(),
            Category = DeviceDataCategory.DeviceState,
            StandardDataType = DeviceData.DataTypeSwitch,
            IsMainData = true
        });
    }

    private static void AddData(JsonElement payload, string propertyName, string dataName, DeviceDataCategory category, string dataType, string valueUnit, Dictionary<string, string> exposedUnits, List<DeviceStateChangedMessage.DeviceStateValue> changes, bool isMainData = false)
    {
        if (!payload.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array)
            return;

        string stringValue = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText()
        };

        if (string.IsNullOrWhiteSpace(stringValue))
            return;

        string exposedUnit = GetExposedUnit(exposedUnits, propertyName) ?? GetDefaultSourceUnit(propertyName);
        if (category == DeviceDataCategory.SensorReading
            && value.TryGetDecimal(out decimal numericValue)
            && TryConvertToCanonicalUnit(propertyName, numericValue, exposedUnit, valueUnit, out decimal convertedValue))
        {
            stringValue = convertedValue.ToString("0.############################", CultureInfo.InvariantCulture);
        }
        else if (!string.IsNullOrWhiteSpace(exposedUnit))
        {
            valueUnit = exposedUnit;
        }

        changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
        {
            Name = dataName,
            Value = stringValue,
            Category = category,
            StandardDataType = dataType,
            ValueUnit = valueUnit,
            IsMainData = isMainData
        });
    }

    private static string GetExposedUnit(Dictionary<string, string> exposedUnits, string propertyName)
    {
        if (exposedUnits == null || string.IsNullOrWhiteSpace(propertyName))
            return null;

        if (exposedUnits.TryGetValue(propertyName, out string unit))
            return unit;

        return string.Equals(propertyName, "illuminance_lux", StringComparison.OrdinalIgnoreCase)
            ? exposedUnits.GetValueOrDefault("illuminance")
            : null;
    }

    private static string GetDefaultSourceUnit(string propertyName)
    {
        return propertyName.ToLowerInvariant() switch
        {
            "temperature" => "C",
            "humidity" or "battery" or "soil_moisture" => "%",
            "pressure" => "hPa",
            "illuminance" or "illuminance_lux" => "lx",
            "co2" => "ppm",
            "noise" => "dB",
            "power" => "W",
            "energy" => "kWh",
            _ => null
        };
    }

    private static Dictionary<string, string> GetExposedUnits(string configurationData)
    {
        Dictionary<string, string> exposedUnits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(configurationData))
            return exposedUnits;

        try
        {
            using JsonDocument document = JsonDocument.Parse(configurationData);
            AddExposedUnits(document.RootElement, exposedUnits);
        }
        catch (JsonException)
        {
        }

        return exposedUnits;
    }

    private static void AddExposedUnits(JsonElement element, Dictionary<string, string> exposedUnits)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                AddExposedUnits(item, exposedUnits);

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("property", out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && element.TryGetProperty("unit", out JsonElement unit)
            && unit.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString())
            && !string.IsNullOrWhiteSpace(unit.GetString()))
        {
            exposedUnits[property.GetString()] = unit.GetString();
        }

        if (element.TryGetProperty("definition", out JsonElement definition))
            AddExposedUnits(definition, exposedUnits);
        if (element.TryGetProperty("exposes", out JsonElement exposes))
            AddExposedUnits(exposes, exposedUnits);
        if (element.TryGetProperty("features", out JsonElement features))
            AddExposedUnits(features, exposedUnits);
    }

    private static bool TryConvertToCanonicalUnit(string propertyName, decimal value, string sourceUnit, string targetUnit, out decimal convertedValue)
    {
        convertedValue = value;
        string normalizedSourceUnit = NormalizeUnit(sourceUnit);
        string normalizedTargetUnit = NormalizeUnit(targetUnit);
        if (string.IsNullOrWhiteSpace(normalizedSourceUnit) || string.IsNullOrWhiteSpace(normalizedTargetUnit))
            return false;
        if (string.Equals(normalizedSourceUnit, normalizedTargetUnit, StringComparison.Ordinal))
            return true;

        if (string.Equals(propertyName, "temperature", StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedSourceUnit == "f")
            {
                convertedValue = (value - 32M) * 5M / 9M;
                return true;
            }
            if (normalizedSourceUnit == "k")
            {
                convertedValue = value - 273.15M;
                return true;
            }
        }

        if (string.Equals(propertyName, "pressure", StringComparison.OrdinalIgnoreCase))
        {
            convertedValue = normalizedSourceUnit switch
            {
                "hpa" or "mbar" => value * 100M,
                "kpa" => value * 1_000M,
                "bar" => value * 100_000M,
                _ => value
            };
            return normalizedSourceUnit is "hpa" or "mbar" or "kpa" or "bar";
        }

        if (string.Equals(propertyName, "power", StringComparison.OrdinalIgnoreCase))
        {
            convertedValue = normalizedSourceUnit switch
            {
                "mw" => value / 1_000M,
                "kw" => value * 1_000M,
                _ => value
            };
            return normalizedSourceUnit is "mw" or "kw";
        }

        if (string.Equals(propertyName, "energy", StringComparison.OrdinalIgnoreCase))
        {
            convertedValue = normalizedSourceUnit switch
            {
                "wh" => value / 1_000M,
                "mwh" => value * 1_000M,
                "j" => value / 3_600_000M,
                _ => value
            };
            return normalizedSourceUnit is "wh" or "mwh" or "j";
        }

        if (propertyName is "voc" or "pm25" or "pm10" or "formaldehyde")
        {
            if (normalizedSourceUnit == "mg/m3")
            {
                convertedValue = value * 1_000M;
                return true;
            }
        }

        return false;
    }

    private static string NormalizeUnit(string unit)
    {
        return string.IsNullOrWhiteSpace(unit)
            ? null
            : unit.Trim().ToLowerInvariant()
                .Replace("\u00B5", "u", StringComparison.Ordinal)
                .Replace("\u03BC", "u", StringComparison.Ordinal)
                .Replace("\u00B3", "3", StringComparison.Ordinal)
                .Replace("deg", string.Empty, StringComparison.Ordinal)
                .Replace("°", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static async Task HandleBridgeDevicesAsync(JsonElement devices, CancellationToken cancellationToken)
    {
        if (devices.ValueKind != JsonValueKind.Array)
            return;

        List<DiscoveredDevice> discoveredDevices = [];
        foreach (JsonElement device in devices.EnumerateArray())
        {
            if (!device.TryGetProperty("friendly_name", out JsonElement friendlyName) || friendlyName.ValueKind != JsonValueKind.String)
                continue;
            if (device.TryGetProperty("type", out JsonElement type) && string.Equals(type.GetString(), "coordinator", StringComparison.OrdinalIgnoreCase))
                continue;

            string deviceName = friendlyName.GetString();
            if (string.IsNullOrWhiteSpace(deviceName))
                continue;

            List<string> roles = GetRoles(device);
            List<string> capabilities = GetCapabilities(device);
            List<DeviceAvailableAction> availableActions = GetAvailableActions(device);
            if (roles.Count == 0)
                roles.Add(Device.HomeAutomationMainRoleSensors);

            discoveredDevices.Add(new DiscoveredDevice()
            {
                DeviceInternalName = deviceName,
                DeviceAgentId = "sarah",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = roles,
                DeviceCapabilities = capabilities,
                AvailableActions = availableActions,
                DefaultConfigurationData = device.GetRawText()
            });
        }

        if (discoveredDevices.Count > 0)
            await new DiscoveredDeviceLogic().UpsertManyAsync(discoveredDevices, cancellationToken);
    }

    private static List<string> GetRoles(JsonElement device)
    {
        List<string> roles = [];
        List<string> properties = GetExposedProperties(device);
        if (properties.Contains("state", StringComparer.OrdinalIgnoreCase))
            roles.Add(Device.HomeAutomationRoleSwitch);
        if (properties.Contains("brightness", StringComparer.OrdinalIgnoreCase))
            roles.Add(Device.HomeAutomationRoleDimmer);
        if (properties.Contains("action", StringComparer.OrdinalIgnoreCase))
            roles.Add(Device.HomeAutomationRoleActionnable);
        if (GetCapabilities(device).Count > 0)
            roles.Add(Device.HomeAutomationRoleColorBound);

        return roles;
    }

    private static List<DeviceAvailableAction> GetAvailableActions(JsonElement device)
    {
        List<DeviceAvailableAction> actions = [];
        AddAvailableActions(device, actions);
        return actions;
    }

    private static void AddAvailableActions(JsonElement element, List<DeviceAvailableAction> actions)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                AddAvailableActions(item, actions);

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("property", out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && string.Equals(property.GetString(), "action", StringComparison.OrdinalIgnoreCase)
            && element.TryGetProperty("values", out JsonElement values)
            && values.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    continue;

                string rawAction = value.GetString().Trim();
                if (!actions.Exists(action => string.Equals(action.RawAction, rawAction, StringComparison.OrdinalIgnoreCase)))
                    actions.Add(NormalizeAvailableAction(rawAction));
            }
        }

        if (element.TryGetProperty("definition", out JsonElement definition))
            AddAvailableActions(definition, actions);
        if (element.TryGetProperty("exposes", out JsonElement exposes))
            AddAvailableActions(exposes, actions);
        if (element.TryGetProperty("features", out JsonElement features))
            AddAvailableActions(features, actions);
    }

    private static DeviceAvailableAction NormalizeAvailableAction(string rawAction)
    {
        DeviceAvailableAction action = new DeviceAvailableAction()
        {
            ActionKind = "button",
            Action = rawAction,
            RawAction = rawAction
        };

        if (rawAction.Contains("rotate_left", StringComparison.OrdinalIgnoreCase))
        {
            action.ActionKind = "rotary";
            action.Action = "rotate";
            action.Attributes["direction"] = "left";
            action.Attributes["delta"] = "-1";
        }
        else if (rawAction.Contains("rotate_right", StringComparison.OrdinalIgnoreCase))
        {
            action.ActionKind = "rotary";
            action.Action = "rotate";
            action.Attributes["direction"] = "right";
            action.Attributes["delta"] = "1";
        }
        else if (string.Equals(rawAction, "brightness_move_up", StringComparison.OrdinalIgnoreCase))
        {
            action.ActionKind = "continuous";
            action.Action = "brightness_move";
            action.Attributes["direction"] = "up";
            action.Attributes["delta"] = "1";
        }
        else if (string.Equals(rawAction, "brightness_move_down", StringComparison.OrdinalIgnoreCase))
        {
            action.ActionKind = "continuous";
            action.Action = "brightness_move";
            action.Attributes["direction"] = "down";
            action.Attributes["delta"] = "-1";
        }
        else if (string.Equals(rawAction, "brightness_stop", StringComparison.OrdinalIgnoreCase))
        {
            action.ActionKind = "continuous";
            action.Action = "brightness_stop";
        }

        return action;
    }

    private static List<string> GetCapabilities(JsonElement device)
    {
        List<string> properties = GetExposedProperties(device);
        List<string> capabilities = [];
        if (properties.Contains("color_xy", StringComparer.OrdinalIgnoreCase)
            || (properties.Contains("x", StringComparer.OrdinalIgnoreCase) && properties.Contains("y", StringComparer.OrdinalIgnoreCase)))
        {
            capabilities.Add(Device.CapabilityColorXy);
        }
        if (properties.Contains("color_hs", StringComparer.OrdinalIgnoreCase)
            || (properties.Contains("hue", StringComparer.OrdinalIgnoreCase) && properties.Contains("saturation", StringComparer.OrdinalIgnoreCase)))
        {
            capabilities.Add(Device.CapabilityColorHs);
        }
        if (properties.Contains("color_temp", StringComparer.OrdinalIgnoreCase))
            capabilities.Add(Device.CapabilityColorTemperature);

        return capabilities;
    }

    private static List<string> GetExposedProperties(JsonElement device)
    {
        List<string> properties = [];
        if (!device.TryGetProperty("definition", out JsonElement definition) || !definition.TryGetProperty("exposes", out JsonElement exposes) || exposes.ValueKind != JsonValueKind.Array)
            return properties;

        foreach (JsonElement expose in exposes.EnumerateArray())
        {
            AddExposedProperties(expose, properties);
        }

        return properties;
    }

    private static void AddExposedProperties(JsonElement expose, List<string> properties)
    {
        if (expose.TryGetProperty("property", out JsonElement property) && property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString()))
            properties.Add(property.GetString());

        if (!expose.TryGetProperty("features", out JsonElement features) || features.ValueKind != JsonValueKind.Array)
            return;

        foreach (JsonElement feature in features.EnumerateArray())
        {
            AddExposedProperties(feature, properties);
        }
    }

    private static bool IsBridgeDevicesTopic(string topic)
    {
        return string.Equals(topic, string.Concat(GetTopicRoot(), "/bridge/devices"), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetDeviceId(string topic, out string deviceId)
    {
        deviceId = null;
        string topicRoot = GetTopicRoot();
        string prefix = string.Concat(topicRoot, "/");
        if (string.IsNullOrWhiteSpace(topic) || !topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string relativeTopic = topic.Substring(prefix.Length);
        if (relativeTopic.StartsWith("bridge/", StringComparison.OrdinalIgnoreCase) || relativeTopic.Contains('/') || string.Equals(relativeTopic, "bridge", StringComparison.OrdinalIgnoreCase))
            return false;

        deviceId = relativeTopic.Trim();
        return !string.IsNullOrWhiteSpace(deviceId);
    }

    private static bool TryGetAvailabilityDeviceId(string topic, out string deviceId)
    {
        deviceId = null;
        string prefix = string.Concat(GetTopicRoot(), "/");
        if (string.IsNullOrWhiteSpace(topic) || !topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        const string availabilitySuffix = "/availability";
        string relativeTopic = topic.Substring(prefix.Length);
        if (!relativeTopic.EndsWith(availabilitySuffix, StringComparison.OrdinalIgnoreCase))
            return false;

        deviceId = relativeTopic.Substring(0, relativeTopic.Length - availabilitySuffix.Length).Trim();
        return !string.IsNullOrWhiteSpace(deviceId) && !deviceId.Contains('/');
    }

    private static string GetTopicRoot()
    {
        string configuredRoot = Environment.GetEnvironmentVariable("ZIGBEE2MQTT_TOPIC");
        return string.IsNullOrWhiteSpace(configuredRoot) ? "zigbee2mqtt" : configuredRoot.Trim().Trim('/');
    }

    private static (string host, int port) ResolveMqttEndpoint()
    {
        string host = Environment.GetEnvironmentVariable("MQTT_SERVICE_HOST");
        if (string.IsNullOrWhiteSpace(host))
            host = "localhost";

        string portValue = Environment.GetEnvironmentVariable("MQTT_SERVICE_PORT");
        return (host, int.TryParse(portValue, out int port) ? port : 1883);
    }
}