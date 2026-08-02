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
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah;

/*
| Famille Gen2+ | Identification | Gestion actuelle |
|---|---|---|
| Relais Gen2+ | Composants switch:<id> | Decouverte, etat on/off, puissance et energie |
| Volets Gen2+ | Composants cover:<id> | Decouverte, ouvrir/fermer/stop, position calibree, puissance et energie |
| Variateurs Gen2+ | Composants light:<id> | Decouverte, etat on/off, luminosite, puissance et energie |
| Eclairages RGB Gen2+ | Composants rgb:<id> | Decouverte, etat on/off, luminosite, couleur RGB, puissance et energie |
| Capteurs environnementaux Gen2+ | Composants temperature:<id>, humidity:<id>, illuminance:<id> | Decouverte, temperature, humidite relative et luminosite |
| Mesure electrique Gen2+ | Composants em:<id>, em1:<id>, em1data:<id> | Puissance active et energie cumulee |
| Securite et alimentation Gen2+ | Composants battery:<id>, flood:<id>, smoke:<id> | Niveau de batterie, fuite d'eau et fumee |
| Presence et mouvement Gen2+ | Composants presence:<id>, motion:<id> | Detection d'occupation |
| Configuration MQTT | Mqtt.GetConfig et Mqtt.SetConfig | Broker, activation et topic_prefix unique par appareil, puis redemarrage |
| Appareils proteges | Challenge HTTP Digest SHA-256 | Rejeu authentifie des appels RPC avec l'utilisateur sarah par defaut |
| Entrees Gen2+ | Composants input:<id> et notifications events/rpc | Decouverte et actions bouton simple/double/triple/appui long |
| Autres composants | autres capteurs | A venir |
*/
public sealed class ShellyGen2RuntimeService : BackgroundService
{
    private const string Platform = "shelly-gen2";
    private readonly ILogger<ShellyGen2RuntimeService> _logger;
    private readonly HttpClient _httpClient;

    public ShellyGen2RuntimeService(ILogger<ShellyGen2RuntimeService> logger, HttpClient httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        (string host, int port) = ResolveMqttEndpoint();
        MqttFactory factory = new MqttFactory();
        using IMqttClient client = factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += args => HandleMessageAsync(args.ApplicationMessage.Topic, Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()), stoppingToken);

        try
        {
            await client.ConnectAsync(new MqttClientOptionsBuilder()
                .WithClientId("manoir-sarah-shelly-gen2")
                .WithTcpServer(host, port)
                .Build(), stoppingToken);
            await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(string.Concat(GetTopicRoot(), "/#"))
                .Build(), stoppingToken);
            _logger.LogInformation("Subscribed to Shelly Gen2+ topic root {TopicRoot} at {Host}:{Port}.", GetTopicRoot(), host, port);

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
        if (string.IsNullOrWhiteSpace(topic) || payload == null)
        {
            return;
        }

        if (TryGetEventTopic(topic, out string eventDeviceInternalName))
        {
            await HandleEventMessageAsync(eventDeviceInternalName, payload, cancellationToken);
            return;
        }

        if (!TryGetStatusTopic(topic, out string deviceInternalName, out string componentType, out int componentIndex))
            return;

        DeviceLogic deviceLogic = new DeviceLogic();
        Device device = await deviceLogic.GetByInternalNameAndPlatformAsync(deviceInternalName, Platform, cancellationToken);
        if (device == null)
            return;

        if (string.Equals(componentType, "switch", StringComparison.OrdinalIgnoreCase))
            await HandleSwitchStatusAsync(deviceLogic, device, componentIndex, payload, cancellationToken);
        else if (string.Equals(componentType, "cover", StringComparison.OrdinalIgnoreCase))
            await HandleCoverStatusAsync(deviceLogic, device, componentIndex, payload, cancellationToken);
        else if (string.Equals(componentType, "light", StringComparison.OrdinalIgnoreCase))
            await HandleLightStatusAsync(deviceLogic, device, componentIndex, payload, cancellationToken);
        else if (string.Equals(componentType, "rgb", StringComparison.OrdinalIgnoreCase))
            await HandleRgbStatusAsync(deviceLogic, device, componentIndex, payload, cancellationToken);
        else if (string.Equals(componentType, "temperature", StringComparison.OrdinalIgnoreCase))
            await HandleSensorStatusAsync(deviceLogic, device, componentIndex, payload, "tC", "Temperature", DeviceData.DataTypeSensorTemperature, "C", cancellationToken);
        else if (string.Equals(componentType, "humidity", StringComparison.OrdinalIgnoreCase))
            await HandleSensorStatusAsync(deviceLogic, device, componentIndex, payload, "rh", "Humidity", DeviceData.DataTypeSensorHumidity, "%", cancellationToken);
        else if (string.Equals(componentType, "illuminance", StringComparison.OrdinalIgnoreCase))
            await HandleSensorStatusAsync(deviceLogic, device, componentIndex, payload, "lux", "Illuminance", DeviceData.DataTypeSensorIlluminance, "lx", cancellationToken);
        else if (string.Equals(componentType, "input", StringComparison.OrdinalIgnoreCase))
            await HandleInputStatusAsync(deviceLogic, device, componentIndex, payload, cancellationToken);
        else if (string.Equals(componentType, "em1", StringComparison.OrdinalIgnoreCase))
            await HandleElectricalStatusAsync(deviceLogic, device, componentIndex, payload, ["act_power"], cancellationToken);
        else if (string.Equals(componentType, "em", StringComparison.OrdinalIgnoreCase))
            await HandleElectricalStatusAsync(deviceLogic, device, componentIndex, payload, ["total_act_power", "a_act_power", "b_act_power", "c_act_power"], cancellationToken);
        else if (string.Equals(componentType, "em1data", StringComparison.OrdinalIgnoreCase))
            await HandleElectricalEnergyStatusAsync(deviceLogic, device, componentIndex, payload, cancellationToken);
        else if (string.Equals(componentType, "battery", StringComparison.OrdinalIgnoreCase))
            await HandleBatteryStatusAsync(deviceLogic, device, componentIndex, payload, cancellationToken);
        else if (string.Equals(componentType, "flood", StringComparison.OrdinalIgnoreCase))
            await HandleAlarmStatusAsync(deviceLogic, device, componentIndex, payload, "Flood", DeviceData.DataTypeWaterLeak, cancellationToken);
        else if (string.Equals(componentType, "smoke", StringComparison.OrdinalIgnoreCase))
            await HandleAlarmStatusAsync(deviceLogic, device, componentIndex, payload, "Smoke", DeviceData.DataTypeSmoke, cancellationToken);
        else if (string.Equals(componentType, "motion", StringComparison.OrdinalIgnoreCase))
            await HandleOccupancyStatusAsync(deviceLogic, device, componentIndex, payload, "Motion", cancellationToken);
        else if (string.Equals(componentType, "presence", StringComparison.OrdinalIgnoreCase))
            await HandleOccupancyStatusAsync(deviceLogic, device, componentIndex, payload, "Presence", cancellationToken);
    }

    private static async Task HandleEventMessageAsync(string deviceInternalName, string payload, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("method", out JsonElement method)
                || !string.Equals(method.GetString(), "NotifyEvent", StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("params", out JsonElement parameters)
                || !parameters.TryGetProperty("events", out JsonElement events)
                || events.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            Device device = await new DeviceLogic().GetByInternalNameAndPlatformAsync(deviceInternalName, Platform, cancellationToken);
            if (device == null)
                return;

            foreach (JsonElement eventValue in events.EnumerateArray())
                PublishInputEvent(device, eventValue);
        }
        catch (JsonException)
        {
        }
    }

    private static void PublishInputEvent(Device device, JsonElement eventValue)
    {
        if (!eventValue.TryGetProperty("component", out JsonElement component)
            || component.ValueKind != JsonValueKind.String
            || !TryParseComponent(component.GetString(), "input", out int inputIndex)
            || !eventValue.TryGetProperty("event", out JsonElement rawActionValue)
            || rawActionValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(rawActionValue.GetString()))
        {
            return;
        }

        string rawAction = rawActionValue.GetString().Trim();
        if (rawAction is not ("single_push" or "double_push" or "triple_push" or "long_push"))
            return;

        NatsInterprocess.Push(new DeviceActionTriggeredMessage()
        {
            DeviceId = device.Id,
            DeviceInternalName = device.DeviceInternalName,
            DevicePlatform = Platform,
            ActionKind = "button",
            Action = rawAction,
            RawAction = rawAction,
            Attributes = new Dictionary<string, string>() { ["input"] = inputIndex.ToString(CultureInfo.InvariantCulture) }
        });
    }

    public async Task<bool> OnboardAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(string.Concat("http://", ipAddress?.Trim().TrimEnd('/'), "/"), UriKind.Absolute, out Uri deviceAddress)
            || !TryGetMqttServer(out string mqttServer))
        {
            return false;
        }

        try
        {
            using JsonDocument deviceInfo = await CallRpcAsync(deviceAddress, "Shelly.GetDeviceInfo", null, cancellationToken);
            JsonElement deviceInfoResult = GetResult(deviceInfo.RootElement);
            if (!deviceInfoResult.TryGetProperty("gen", out JsonElement generation)
                || !generation.TryGetInt32(out int generationNumber)
                || generationNumber < 2
                || !deviceInfoResult.TryGetProperty("id", out JsonElement id)
                || id.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(id.GetString()))
            {
                return false;
            }

            string deviceInternalName = id.GetString().Trim();
            string topicPrefix = GetTopicPrefix(deviceInternalName);
            using JsonDocument mqttConfig = await CallRpcAsync(deviceAddress, "Mqtt.GetConfig", null, cancellationToken);
            JsonElement mqttConfigResult = GetResult(mqttConfig.RootElement);
            using JsonDocument deviceStatus = await CallRpcAsync(deviceAddress, "Shelly.GetStatus", null, cancellationToken);
            await DiscoverDeviceAsync(deviceInternalName, new { ip = deviceAddress.Host, app = GetApplication(deviceInfoResult), topicPrefix }, cancellationToken, GetResult(deviceStatus.RootElement));
            if (IsMqttConfigured(mqttConfigResult, mqttServer, topicPrefix))
                return true;

            using JsonDocument ignored = await CallRpcAsync(deviceAddress, "Mqtt.SetConfig", new { config = new { enable = true, server = mqttServer, topic_prefix = topicPrefix } }, cancellationToken);
            using JsonDocument reboot = await CallRpcAsync(deviceAddress, "Shelly.Reboot", null, cancellationToken);
            _logger.LogInformation("Configured MQTT for Shelly Gen{Generation} device at {Address}.", generationNumber, deviceAddress);
            return true;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogDebug(exception, "Unable to auto-configure Shelly Gen2+ device at {Address}.", deviceAddress);
            return false;
        }
        catch (JsonException exception)
        {
            _logger.LogDebug(exception, "Shelly Gen2+ device at {Address} returned an invalid RPC response.", deviceAddress);
            return false;
        }
    }

    private static async Task HandleSwitchStatusAsync(DeviceLogic deviceLogic, Device device, int switchIndex, string payload, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            List<DeviceStateChangedMessage.DeviceStateValue> changes = [];
            string switchName = GetSwitchName(switchIndex);
            if (status.TryGetProperty("output", out JsonElement output) && output.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = switchName,
                    Value = output.GetBoolean() ? "on" : "off",
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeSwitch,
                    IsMainData = switchIndex == 0
                });
            }

            if (status.TryGetProperty("apower", out JsonElement activePower) && activePower.TryGetDecimal(out decimal activePowerValue))
                changes.Add(CreateMeasurement(string.Concat(switchName, " Power"), activePowerValue, DeviceData.DataTypePowerCurrentConsumption, "W"));
            if (TryGetEnergy(status, out decimal energyValue))
                changes.Add(CreateMeasurement(string.Concat(switchName, " Energy"), energyValue, DeviceData.DataTypePowerTotal, "Wh"));

            if (changes.Count > 0)
                await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationRoleSwitch, "online", changes, cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task HandleCoverStatusAsync(DeviceLogic deviceLogic, Device device, int coverIndex, string payload, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            string coverName = string.Concat("Cover ", coverIndex.ToString(CultureInfo.InvariantCulture));
            List<DeviceStateChangedMessage.DeviceStateValue> changes = [];
            if (status.TryGetProperty("state", out JsonElement state) && state.ValueKind == JsonValueKind.String
                && state.GetString() is string stateValue && stateValue is "open" or "close" or "stop")
            {
                changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = coverName,
                    Value = stateValue,
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeShutter,
                    IsMainData = coverIndex == 0
                });
            }

            bool positioning = status.TryGetProperty("positioning", out JsonElement positioningValue) && positioningValue.ValueKind == JsonValueKind.True;
            await deviceLogic.SetCapabilityAsync(device.Id, Device.CapabilityShutterPosition, positioning, cancellationToken);
            if (positioning && status.TryGetProperty("current_pos", out JsonElement position) && position.TryGetDecimal(out decimal positionValue) && positionValue is >= 0M and <= 100M)
            {
                changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = string.Concat(coverName, " Position"),
                    Value = positionValue.ToString("0.############################", CultureInfo.InvariantCulture),
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeGradient,
                    ValueUnit = "%"
                });
            }

            AddMeasurements(status, changes, coverName);
            if (changes.Count > 0)
                await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationMainRoleShutterSwitch, "online", changes, cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task HandleLightStatusAsync(DeviceLogic deviceLogic, Device device, int lightIndex, string payload, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            string lightName = string.Concat("Light ", lightIndex.ToString(CultureInfo.InvariantCulture));
            List<DeviceStateChangedMessage.DeviceStateValue> changes = [];
            if (status.TryGetProperty("output", out JsonElement output) && output.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = lightName,
                    Value = output.GetBoolean() ? "on" : "off",
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeSwitch,
                    IsMainData = lightIndex == 0
                });
            }
            if (status.TryGetProperty("brightness", out JsonElement brightness) && brightness.TryGetDecimal(out decimal brightnessValue))
            {
                changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = string.Concat(lightName, " Brightness"),
                    Value = brightnessValue.ToString("0.############################", CultureInfo.InvariantCulture),
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeGradient,
                    ValueUnit = "%",
                    IsMainData = lightIndex == 0
                });
            }

            AddMeasurements(status, changes, lightName);
            if (changes.Count > 0)
                await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationRoleDimmer, "online", changes, cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task HandleRgbStatusAsync(DeviceLogic deviceLogic, Device device, int rgbIndex, string payload, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            string rgbName = string.Concat("RGB ", rgbIndex.ToString(CultureInfo.InvariantCulture));
            List<DeviceStateChangedMessage.DeviceStateValue> changes = [];
            if (status.TryGetProperty("output", out JsonElement output) && output.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = rgbName,
                    Value = output.GetBoolean() ? "on" : "off",
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeSwitch,
                    IsMainData = rgbIndex == 0
                });
            }
            if (status.TryGetProperty("brightness", out JsonElement brightness) && brightness.TryGetDecimal(out decimal brightnessValue))
            {
                changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = string.Concat(rgbName, " Brightness"),
                    Value = brightnessValue.ToString("0.############################", CultureInfo.InvariantCulture),
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeGradient,
                    ValueUnit = "%",
                    IsMainData = rgbIndex == 0
                });
            }
            if (TryGetRgbColor(status, out string color))
            {
                changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = string.Concat(rgbName, " Color"),
                    Value = color,
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeColor
                });
            }

            AddMeasurements(status, changes, rgbName);
            if (changes.Count > 0)
                await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationRoleColorBound, "online", changes, cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task HandleInputStatusAsync(DeviceLogic deviceLogic, Device device, int inputIndex, string payload, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            if (!status.TryGetProperty("state", out JsonElement state) || state.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return;

            await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationRoleActionnable, "online",
            [
                new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = string.Concat("Input ", inputIndex.ToString(CultureInfo.InvariantCulture)),
                    Value = state.GetBoolean() ? "true" : "false",
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeContact,
                    IsMainData = inputIndex == 0
                }
            ], cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task HandleElectricalStatusAsync(DeviceLogic deviceLogic, Device device, int meterIndex, string payload, string[] powerProperties, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            List<DeviceStateChangedMessage.DeviceStateValue> changes = [];
            string meterName = string.Concat("EM ", meterIndex.ToString(CultureInfo.InvariantCulture));
            foreach (string property in powerProperties)
            {
                if (!status.TryGetProperty(property, out JsonElement power) || !power.TryGetDecimal(out decimal powerValue))
                    continue;

                string name = property switch
                {
                    "total_act_power" or "act_power" => string.Concat(meterName, " Power"),
                    "a_act_power" => string.Concat(meterName, " Phase A Power"),
                    "b_act_power" => string.Concat(meterName, " Phase B Power"),
                    _ => string.Concat(meterName, " Phase C Power")
                };
                changes.Add(CreateMeasurement(name, powerValue, DeviceData.DataTypePowerCurrentConsumption, "W"));
            }

            if (changes.Count > 0)
                await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationMainRoleSensors, "online", changes, cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task HandleElectricalEnergyStatusAsync(DeviceLogic deviceLogic, Device device, int meterIndex, string payload, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            if (!status.TryGetProperty("total_act_energy", out JsonElement energy) || !energy.TryGetDecimal(out decimal energyValue))
                return;

            string meterName = string.Concat("EM ", meterIndex.ToString(CultureInfo.InvariantCulture));
            await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationMainRoleSensors, "online",
            [CreateMeasurement(string.Concat(meterName, " Energy"), energyValue, DeviceData.DataTypePowerTotal, "Wh")], cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task HandleBatteryStatusAsync(DeviceLogic deviceLogic, Device device, int batteryIndex, string payload, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            if (!status.TryGetProperty("percent", out JsonElement percent) || !percent.TryGetDecimal(out decimal batteryPercentage))
                return;

            await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationMainRoleSensors, "online",
            [
                new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = string.Concat("Battery ", batteryIndex.ToString(CultureInfo.InvariantCulture)),
                    Value = batteryPercentage.ToString("0.############################", CultureInfo.InvariantCulture),
                    Category = DeviceDataCategory.DeviceHealth,
                    StandardDataType = DeviceData.DataTypeBatteryPercentage,
                    ValueUnit = "%",
                    IsMainData = batteryIndex == 0
                }
            ], cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task HandleAlarmStatusAsync(DeviceLogic deviceLogic, Device device, int sensorIndex, string payload, string sensorName, string dataType, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            if (!status.TryGetProperty("alarm", out JsonElement alarm) || alarm.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return;

            string name = sensorIndex == 0 ? sensorName : string.Concat(sensorName, " ", sensorIndex.ToString(CultureInfo.InvariantCulture));
            await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationMainRoleSensors, "online",
            [
                new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = name,
                    Value = alarm.GetBoolean() ? "true" : "false",
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = dataType,
                    IsMainData = sensorIndex == 0
                }
            ], cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task HandleOccupancyStatusAsync(DeviceLogic deviceLogic, Device device, int sensorIndex, string payload, string sensorName, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            bool isOccupied;
            if (!TryGetBooleanProperty(status, ["motion", "presence", "detected", "active", "occupied"], out isOccupied))
                isOccupied = status.TryGetProperty("live_track", out JsonElement liveTrack) && liveTrack.ValueKind == JsonValueKind.Object;

            string name = sensorIndex == 0 ? sensorName : string.Concat(sensorName, " ", sensorIndex.ToString(CultureInfo.InvariantCulture));
            await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationMainRoleSensors, "online",
            [
                new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = name,
                    Value = isOccupied ? "true" : "false",
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeOccupancy,
                    IsMainData = sensorIndex == 0
                }
            ], cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static bool TryGetBooleanProperty(JsonElement status, string[] propertyNames, out bool value)
    {
        foreach (string propertyName in propertyNames)
        {
            if (status.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }
        }

        value = false;
        return false;
    }

    private static bool TryGetRgbColor(JsonElement status, out string color)
    {
        color = null;
        if (!status.TryGetProperty("rgb", out JsonElement rgb)
            || rgb.ValueKind != JsonValueKind.Array
            || rgb.GetArrayLength() != 3
            || !rgb[0].TryGetByte(out byte red)
            || !rgb[1].TryGetByte(out byte green)
            || !rgb[2].TryGetByte(out byte blue))
        {
            return false;
        }

        color = string.Concat("#", red.ToString("X2", CultureInfo.InvariantCulture), green.ToString("X2", CultureInfo.InvariantCulture), blue.ToString("X2", CultureInfo.InvariantCulture));
        return true;
    }

    private static async Task HandleSensorStatusAsync(DeviceLogic deviceLogic, Device device, int sensorIndex, string payload, string propertyName, string sensorName, string dataType, string unit, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            if (!status.TryGetProperty(propertyName, out JsonElement value) || !value.TryGetDecimal(out decimal sensorValue))
                return;

            string name = sensorIndex == 0 ? sensorName : string.Concat(sensorName, " ", sensorIndex.ToString(CultureInfo.InvariantCulture));
            await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationMainRoleSensors, "online",
            [
                new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = name,
                    Value = sensorValue.ToString("0.############################", CultureInfo.InvariantCulture),
                    Category = DeviceDataCategory.SensorReading,
                    StandardDataType = dataType,
                    ValueUnit = unit,
                    IsMainData = sensorIndex == 0
                }
            ], cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static void AddMeasurements(JsonElement status, List<DeviceStateChangedMessage.DeviceStateValue> changes, string componentName)
    {
        if (status.TryGetProperty("apower", out JsonElement activePower) && activePower.TryGetDecimal(out decimal activePowerValue))
            changes.Add(CreateMeasurement(string.Concat(componentName, " Power"), activePowerValue, DeviceData.DataTypePowerCurrentConsumption, "W"));
        if (TryGetEnergy(status, out decimal energyValue))
            changes.Add(CreateMeasurement(string.Concat(componentName, " Energy"), energyValue, DeviceData.DataTypePowerTotal, "Wh"));
    }

    private static async Task DiscoverDeviceAsync(string deviceInternalName, object configuration, CancellationToken cancellationToken, JsonElement? status = null)
    {
        if (status is not JsonElement effectiveStatus)
            return;

        List<string> roles = GetRoles(effectiveStatus);
        if (roles.Count == 0)
            return;

        await new DiscoveredDeviceLogic().UpsertAsync(new DiscoveredDevice()
        {
            DeviceInternalName = deviceInternalName,
            DeviceAgentId = "sarah",
            DevicePlatform = Platform,
            DeviceKind = Device.DeviceKindHomeAutomation,
            DeviceRoles = roles,
            AvailableActions = roles.Contains(Device.HomeAutomationRoleActionnable, StringComparer.OrdinalIgnoreCase) ? GetInputActions() : [],
            DefaultConfigurationData = configuration == null ? null : JsonSerializer.Serialize(configuration)
        }, cancellationToken);
    }

    private static List<string> GetRoles(JsonElement status)
    {
        List<string> roles = [];
        if (status.ValueKind != JsonValueKind.Object)
            return roles;

        if (status.EnumerateObject().Any(property => TryParseComponent(property.Name, "switch", out _)))
            roles.Add(Device.HomeAutomationRoleSwitch);
        if (status.EnumerateObject().Any(property => TryParseComponent(property.Name, "cover", out _)))
            roles.Add(Device.HomeAutomationMainRoleShutterSwitch);
        if (status.EnumerateObject().Any(property => TryParseComponent(property.Name, "light", out _)))
        {
            roles.Add(Device.HomeAutomationRoleSwitch);
            roles.Add(Device.HomeAutomationRoleDimmer);
        }
        if (status.EnumerateObject().Any(property => TryParseComponent(property.Name, "rgb", out _)))
        {
            roles.Add(Device.HomeAutomationRoleSwitch);
            roles.Add(Device.HomeAutomationRoleDimmer);
            roles.Add(Device.HomeAutomationRoleColorBound);
        }
        if (status.EnumerateObject().Any(property => TryParseComponent(property.Name, "temperature", out _)
                                                      || TryParseComponent(property.Name, "humidity", out _)
                                                      || TryParseComponent(property.Name, "illuminance", out _)))
        {
            roles.Add(Device.HomeAutomationMainRoleSensors);
        }
        if (status.EnumerateObject().Any(property => TryParseComponent(property.Name, "input", out _)))
            roles.Add(Device.HomeAutomationRoleActionnable);
        if (status.EnumerateObject().Any(property => TryParseComponent(property.Name, "em", out _)
                                                      || TryParseComponent(property.Name, "em1", out _)
                                                      || TryParseComponent(property.Name, "em1data", out _)
                                                      || TryParseComponent(property.Name, "battery", out _)
                                                      || TryParseComponent(property.Name, "flood", out _)
                                                      || TryParseComponent(property.Name, "smoke", out _)
                                                      || TryParseComponent(property.Name, "motion", out _)
                                                      || TryParseComponent(property.Name, "presence", out _)))
        {
            roles.Add(Device.HomeAutomationMainRoleSensors);
        }

        return roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryGetStatusTopic(string topic, out string deviceInternalName, out string componentType, out int componentIndex)
    {
        deviceInternalName = null;
        componentType = null;
        componentIndex = 0;
        string root = string.Concat(GetTopicRoot(), "/");
        if (!topic.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;

        string[] segments = topic.Substring(root.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 3
            && !string.IsNullOrWhiteSpace(segments[0])
            && string.Equals(segments[1], "status", StringComparison.OrdinalIgnoreCase)
            && TryParseComponent(segments[2], out componentType, out componentIndex)
            && (deviceInternalName = segments[0]) != null;
    }

    private static bool TryParseComponent(string component, string expectedType, out int componentIndex)
    {
        componentIndex = 0;
        return component.StartsWith(string.Concat(expectedType, ":"), StringComparison.OrdinalIgnoreCase)
            && int.TryParse(component.Substring(expectedType.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out componentIndex)
            && componentIndex >= 0;
    }

    private static bool TryParseComponent(string component, out string componentType, out int componentIndex)
    {
        componentType = null;
        componentIndex = 0;
        foreach (string type in new[] { "switch", "cover", "light", "rgb", "temperature", "humidity", "illuminance", "input", "em", "em1", "em1data", "battery", "flood", "smoke", "motion", "presence" })
        {
            if (TryParseComponent(component, type, out componentIndex))
            {
                componentType = type;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetEventTopic(string topic, out string deviceInternalName)
    {
        deviceInternalName = null;
        string root = string.Concat(GetTopicRoot(), "/");
        if (!topic.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;

        string[] segments = topic.Substring(root.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 3
            && !string.IsNullOrWhiteSpace(segments[0])
            && string.Equals(segments[1], "events", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[2], "rpc", StringComparison.OrdinalIgnoreCase)
            && (deviceInternalName = segments[0]) != null;
    }

    private static List<DeviceAvailableAction> GetInputActions()
    {
        return
        [
            new DeviceAvailableAction() { RawAction = "single_push", Action = "single_push", ActionKind = "button" },
            new DeviceAvailableAction() { RawAction = "double_push", Action = "double_push", ActionKind = "button" },
            new DeviceAvailableAction() { RawAction = "triple_push", Action = "triple_push", ActionKind = "button" },
            new DeviceAvailableAction() { RawAction = "long_push", Action = "long_push", ActionKind = "button" }
        ];
    }

    private static bool TryGetEnergy(JsonElement status, out decimal energy)
    {
        energy = 0;
        if (!status.TryGetProperty("aenergy", out JsonElement activeEnergy))
            return false;

        return activeEnergy.ValueKind == JsonValueKind.Number && activeEnergy.TryGetDecimal(out energy)
            || (activeEnergy.ValueKind == JsonValueKind.Object
                && activeEnergy.TryGetProperty("total", out JsonElement total)
                && total.TryGetDecimal(out energy));
    }

    private static DeviceStateChangedMessage.DeviceStateValue CreateMeasurement(string name, decimal value, string dataType, string unit)
    {
        (decimal canonicalValue, string canonicalUnit) = MeasurementUnitNormalizer.ToCanonical(value, dataType, unit);
        return new DeviceStateChangedMessage.DeviceStateValue()
        {
            Name = name,
            Value = canonicalValue.ToString("0.############################", CultureInfo.InvariantCulture),
            Category = DeviceDataCategory.SensorReading,
            StandardDataType = dataType,
            ValueUnit = canonicalUnit
        };
    }

    private static string GetSwitchName(int switchIndex)
    {
        return switchIndex == 0 ? "Switch" : string.Concat("Switch ", switchIndex.ToString(CultureInfo.InvariantCulture));
    }

    private static string GetApplication(JsonElement deviceInfo)
    {
        return deviceInfo.TryGetProperty("app", out JsonElement application) && application.ValueKind == JsonValueKind.String
            ? application.GetString()
            : null;
    }

    private async Task<JsonDocument> CallRpcAsync(Uri deviceAddress, string method, object parameters, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendRpcAsync(deviceAddress, method, parameters, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized
            && TryGetDigestAuthentication(response, method, out DigestAuthentication authentication))
        {
            using HttpResponseMessage authenticatedResponse = await SendRpcAsync(deviceAddress, method, parameters, authentication, cancellationToken);
            authenticatedResponse.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await authenticatedResponse.Content.ReadAsStringAsync(cancellationToken));
        }

        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task<HttpResponseMessage> SendRpcAsync(Uri deviceAddress, string method, object parameters, DigestAuthentication authentication, CancellationToken cancellationToken)
    {
        string body = authentication == null
            ? JsonSerializer.Serialize(new { id = 1, method, @params = parameters })
            : JsonSerializer.Serialize(new { id = 1, method, @params = parameters, auth = authentication });
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(deviceAddress, "rpc"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static bool TryGetDigestAuthentication(HttpResponseMessage response, string method, out DigestAuthentication authentication)
    {
        authentication = null;
        string password = GetPassword();
        if (string.IsNullOrWhiteSpace(password)
            || !response.Headers.TryGetValues("WWW-Authenticate", out IEnumerable<string> values))
        {
            return false;
        }

        string challenge = values.FirstOrDefault(value => value.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(challenge)
            || !TryGetDigestChallengeValue(challenge, "realm", out string realm)
            || !TryGetDigestChallengeValue(challenge, "nonce", out string nonce))
        {
            return false;
        }

        string username = Environment.GetEnvironmentVariable("SHELLY_GEN2_USERNAME");
        if (string.IsNullOrWhiteSpace(username))
            username = "sarah";

        const string nonceCount = "00000001";
        int clientNonce = RandomNumberGenerator.GetInt32(int.MaxValue);
        string ha1 = ComputeSha256(string.Concat(username, ":", realm, ":", password));
        string ha2 = ComputeSha256(string.Concat("POST:/rpc"));
        string digestResponse = ComputeSha256(string.Concat(ha1, ":", nonce, ":", nonceCount, ":", clientNonce.ToString(CultureInfo.InvariantCulture), ":auth:", ha2));
        authentication = new DigestAuthentication(realm, username, nonce, clientNonce, nonceCount, digestResponse, "SHA-256");
        return true;
    }

    private static bool TryGetDigestChallengeValue(string challenge, string name, out string value)
    {
        Match match = Regex.Match(challenge, string.Concat("(?:^|,)\\s*", Regex.Escape(name), "=\\\"(?<value>[^\\\"]+)\\\""), RegexOptions.IgnoreCase);
        value = match.Success ? match.Groups["value"].Value : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static JsonElement GetResult(JsonElement response)
    {
        return response.TryGetProperty("result", out JsonElement result) && result.ValueKind == JsonValueKind.Object
            ? result
            : response;
    }

    private static bool IsMqttConfigured(JsonElement config, string mqttServer, string topicPrefix)
    {
        return config.TryGetProperty("enable", out JsonElement enabled)
            && enabled.ValueKind == JsonValueKind.True
            && config.TryGetProperty("server", out JsonElement server)
            && server.ValueKind == JsonValueKind.String
            && string.Equals(server.GetString(), mqttServer, StringComparison.OrdinalIgnoreCase)
            && config.TryGetProperty("topic_prefix", out JsonElement configuredTopicPrefix)
            && configuredTopicPrefix.ValueKind == JsonValueKind.String
            && string.Equals(configuredTopicPrefix.GetString(), topicPrefix, StringComparison.Ordinal);
    }

    private static string GetTopicRoot()
    {
        string configuredPrefix = Environment.GetEnvironmentVariable("SHELLY_GEN2_TOPIC");
        return string.IsNullOrWhiteSpace(configuredPrefix) ? "shelly" : configuredPrefix.Trim().Trim('/');
    }

    private static string GetTopicPrefix(string deviceInternalName)
    {
        return string.Concat(GetTopicRoot(), "/", deviceInternalName);
    }

    private static string GetPassword()
    {
        string password = Environment.GetEnvironmentVariable("SHELLY_GEN2_PASSWORD");
        return string.IsNullOrWhiteSpace(password) ? Environment.GetEnvironmentVariable("HOMEAUTOMATION_APIKEY") : password;
    }

    private static bool TryGetMqttServer(out string mqttServer)
    {
        mqttServer = null;
        string host = Environment.GetEnvironmentVariable("MQTT_SERVICE_HOST");
        if (string.IsNullOrWhiteSpace(host))
            return false;

        string port = Environment.GetEnvironmentVariable("MQTT_SERVICE_PORT");
        mqttServer = string.IsNullOrWhiteSpace(port) ? host.Trim() : string.Concat(host.Trim(), ":", port.Trim());
        return true;
    }

    private static (string Host, int Port) ResolveMqttEndpoint()
    {
        string host = Environment.GetEnvironmentVariable("MQTT_SERVICE_HOST");
        if (string.IsNullOrWhiteSpace(host))
            host = "localhost";

        string portValue = Environment.GetEnvironmentVariable("MQTT_SERVICE_PORT");
        int port = int.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out int configuredPort) ? configuredPort : 1883;
        return (host, port);
    }

    private sealed record DigestAuthentication(string realm, string username, string nonce, int cnonce, string nc, string response, string algorithm);
}