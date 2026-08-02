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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah;

/*
| Famille Gen1 | Identification | Gestion actuelle |
|---|---|---|
| Relais et prises | SHSW*, SHPLG*, SHEM*, SHUNI* | Decouverte, etat on/off, puissance, energie, disponibilite et commandes |
| Volets | SHSW-21*, SHSW-25* en mode roller | Decouverte, ouvrir/fermer/stop, puissance, energie et position si calibree |
| Variateurs et ampoules | SHDM*, SHBLB*, SHBDUO*, SHVIN*, SHCB*, SHRGBW* | Decouverte, etat, luminosite, couleur, puissance et energie |
| Boutons et entrees | SHBTN*, SHIX*; entrees des SHSW*, SHUNI*, SHRGBW*, SHDM* | Decouverte et actions simple/double/triple/appui long |
| Capteurs generiques | Autres modeles Gen1 annonces | Decouverte, temperature et humidite quand les topics MQTT sont publies |
| MQTT et HTTP | Tous les Gen1 compatibles /shelly | Souscription shellies/#, onboarding MQTT, Basic Auth et commandes HTTP |
*/
public sealed class ShellyGen1RuntimeService : BackgroundService
{
    private const string Platform = "shelly-gen1";
    private readonly ILogger<ShellyGen1RuntimeService> _logger;
    private readonly HttpClient _httpClient;

    public ShellyGen1RuntimeService(ILogger<ShellyGen1RuntimeService> logger, HttpClient httpClient = null)
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
                .WithClientId("manoir-sarah-shelly-gen1")
                .WithTcpServer(host, port)
                .Build(), stoppingToken);
            await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(string.Concat(GetTopicRoot(), "/#"))
                .Build(), stoppingToken);
            _logger.LogInformation("Subscribed to Shelly Gen1 topic root {TopicRoot} at {Host}:{Port}.", GetTopicRoot(), host, port);

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
        if (string.IsNullOrWhiteSpace(topic) || payload == null || !TryGetRelativeTopic(topic, out string relativeTopic))
            return;

        if (string.Equals(relativeTopic, "announce", StringComparison.OrdinalIgnoreCase))
        {
            await HandleAnnounceAsync(payload, cancellationToken);
            return;
        }

        int separatorIndex = relativeTopic.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == relativeTopic.Length - 1)
            return;

        string deviceInternalName = relativeTopic.Substring(0, separatorIndex);
        string property = relativeTopic.Substring(separatorIndex + 1);
        DeviceLogic deviceLogic = new DeviceLogic();
        Device device = await deviceLogic.GetByInternalNameAndPlatformAsync(deviceInternalName, Platform, cancellationToken);
        if (device == null)
            return;

        if (string.Equals(property, "online", StringComparison.OrdinalIgnoreCase))
        {
            string status = payload.Trim();
            if (string.Equals(status, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "false", StringComparison.OrdinalIgnoreCase))
                await deviceLogic.ChangeStatusAsync(Platform, device.Id, string.Equals(status, "true", StringComparison.OrdinalIgnoreCase) ? "online" : "offline", cancellationToken);
            return;
        }

        if (TryGetIndexedTopic(property, "relay", out int relayIndex, out string relayProperty))
        {
            await HandleRelayAsync(deviceLogic, device, relayIndex, relayProperty, payload, cancellationToken);
            return;
        }

        if (TryGetIndexedTopic(property, "roller", out int rollerIndex, out string rollerProperty))
        {
            await HandleRollerAsync(deviceLogic, device, rollerIndex, rollerProperty, payload, cancellationToken);
            return;
        }

        if (TryGetIndexedTopic(property, "light", out int lightIndex, out string lightProperty)
            || TryGetIndexedTopic(property, "white", out lightIndex, out lightProperty)
            || TryGetIndexedTopic(property, "color", out lightIndex, out lightProperty))
        {
            string outputKind = property.Substring(0, property.IndexOf('/'));
            await HandleLightAsync(deviceLogic, device, outputKind, lightIndex, lightProperty, payload, cancellationToken);
            return;
        }

        if (TryGetIndexedTopic(property, "emeter", out int meterIndex, out string meterProperty))
        {
            await HandleMeterAsync(deviceLogic, device, meterIndex, meterProperty, payload, cancellationToken);
            return;
        }

        if (string.Equals(property, "sensor/temperature", StringComparison.OrdinalIgnoreCase))
        {
            await UpdateSensorAsync(deviceLogic, device, "Temperature", payload, DeviceData.DataTypeSensorTemperature, "C", cancellationToken);
            return;
        }

        if (string.Equals(property, "sensor/humidity", StringComparison.OrdinalIgnoreCase))
        {
            await UpdateSensorAsync(deviceLogic, device, "Humidity", payload, DeviceData.DataTypeSensorHumidity, "%", cancellationToken);
            return;
        }

        if (TryGetIndexedTopic(property, "input_event", out int inputIndex, out string inputProperty) && string.IsNullOrEmpty(inputProperty))
            PublishInputEvent(device, inputIndex, payload);
    }

    private static async Task HandleRelayAsync(DeviceLogic deviceLogic, Device device, int relayIndex, string property, string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(property))
        {
            string state = payload.Trim();
            if (string.Equals(state, "on", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "off", StringComparison.OrdinalIgnoreCase))
            {
                await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationRoleSwitch, "online",
                    [CreateSwitchValue(GetRelayName(relayIndex), state, relayIndex == 0)], cancellationToken);
            }
            return;
        }

        if (string.Equals(property, "power", StringComparison.OrdinalIgnoreCase))
            await UpdateMeasurementAsync(deviceLogic, device, string.Concat(GetRelayName(relayIndex), " Power"), payload, DeviceData.DataTypePowerCurrentConsumption, "W", cancellationToken);
        else if (string.Equals(property, "energy", StringComparison.OrdinalIgnoreCase))
            await UpdateMeasurementAsync(deviceLogic, device, string.Concat(GetRelayName(relayIndex), " Energy"), payload, DeviceData.DataTypePowerTotal, "W-min", cancellationToken);
    }

    private static async Task HandleRollerAsync(DeviceLogic deviceLogic, Device device, int rollerIndex, string property, string payload, CancellationToken cancellationToken)
    {
        string rollerName = string.Concat("Cover ", rollerIndex.ToString(CultureInfo.InvariantCulture));
        if (string.IsNullOrEmpty(property))
        {
            string state = payload.Trim().ToLowerInvariant();
            if (state is "open" or "close" or "stop")
            {
                await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationMainRoleShutterSwitch, "online",
                [
                    new DeviceStateChangedMessage.DeviceStateValue()
                    {
                        Name = rollerName,
                        Value = state,
                        Category = DeviceDataCategory.DeviceState,
                        StandardDataType = DeviceData.DataTypeShutter,
                        IsMainData = rollerIndex == 0
                    }
                ], cancellationToken);
            }
            return;
        }

        if (string.Equals(property, "pos", StringComparison.OrdinalIgnoreCase))
        {
            if (decimal.TryParse(payload, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal position) && position is >= 0M and <= 100M)
            {
                await deviceLogic.SetCapabilityAsync(device.Id, Device.CapabilityShutterPosition, true, cancellationToken);
                await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationMainRoleShutterSwitch, "online",
                [CreateGradientValue(string.Concat(rollerName, " Position"), position, false)], cancellationToken);
            }
            else if (string.Equals(payload.Trim(), "-1", StringComparison.Ordinal))
            {
                await deviceLogic.SetCapabilityAsync(device.Id, Device.CapabilityShutterPosition, false, cancellationToken);
            }
            return;
        }

        if (string.Equals(property, "power", StringComparison.OrdinalIgnoreCase))
            await UpdateMeasurementAsync(deviceLogic, device, string.Concat(rollerName, " Power"), payload, DeviceData.DataTypePowerCurrentConsumption, "W", cancellationToken);
        else if (string.Equals(property, "energy", StringComparison.OrdinalIgnoreCase))
            await UpdateMeasurementAsync(deviceLogic, device, string.Concat(rollerName, " Energy"), payload, DeviceData.DataTypePowerTotal, "W-min", cancellationToken);
    }

    private static async Task HandleLightAsync(DeviceLogic deviceLogic, Device device, string outputKind, int outputIndex, string property, string payload, CancellationToken cancellationToken)
    {
        string outputName = string.Concat(char.ToUpperInvariant(outputKind[0]), outputKind.Substring(1), " ", outputIndex.ToString(CultureInfo.InvariantCulture));
        if (string.IsNullOrEmpty(property))
        {
            string state = payload.Trim();
            if (string.Equals(state, "on", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "off", StringComparison.OrdinalIgnoreCase))
                await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationRoleSwitch, "online", [CreateSwitchValue(outputName, state, outputIndex == 0)], cancellationToken);
            return;
        }

        if (string.Equals(property, "power", StringComparison.OrdinalIgnoreCase))
        {
            await UpdateMeasurementAsync(deviceLogic, device, string.Concat(outputName, " Power"), payload, DeviceData.DataTypePowerCurrentConsumption, "W", cancellationToken);
            return;
        }

        if (string.Equals(property, "energy", StringComparison.OrdinalIgnoreCase))
        {
            await UpdateMeasurementAsync(deviceLogic, device, string.Concat(outputName, " Energy"), payload, DeviceData.DataTypePowerTotal, "W-min", cancellationToken);
            return;
        }

        if (!string.Equals(property, "status", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement status = document.RootElement;
            List<DeviceStateChangedMessage.DeviceStateValue> changes = [];
            if (status.TryGetProperty("ison", out JsonElement isOn) && isOn.ValueKind is JsonValueKind.True or JsonValueKind.False)
                changes.Add(CreateSwitchValue(outputName, isOn.GetBoolean() ? "on" : "off", outputIndex == 0));
            if (status.TryGetProperty("brightness", out JsonElement brightness) && brightness.TryGetDecimal(out decimal brightnessValue))
                changes.Add(CreateGradientValue(string.Concat(outputName, " Brightness"), brightnessValue, outputIndex == 0));
            if (status.TryGetProperty("red", out JsonElement red) && red.TryGetByte(out byte redValue)
                && status.TryGetProperty("green", out JsonElement green) && green.TryGetByte(out byte greenValue)
                && status.TryGetProperty("blue", out JsonElement blue) && blue.TryGetByte(out byte blueValue))
            {
                changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = string.Concat(outputName, " Color"),
                    Value = string.Concat("#", redValue.ToString("X2", CultureInfo.InvariantCulture), greenValue.ToString("X2", CultureInfo.InvariantCulture), blueValue.ToString("X2", CultureInfo.InvariantCulture)),
                    Category = DeviceDataCategory.DeviceState,
                    StandardDataType = DeviceData.DataTypeColor
                });
            }

            if (changes.Count > 0)
                await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationRoleDimmer, "online", changes, cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task HandleMeterAsync(DeviceLogic deviceLogic, Device device, int meterIndex, string property, string payload, CancellationToken cancellationToken)
    {
        if (string.Equals(property, "power", StringComparison.OrdinalIgnoreCase))
            await UpdateMeasurementAsync(deviceLogic, device, string.Concat("Meter ", meterIndex.ToString(CultureInfo.InvariantCulture), " Power"), payload, DeviceData.DataTypePowerCurrentConsumption, "W", cancellationToken);
        else if (string.Equals(property, "total", StringComparison.OrdinalIgnoreCase))
            await UpdateMeasurementAsync(deviceLogic, device, string.Concat("Meter ", meterIndex.ToString(CultureInfo.InvariantCulture), " Energy"), payload, DeviceData.DataTypePowerTotal, "Wh", cancellationToken);
    }

    private static DeviceStateChangedMessage.DeviceStateValue CreateSwitchValue(string name, string state, bool isMainData)
    {
        return new DeviceStateChangedMessage.DeviceStateValue()
        {
            Name = name,
            Value = state.ToLowerInvariant(),
            Category = DeviceDataCategory.DeviceState,
            StandardDataType = DeviceData.DataTypeSwitch,
            IsMainData = isMainData
        };
    }

    private static DeviceStateChangedMessage.DeviceStateValue CreateGradientValue(string name, decimal value, bool isMainData)
    {
        return new DeviceStateChangedMessage.DeviceStateValue()
        {
            Name = name,
            Value = value.ToString("0.############################", CultureInfo.InvariantCulture),
            Category = DeviceDataCategory.DeviceState,
            StandardDataType = DeviceData.DataTypeGradient,
            ValueUnit = "%",
            IsMainData = isMainData
        };
    }

    private static async Task UpdateSensorAsync(DeviceLogic deviceLogic, Device device, string name, string payload, string dataType, string unit, CancellationToken cancellationToken)
    {
        await UpdateMeasurementAsync(deviceLogic, device, name, payload, dataType, unit, cancellationToken);
    }

    private static async Task UpdateMeasurementAsync(DeviceLogic deviceLogic, Device device, string name, string payload, string dataType, string unit, CancellationToken cancellationToken)
    {
        if (!decimal.TryParse(payload, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
            return;

        (decimal canonicalValue, string canonicalUnit) = MeasurementUnitNormalizer.ToCanonical(value, dataType, unit);

        await deviceLogic.OnDeviceStateChangedAsync(Platform, device.Id, Device.HomeAutomationMainRoleSensors, "online",
            [
                new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = name,
                    Value = canonicalValue.ToString("0.############################", CultureInfo.InvariantCulture),
                    Category = DeviceDataCategory.SensorReading,
                    StandardDataType = dataType,
                    ValueUnit = canonicalUnit,
                    IsMainData = true
                }
            ], cancellationToken);
    }

    private static void PublishInputEvent(Device device, int inputIndex, string payload)
    {
        string rawAction = payload?.Trim();
        Dictionary<string, string> attributes = new Dictionary<string, string>() { ["input"] = inputIndex.ToString(CultureInfo.InvariantCulture) };
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("event", out JsonElement eventValue) && eventValue.ValueKind == JsonValueKind.String)
                rawAction = eventValue.GetString()?.Trim();
            if (document.RootElement.TryGetProperty("event_cnt", out JsonElement count) && count.TryGetInt32(out int eventCount))
                attributes["eventCount"] = eventCount.ToString(CultureInfo.InvariantCulture);
        }
        catch (JsonException)
        {
        }

        if (string.IsNullOrWhiteSpace(rawAction))
            return;

        string action = rawAction.ToUpperInvariant() switch
        {
            "S" => "single_push",
            "SS" => "double_push",
            "SSS" => "triple_push",
            "L" => "long_push",
            _ => rawAction
        };

        NatsInterprocess.Push(new DeviceActionTriggeredMessage()
        {
            DeviceId = device.Id,
            DeviceInternalName = device.DeviceInternalName,
            DevicePlatform = Platform,
            ActionKind = "button",
            Action = action,
            RawAction = rawAction,
            Attributes = attributes
        });
    }

    private async Task HandleAnnounceAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                return;

            string model = root.TryGetProperty("model", out JsonElement modelValue) && modelValue.ValueKind == JsonValueKind.String
                ? modelValue.GetString()
                : string.Empty;
            string mode = root.TryGetProperty("mode", out JsonElement modeValue) && modeValue.ValueKind == JsonValueKind.String
                ? modeValue.GetString()
                : string.Empty;
            List<string> roles = GetRoles(model, mode);
            await new DiscoveredDeviceLogic().UpsertAsync(new DiscoveredDevice()
            {
                DeviceInternalName = id.GetString(),
                DeviceAgentId = "sarah",
                DevicePlatform = Platform,
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = roles,
                AvailableActions = SupportsInputActions(model) ? GetButtonActions() : [],
                DefaultConfigurationData = root.GetRawText()
            }, cancellationToken);
        }
        catch (JsonException)
        {
        }
    }

    public async Task<bool> OnboardAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(string.Concat("http://", ipAddress?.Trim().TrimEnd('/'), "/"), UriKind.Absolute, out Uri deviceAddress))
            return false;

        return await ConfigureMqttAsync(deviceAddress, cancellationToken);
    }

    private async Task<bool> ConfigureMqttAsync(Uri deviceAddress, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage deviceInfoResponse = await _httpClient.GetAsync(new Uri(deviceAddress, "shelly"), cancellationToken);
            if (!deviceInfoResponse.IsSuccessStatusCode)
                return await ReportUnsupportedGenerationAsync(deviceAddress, cancellationToken);

            using JsonDocument deviceInfo = JsonDocument.Parse(await deviceInfoResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!IsGen1DeviceInfo(deviceInfo.RootElement)
                || !deviceInfo.RootElement.TryGetProperty("auth", out JsonElement authenticationEnabled)
                || authenticationEnabled.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                return await ReportUnsupportedGenerationAsync(deviceAddress, cancellationToken);

            string password = GetPassword();
            if (string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("Neither SHELLY_GEN1_PASSWORD nor HOMEAUTOMATION_APIKEY is configured; skipping Shelly Gen1 onboarding for {Address}.", deviceAddress);
                return false;
            }

            string username = Environment.GetEnvironmentVariable("SHELLY_GEN1_USERNAME");
            if (string.IsNullOrWhiteSpace(username))
                username = "sarah";

            if (!TryGetMqttServer(out string mqttServer))
            {
                _logger.LogWarning("MQTT_SERVICE_HOST is not configured; skipping Shelly Gen1 onboarding for {Address}.", deviceAddress);
                return false;
            }

            if (authenticationEnabled.ValueKind == JsonValueKind.False)
            {
                Uri loginUri = new Uri(deviceAddress, string.Concat("settings/login?enabled=true&username=", Uri.EscapeDataString(username), "&password=", Uri.EscapeDataString(password)));
                using HttpResponseMessage loginResponse = await _httpClient.GetAsync(loginUri, cancellationToken);
                if (!loginResponse.IsSuccessStatusCode)
                    return false;
            }

            using HttpResponseMessage settingsResponse = await GetAuthenticatedAsync(new Uri(deviceAddress, "settings"), username, password, cancellationToken);
            if (!settingsResponse.IsSuccessStatusCode)
                return false;

            using JsonDocument settings = JsonDocument.Parse(await settingsResponse.Content.ReadAsStringAsync(cancellationToken));
            if (IsMqttConfigured(settings.RootElement, mqttServer))
                return true;

            Uri configureUri = new Uri(deviceAddress, string.Concat("settings?mqtt_enable=true&mqtt_server=", Uri.EscapeDataString(mqttServer)));
            using HttpResponseMessage configurationResponse = await GetAuthenticatedAsync(configureUri, username, password, cancellationToken);
            if (!configurationResponse.IsSuccessStatusCode)
                return false;

            using HttpResponseMessage rebootResponse = await GetAuthenticatedAsync(new Uri(deviceAddress, "reboot"), username, password, cancellationToken);
            if (rebootResponse.IsSuccessStatusCode)
                _logger.LogInformation("Configured MQTT for unprotected Shelly Gen1 device at {Address}.", deviceAddress);
            return rebootResponse.IsSuccessStatusCode;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogDebug(exception, "Unable to auto-configure Shelly Gen1 device at {Address}.", deviceAddress);
            return false;
        }
        catch (JsonException exception)
        {
            _logger.LogDebug(exception, "Shelly Gen1 device at {Address} returned an invalid configuration response.", deviceAddress);
            return false;
        }
    }

    private async Task<bool> ReportUnsupportedGenerationAsync(Uri deviceAddress, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(new Uri(deviceAddress, "rpc/Shelly.GetDeviceInfo"), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Shelly device at {Address} is not a supported Gen1 device.", deviceAddress);
            return false;
        }

        using JsonDocument deviceInfo = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (deviceInfo.RootElement.TryGetProperty("gen", out JsonElement generation)
            && generation.ValueKind == JsonValueKind.Number
            && generation.TryGetInt32(out int generationNumber))
        {
            _logger.LogInformation("Shelly Gen{Generation} device at {Address} is detected but not supported yet.", generationNumber, deviceAddress);
            return false;
        }

        _logger.LogInformation("Shelly device at {Address} is not a supported Gen1 device.", deviceAddress);
        return false;
    }

    private async Task<HttpResponseMessage> GetAuthenticatedAsync(Uri requestUri, string username, string password, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        string credential = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(username, ":", password)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static bool IsMqttConfigured(JsonElement settings, string mqttServer)
    {
        return settings.TryGetProperty("mqtt", out JsonElement mqtt)
            && mqtt.ValueKind == JsonValueKind.Object
            && mqtt.TryGetProperty("enable", out JsonElement enabled)
            && enabled.ValueKind == JsonValueKind.True
            && mqtt.TryGetProperty("server", out JsonElement server)
            && server.ValueKind == JsonValueKind.String
            && string.Equals(server.GetString(), mqttServer, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGen1DeviceInfo(JsonElement deviceInfo)
    {
        return deviceInfo.TryGetProperty("type", out JsonElement type)
            && type.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(type.GetString());
    }

    private static string GetPassword()
    {
        string password = Environment.GetEnvironmentVariable("SHELLY_GEN1_PASSWORD");
        return string.IsNullOrWhiteSpace(password) ? Environment.GetEnvironmentVariable("HOMEAUTOMATION_APIKEY") : password;
    }

    private static List<string> GetRoles(string model, string mode)
    {
        List<string> roles = [];
        if (IsRollerModel(model, mode))
            roles.Add(Device.HomeAutomationMainRoleShutterSwitch);
        else if (IsRelayModel(model))
            roles.Add(Device.HomeAutomationRoleSwitch);
        if (IsDimmableModel(model))
        {
            roles.Add(Device.HomeAutomationRoleSwitch);
            roles.Add(Device.HomeAutomationRoleDimmer);
        }
        if (IsColorModel(model))
            roles.Add(Device.HomeAutomationRoleColorBound);
        if (IsButtonModel(model))
            roles.Add(Device.HomeAutomationRoleActionnable);
        if (roles.Count == 0)
            roles.Add(Device.HomeAutomationMainRoleSensors);

        return roles;
    }

    private static bool IsRollerModel(string model, string mode)
    {
        return string.Equals(mode, "roller", StringComparison.OrdinalIgnoreCase)
            && (model.StartsWith("SHSW-21", StringComparison.OrdinalIgnoreCase)
                || model.StartsWith("SHSW-25", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRelayModel(string model)
    {
        return model.StartsWith("SHSW", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHPLG", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHEM", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHUNI", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDimmableModel(string model)
    {
        return model.StartsWith("SHDM", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHBLB", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHBDUO", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHVIN", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHCB", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHRGBW", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsColorModel(string model)
    {
        return model.StartsWith("SHBLB", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHCB", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHRGBW", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsButtonModel(string model)
    {
        return model.StartsWith("SHBTN", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHIX", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsInputActions(string model)
    {
        return IsButtonModel(model)
            || model.StartsWith("SHSW", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHUNI", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHRGBW", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHDM", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelayName(int relayIndex)
    {
        return relayIndex == 0 ? "Switch" : string.Concat("Relay ", relayIndex.ToString(CultureInfo.InvariantCulture));
    }

    private static bool TryGetIndexedTopic(string topic, string expectedRoot, out int index, out string property)
    {
        index = 0;
        property = null;
        string[] parts = topic.Split('/');
        return parts.Length is 2 or 3
            && string.Equals(parts[0], expectedRoot, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && index >= 0
            && (property = parts.Length == 3 ? parts[2] : string.Empty) != null;
    }

    private static List<DeviceAvailableAction> GetButtonActions()
    {
        return
        [
            new DeviceAvailableAction() { RawAction = "S", Action = "single_push", ActionKind = "button" },
            new DeviceAvailableAction() { RawAction = "SS", Action = "double_push", ActionKind = "button" },
            new DeviceAvailableAction() { RawAction = "SSS", Action = "triple_push", ActionKind = "button" },
            new DeviceAvailableAction() { RawAction = "L", Action = "long_push", ActionKind = "button" }
        ];
    }

    private static bool TryGetRelativeTopic(string topic, out string relativeTopic)
    {
        relativeTopic = null;
        string prefix = string.Concat(GetTopicRoot(), "/");
        if (!topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        relativeTopic = topic.Substring(prefix.Length);
        return !string.IsNullOrWhiteSpace(relativeTopic);
    }

    private static string GetTopicRoot()
    {
        string configuredRoot = Environment.GetEnvironmentVariable("SHELLY_GEN1_TOPIC");
        return string.IsNullOrWhiteSpace(configuredRoot) ? "shellies" : configuredRoot.Trim().Trim('/');
    }

    private static (string host, int port) ResolveMqttEndpoint()
    {
        string host = Environment.GetEnvironmentVariable("MQTT_SERVICE_HOST");
        if (string.IsNullOrWhiteSpace(host))
            host = "localhost";

        string portValue = Environment.GetEnvironmentVariable("MQTT_SERVICE_PORT");
        return (host, int.TryParse(portValue, out int port) ? port : 1883);
    }

    private static bool TryGetMqttServer(out string mqttServer)
    {
        string host = Environment.GetEnvironmentVariable("MQTT_SERVICE_HOST");
        string portValue = Environment.GetEnvironmentVariable("MQTT_SERVICE_PORT");
        int port = int.TryParse(portValue, out int configuredPort) ? configuredPort : 1883;
        mqttServer = string.IsNullOrWhiteSpace(host) ? null : string.Concat(host.Trim(), ":", port.ToString(CultureInfo.InvariantCulture));
        return mqttServer != null;
    }
}