using Home.Common;
using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.Agents.Sarah;
using MaNoir.HomeAutomation;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;
using MaNoir.HomeAutomation.Protocols.Zigbee2Mqtt;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah.Zigbee2Mqtt;

public sealed class Zigbee2MqttRuntimeService : BackgroundService
{
    // TODO: Promote Zigbee metadata (IEEE address, manufacturer, model, network type, power source and last activity) from discovery JSON to device fields.
    // TODO: Model exposes as structured discovery data, including numeric ranges, enum values, units and writable access.
    // TODO: Support additional Zigbee2MQTT commands such as locks, covers, climate, groups, identify and native Zigbee scenes.
    // TODO: Handle bridge diagnostics and command failures, including last received message and bridge health information.
    private readonly ILogger<Zigbee2MqttRuntimeService> _logger;
    private readonly SarahDeviceService _deviceService;
    private readonly RuntimeDeviceRegistry _runtimeRegistry;
    private IMqttClient _mqttClient;
    private Zigbee2MqttProtocol _protocol;
    private Exception _lastError;

    public Zigbee2MqttRuntimeService(
        ILogger<Zigbee2MqttRuntimeService> logger,
        SarahDeviceService deviceService = null,
        RuntimeDeviceRegistry runtimeRegistry = null)
    {
        _logger = logger;
        _runtimeRegistry = runtimeRegistry ?? new RuntimeDeviceRegistry();
        _deviceService = deviceService;
    }

    public RuntimeDeviceRegistry RuntimeRegistry => _runtimeRegistry;

    public bool IsConnected => _mqttClient?.IsConnected == true;

    public Exception LastError => _lastError;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string topicRoot = GetTopicRoot();
        (string host, int port) = ResolveMqttEndpoint();
        MqttFactory factory = new MqttFactory();
        using IMqttClient client = factory.CreateMqttClient();
        _mqttClient = client;


        _protocol = new Zigbee2MqttProtocol(
            (message, cancellationToken) => client.PublishAsync(message, cancellationToken),
            topicRoot);
        client.ApplicationMessageReceivedAsync += args => HandleMqttMessageAsync(
            args.ApplicationMessage.Topic,
            Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()),
            stoppingToken);

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
        catch (Exception exception)
        {
            _lastError = exception;
            _logger.LogError(exception, "Zigbee2MQTT runtime stopped unexpectedly.");
        }
        finally
        {
            _mqttClient = null;
            _protocol = null;
            if (client.IsConnected)
                await client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None);
        }
    }

    public async Task PublishCommandAsync(
        string deviceId,
        IReadOnlyDictionary<string, object> command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("A Zigbee device identifier is required.", nameof(deviceId));
        if (command == null)
            throw new ArgumentNullException(nameof(command));
        if (_mqttClient == null || !_mqttClient.IsConnected)
            throw new InvalidOperationException("The Zigbee2MQTT client is not connected.");

        MqttApplicationMessage message = new MqttApplicationMessageBuilder()
            .WithTopic(string.Concat(GetTopicRoot(), "/", deviceId, "/set"))
            .WithPayload(JsonSerializer.Serialize(command))
            .Build();
        await _mqttClient.PublishAsync(message, cancellationToken);
    }

    public async Task HandleMessageAsync(string topic, string payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        if (TryGetAvailabilityDeviceId(topic, out string availabilityDeviceId))
        {
            string availability = payload.Trim();
            (_runtimeRegistry.GetById(availabilityDeviceId) as ZigbeeDevice)?.ApplyAvailability(availability);
            if (string.Equals(availability, "online", StringComparison.OrdinalIgnoreCase)
                || string.Equals(availability, "offline", StringComparison.OrdinalIgnoreCase))
            {
                if (_deviceService != null)
                    await _deviceService.ChangeStatusAsync("zigbee2mqtt", availabilityDeviceId, availability, cancellationToken);
            }

            return;
        }

        bool isBridgeDevicesTopic = IsBridgeDevicesTopic(topic);
        string deviceId = null;
        if (!isBridgeDevicesTopic && !TryGetDeviceId(topic, out deviceId))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (isBridgeDevicesTopic)
            {
                await HandleBridgeDevicesAsync(document.RootElement, cancellationToken);
                return;
            }

            ZigbeeDevice runtimeDevice = _runtimeRegistry.GetById(deviceId) as ZigbeeDevice;
            if (runtimeDevice == null)
            {
                _logger.LogDebug("Ignoring Zigbee2MQTT state for undiscovered device {DeviceId}.", deviceId);
                return;
            }

            runtimeDevice.ApplyState(document.RootElement);

            Device device = _deviceService == null
                ? null
                : await _deviceService.GetByIdOrInternalNameAsync(deviceId, "zigbee2mqtt", cancellationToken);
            if (runtimeDevice.TryApplyAction(document.RootElement, out RuntimeDeviceAction runtimeAction))
            {
                DeviceActionTriggeredMessage actionMessage = CreateDeviceAction(runtimeAction, runtimeDevice, device);
                NatsInterprocess.Push(actionMessage);
            }

        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Ignoring invalid Zigbee2MQTT payload on {Topic}.", topic);
        }
    }

    private async Task HandleMqttMessageAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        try
        {
            await HandleMessageAsync(topic, payload, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to process Zigbee2MQTT message on {Topic}.", topic);
        }
    }

    private static DeviceActionTriggeredMessage CreateDeviceAction(RuntimeDeviceAction action, ZigbeeDevice runtimeDevice, Device device)
    {
        if (action == null || runtimeDevice == null)
            return null;

        return new DeviceActionTriggeredMessage()
        {
            DeviceId = device?.Id ?? runtimeDevice.Id,
            DeviceInternalName = device?.DeviceInternalName ?? runtimeDevice.Id,
            DevicePlatform = "zigbee2mqtt",
            ActionKind = action.Kind,
            Action = action.Action,
            RawAction = action.RawAction,
            Attributes = new Dictionary<string, string>(action.Attributes)
        };
    }

    private async Task HandleBridgeDevicesAsync(JsonElement devices, CancellationToken cancellationToken)
    {
        if (devices.ValueKind != JsonValueKind.Array)
            return;

        _logger.LogInformation("Received Zigbee2MQTT device list with {DeviceCount} entries.", devices.GetArrayLength());

        List<DiscoveredDevice> discoveredDevices = [];
        List<IDevice> runtimeDevices = [];
        foreach (JsonElement device in devices.EnumerateArray())
        {
            if (!device.TryGetProperty("friendly_name", out JsonElement friendlyName) || friendlyName.ValueKind != JsonValueKind.String)
                continue;
            if (device.TryGetProperty("type", out JsonElement type) && string.Equals(type.GetString(), "coordinator", StringComparison.OrdinalIgnoreCase))
                continue;

            string deviceName = friendlyName.GetString();
            if (string.IsNullOrWhiteSpace(deviceName))
                continue;

            ZigbeeDevice runtimeDevice = ZigbeeDevice.Create(deviceName, device, _protocol);

            discoveredDevices.Add(new DiscoveredDevice()
            {
                DeviceInternalName = deviceName,
                DeviceAgentId = "sarah",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = runtimeDevice.DeviceRoles.ToList(),
                DeviceCapabilities = runtimeDevice.DeviceCapabilities.ToList(),
                AvailableActions = runtimeDevice.AvailableActions.ToList(),
                DefaultConfigurationData = device.GetRawText()
            });
            runtimeDevices.Add(runtimeDevice);
        }

        Zigbee2MqttBridgeDevice runtimeBridge = Zigbee2MqttBridgeDevice.Create(GetTopicRoot(), runtimeDevices);
        discoveredDevices.Insert(0, new DiscoveredDevice()
        {
            DeviceInternalName = runtimeBridge.Id,
            DeviceAgentId = "sarah",
            DevicePlatform = "zigbee2mqtt",
            DeviceKind = Device.DeviceKindHomeAutomation,
            DeviceRoles = [Device.HomeAutomationMainRoleBridge]
        });

        _runtimeRegistry.ApplySnapshot("zigbee2mqtt", [runtimeBridge, .. runtimeDevices]);

        try
        {
            await new DiscoveredDeviceLogic().UpsertManyAsync(discoveredDevices, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to persist Zigbee2MQTT discovery; runtime devices remain available.");
        }
    }

    private static bool IsBridgeDevicesTopic(string topic)
    {
        return string.Equals(topic, string.Concat(GetTopicRoot(), "/bridge/devices"), StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetDeviceId(string topic, out string deviceId)
    {
        deviceId = null;
        string topicRoot = GetTopicRoot();
        string prefix = string.Concat(topicRoot, "/");
        if (string.IsNullOrWhiteSpace(topic) || !topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string relativeTopic = topic.Substring(prefix.Length);
        if (relativeTopic.StartsWith("bridge/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativeTopic, "bridge", StringComparison.OrdinalIgnoreCase))
            return false;

        deviceId = _runtimeRegistry.Devices
            .OfType<ZigbeeDevice>()
            .Select(device => device.Id)
            .Where(id => string.Equals(relativeTopic, id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(id => id.Length)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(deviceId))
            return true;

        return false;
    }

    private bool TryGetAvailabilityDeviceId(string topic, out string deviceId)
    {
        deviceId = null;
        string prefix = string.Concat(GetTopicRoot(), "/");
        if (string.IsNullOrWhiteSpace(topic) || !topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        const string availabilitySuffix = "/availability";
        string relativeTopic = topic.Substring(prefix.Length);
        if (!relativeTopic.EndsWith(availabilitySuffix, StringComparison.OrdinalIgnoreCase))
            return false;

        string candidate = relativeTopic.Substring(0, relativeTopic.Length - availabilitySuffix.Length).Trim();
        deviceId = _runtimeRegistry.Devices
            .OfType<ZigbeeDevice>()
            .Select(device => device.Id)
            .FirstOrDefault(id => string.Equals(id, candidate, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(deviceId))
            return true;

        deviceId = candidate;
        return !string.IsNullOrWhiteSpace(deviceId);
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
        else if (Uri.TryCreate(host, UriKind.Absolute, out Uri endpoint)
            && !string.IsNullOrWhiteSpace(endpoint.Host))
            host = endpoint.Host;
        else
            host = host.Trim().TrimEnd('/');

        string portValue = Environment.GetEnvironmentVariable("MQTT_SERVICE_PORT");
        return (host, int.TryParse(portValue, out int port) ? port : 1883);
    }
}