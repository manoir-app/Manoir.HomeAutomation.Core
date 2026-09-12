using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Devices.Awtrix;
using MQTTnet;
using MQTTnet.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MaNoir.Agents.Sarah.Awtrix;

public sealed class AwtrixRuntimeService : BackgroundService
{
    private const string Platform = "awtrix";
    private const string DisplayAppName = "manoir_display";
    private readonly ILogger<AwtrixRuntimeService> _logger;
    private readonly HttpClient _httpClient;
    private readonly RuntimeDeviceRegistry _runtimeRegistry;
    private readonly Dictionary<string, AwtrixDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private IMqttClient _mqttClient;

    public AwtrixRuntimeService(
        ILogger<AwtrixRuntimeService> logger,
        HttpClient httpClient = null,
        RuntimeDeviceRegistry runtimeRegistry = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _runtimeRegistry = runtimeRegistry ?? new RuntimeDeviceRegistry();
    }

    public RuntimeDeviceRegistry RuntimeRegistry => _runtimeRegistry;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string mqttHost = Environment.GetEnvironmentVariable("MQTT_SERVICE_HOST") ?? "localhost";
        int mqttPort = int.TryParse(Environment.GetEnvironmentVariable("MQTT_SERVICE_PORT"), out int configuredPort)
            ? configuredPort
            : 1883;
        const string mqttTopic = "awtrix";

        MqttFactory factory = new();
        _mqttClient = factory.CreateMqttClient();
        _mqttClient.ApplicationMessageReceivedAsync += args => HandleMqttMessageAsync(
            args.ApplicationMessage.Topic,
            Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()));

        try
        {
            await _mqttClient.ConnectAsync(new MqttClientOptionsBuilder()
                .WithClientId("manoir-sarah-awtrix")
                .WithTcpServer(mqttHost, mqttPort)
                .Build(), stoppingToken);
            await _mqttClient.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(string.Concat(mqttTopic.TrimEnd('/'), "/#"))
                .Build(), stoppingToken);
            _logger.LogInformation("Subscribed to AWTRIX MQTT topic {Topic} at {Host}:{Port}.", mqttTopic, mqttHost, mqttPort);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_mqttClient.IsConnected)
                await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None);
        }
    }

    public async Task HandleMqttMessageAsync(string topic, string payload)
    {
        if (string.IsNullOrWhiteSpace(topic) || string.IsNullOrWhiteSpace(payload))
            return;

        try
        {
            string[] topicParts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (topicParts.Length < 3 || !string.Equals(topicParts[0], "awtrix", StringComparison.OrdinalIgnoreCase))
                return;

            string deviceId = topicParts[1];
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement state = document.RootElement;
            if (string.Equals(topicParts[2], "stats", StringComparison.OrdinalIgnoreCase)
                && TryGetString(state, "ip", out string host))
            {
                AwtrixDevice device = GetOrCreateDevice(deviceId, host);
                device.ApplyMqttState(state);
                return;
            }

            if (_devices.TryGetValue(deviceId, out AwtrixDevice knownDevice))
                knownDevice.ApplyMqttState(state);
        }
        catch (JsonException exception)
        {
            _logger.LogDebug(exception, "Ignoring non-JSON AWTRIX MQTT message on {Topic}.", topic);
        }
    }

    private AwtrixDevice GetOrCreateDevice(string id, string host)
    {
        if (_devices.TryGetValue(id, out AwtrixDevice existing)
            && string.Equals(existing.Host, host, StringComparison.OrdinalIgnoreCase))
            return existing;

        AwtrixDevice device = AwtrixDevice.Create(
            id,
            host,
            (text, color, icon, cancellationToken) => SetDisplayTextAsync(host, text, color, icon, cancellationToken),
            cancellationToken => ClearDisplayAsync(host, cancellationToken),
            (notification, cancellationToken) => SendNotificationAsync(host, notification, cancellationToken));
        _devices[id] = device;
        _runtimeRegistry.ApplySnapshot(Platform, _devices.Values.ToArray());
        _logger.LogInformation("Discovered AWTRIX device {DeviceId} at {Host}.", id, host);
        return device;
    }

    private Task SetDisplayTextAsync(string host, string text, DeviceColor color, string icon, CancellationToken cancellationToken)
    {
        return PostJsonAsync(
            string.Concat("/api/custom?name=", DisplayAppName),
            new { text, color = ToHexColor(color), icon },
            host,
            cancellationToken);
    }

    private Task ClearDisplayAsync(string host, CancellationToken cancellationToken)
    {
        return SendAsync(HttpMethod.Post, string.Concat("/api/custom?name=", DisplayAppName), null, host, cancellationToken);
    }

    private Task SendNotificationAsync(string host, RuntimeDeviceNotification notification, CancellationToken cancellationToken)
    {
        return PostJsonAsync(
            "/api/notify",
            new
            {
                text = notification.Text,
                color = ToHexColor(notification.Color),
                icon = notification.Icon,
                duration = notification.DurationSeconds,
                repeat = notification.Repeat,
                hold = notification.Hold
            },
                host,
                cancellationToken);
    }

            private async Task PostJsonAsync(string path, object body, string host, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(CreateBaseAddress(host), path));
        request.Content = new StringContent(JsonSerializer.Serialize(body, _jsonOptions), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendAsync(HttpMethod method, string path, object body, string host, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, new Uri(CreateBaseAddress(host), path));
        if (body != null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, _jsonOptions), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        foreach (JsonProperty property in element.EnumerateObject().Where(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)))
        {
            value = property.Value.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        value = null;
        return false;
    }

    private static Uri CreateBaseAddress(string host)
    {
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new Uri(host.TrimEnd('/') + "/");
        return new Uri(string.Concat("http://", host.TrimEnd('/'), "/"));
    }

    private static string ToHexColor(DeviceColor color)
    {
        return color is DeviceColor.Rgb rgb
            ? string.Concat("#", rgb.Red.ToString("X2"), rgb.Green.ToString("X2"), rgb.Blue.ToString("X2"))
            : null;
    }
}
