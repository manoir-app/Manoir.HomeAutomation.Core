using Home.Common;
using Home.Common.Messages;
using Home.Common.Model;
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
using MaNoir.HomeAutomation;
using MaNoir.Core.Contracts.Models.Locations;
using MaNoir.Core.Contracts.Models.Mesh;
using MaNoir.Core.Locations;
using MaNoir.Core.Mesh;

namespace MaNoir.Agents.Sarah;

public sealed class TriggerRuntimeService : BackgroundService
{
    private readonly ILogger<TriggerRuntimeService> _logger;
    private readonly SarahRuntime _runtime;
    private readonly object _sync = new();
    private readonly Dictionary<string, decimal> _lastNumericValues = new(StringComparer.Ordinal);
    private List<Trigger> _triggers = [];
    private GeoCoordinates _localCoordinates;
    private TimeZoneInfo _localTimeZone = TimeZoneInfo.Local;

    public TriggerRuntimeService(ILogger<TriggerRuntimeService> logger, SarahRuntime runtime = null)
    {
        _logger = logger;
        _runtime = runtime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IMqttClient client = new MqttFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += args => HandleMqttMessageAsync(
            args.ApplicationMessage.Topic,
            Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()),
            stoppingToken);

        try
        {
            await ReloadAsync(client, stoppingToken);
            await client.ConnectAsync(CreateMqttOptions(), stoppingToken);
            await SubscribeToMqttTriggersAsync(client, stoppingToken);

            using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RaiseDueClockTriggersAsync(stoppingToken);
                await ReloadAsync(client, stoppingToken);
                await SubscribeToMqttTriggersAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Trigger runtime stopped unexpectedly.");
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None);
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await ReloadAsync(null, cancellationToken);
    }

    private async Task ReloadAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        await RefreshSolarContextAsync(cancellationToken);
        List<Trigger> triggers = await new TriggerLogic().GetAllAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (Trigger trigger in triggers ?? [])
        {
            if (trigger.Kind == TriggerKind.Clock)
                trigger.ProbableNextOccurence = CalculateNextOccurrence(trigger, now);
        }

        lock (_sync)
        {
            _triggers = triggers ?? [];
        }

        if (client != null && client.IsConnected)
            await SubscribeToMqttTriggersAsync(client, cancellationToken);
    }

    public async Task HandleMqttMessageAsync(string topic, string payload, CancellationToken cancellationToken = default)
    {
        Trigger[] matchingTriggers;
        lock (_sync)
        {
            matchingTriggers = _triggers
                .Where(trigger => trigger.Kind == TriggerKind.MqttValue && TopicMatches(trigger.Path, topic))
                .ToArray();
        }

        foreach (Trigger trigger in matchingTriggers)
        {
            string value = ExtractValue(payload, trigger.JsonPathInValue);
            if (value == null || !PassesThreshold(trigger, value))
                continue;

            await new TriggerLogic().RaiseAsync(trigger.Id, "mqtt", value, cancellationToken);
        }
    }

    public async Task<MessageResponse> HandleNetworkDeviceConnectionChangedAsync(NetworkDeviceConnectionChangedMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.DeviceId) && string.IsNullOrWhiteSpace(message.DeviceName))
            return MessageResponse.GenericFail;

        Trigger[] matchingTriggers;
        lock (_sync)
        {
            matchingTriggers = _triggers
                .Where(trigger => trigger.Kind == TriggerKind.NetworkDeviceConnectionChanged
                    && MatchesNetworkDevice(trigger, message)
                    && MatchesNetworkState(trigger, message.IsConnected))
                .ToArray();
        }

        string data = JsonSerializer.Serialize(message);
        foreach (Trigger trigger in matchingTriggers)
            await new TriggerLogic().RaiseAsync(trigger.Id, "network", data, cancellationToken);

        return MessageResponse.OK;
    }

    public static bool MatchesNetworkDevice(Trigger trigger, NetworkDeviceConnectionChangedMessage message)
    {
        if (trigger == null || message == null || string.IsNullOrWhiteSpace(trigger.NetworkDeviceName))
            return false;

        return string.Equals(trigger.NetworkDeviceName, message.DeviceId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trigger.NetworkDeviceName, message.DeviceName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesNetworkState(Trigger trigger, bool isConnected)
    {
        return !trigger.NetworkDeviceTriggerKind.HasValue
            || (trigger.NetworkDeviceTriggerKind == NetworkDeviceTriggerKind.Connection && isConnected)
            || (trigger.NetworkDeviceTriggerKind == NetworkDeviceTriggerKind.Disconnection && !isConnected);
    }

    public async Task RaiseDueClockTriggersAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        Trigger[] dueTriggers;
        lock (_sync)
        {
            dueTriggers = _triggers
                .Where(trigger => trigger.Kind == TriggerKind.Clock && IsDue(trigger, now))
                .ToArray();
        }

        foreach (Trigger trigger in dueTriggers)
        {
            await new TriggerLogic().RaiseAsync(trigger.Id, "clock", now.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
            trigger.ProbableNextOccurence = CalculateNextOccurrenceAfter(trigger, now);
            await new TriggerLogic().SetSettingsAsync(trigger.Id, trigger.ProbableNextOccurence, cancellationToken);
        }
    }

    public static bool TopicMatches(string pattern, string topic)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(topic))
            return false;

        string[] expected = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] actual = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < expected.Length; index++)
        {
            if (expected[index] == "#")
                return true;
            if (index >= actual.Length || (expected[index] != "+" && !string.Equals(expected[index], actual[index], StringComparison.Ordinal)))
                return false;
        }

        return expected.Length == actual.Length;
    }

    public static DateTimeOffset GetNextOccurrence(Trigger trigger, DateTimeOffset now)
    {
        TimeSpan offset = trigger.Offset.GetValueOrDefault();
        DateTimeOffset next = new DateTimeOffset(now.Date, now.Offset).Add(offset);
        if (next <= now)
            next = next.AddDays(1);
        return next;
    }

    private DateTimeOffset? CalculateNextOccurrence(Trigger trigger, DateTimeOffset now)
    {
        DateTime localDate = TimeZoneInfo.ConvertTime(now, _localTimeZone).Date;
        DateTimeOffset? occurrence = CalculateOccurrenceForDate(trigger, localDate);
        if (!occurrence.HasValue)
            return null;
        if (occurrence <= now && now - occurrence <= TimeSpan.FromMinutes(15))
            return occurrence;
        if (occurrence <= now)
            occurrence = CalculateOccurrenceForDate(trigger, localDate.AddDays(1));
        return occurrence;
    }

    private DateTimeOffset? CalculateNextOccurrenceAfter(Trigger trigger, DateTimeOffset now)
    {
        DateTime localDate = TimeZoneInfo.ConvertTime(now, _localTimeZone).Date;
        return CalculateOccurrenceForDate(trigger, localDate.AddDays(1));
    }

    private DateTimeOffset? CalculateOccurrenceForDate(Trigger trigger, DateTime localDate)
    {
        if (trigger.OffsetKind is TimeOffsetKind.FromSunrise or TimeOffsetKind.FromSunset && _localCoordinates == null)
            return null;

        DateTimeOffset baseOccurrence = trigger.OffsetKind switch
        {
            TimeOffsetKind.FromSunrise when _localCoordinates != null => SolarScheduleCalculator.CalculateSunrise(_localCoordinates, localDate, _localTimeZone),
            TimeOffsetKind.FromSunset when _localCoordinates != null => SolarScheduleCalculator.CalculateSunset(_localCoordinates, localDate, _localTimeZone),
            _ => new DateTimeOffset(localDate, _localTimeZone.GetUtcOffset(localDate))
        };

        return baseOccurrence.Add(trigger.Offset.GetValueOrDefault());
    }

    private async Task RefreshSolarContextAsync(CancellationToken cancellationToken)
    {
        try
        {
            AutomationMesh mesh = await new AutomationMeshLogic().GetLocalAsync(cancellationToken);
            Location location = await new LocationLogic().GetByIdAsync(mesh?.LocationId, cancellationToken);
            _localCoordinates = location?.Coordinates;

            if (!string.IsNullOrWhiteSpace(mesh?.TimeZoneId))
                _localTimeZone = TimeZoneInfo.FindSystemTimeZoneById(mesh.TimeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            _logger.LogWarning(exception, "Unable to resolve the mesh time zone; using the process time zone.");
            _localTimeZone = TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException exception)
        {
            _logger.LogWarning(exception, "The mesh time zone is invalid; using the process time zone.");
            _localTimeZone = TimeZoneInfo.Local;
        }
    }

    private static bool IsDue(Trigger trigger, DateTimeOffset now)
    {
        if (trigger.ProbableNextOccurence.HasValue)
            return trigger.ProbableNextOccurence.Value <= now;

        return false;
    }

    private bool PassesThreshold(Trigger trigger, string value)
    {
        if (!trigger.ThredsholdForChange.HasValue)
            return true;
        if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numericValue))
            return false;

        lock (_sync)
        {
            if (_lastNumericValues.TryGetValue(trigger.Id, out decimal lastValue)
                && Math.Abs(numericValue - lastValue) < Math.Abs(trigger.ThredsholdForChange.Value))
                return false;
            _lastNumericValues[trigger.Id] = numericValue;
            return true;
        }
    }

    private static string ExtractValue(string payload, string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
            return payload;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement current = document.RootElement;
            string normalizedPath = jsonPath.StartsWith("$.", StringComparison.Ordinal) ? jsonPath[2..] : jsonPath;
            foreach (string segment in normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!current.TryGetProperty(segment, out current))
                    return null;
            }
            return current.ValueKind == JsonValueKind.String ? current.GetString() : current.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SubscribeToMqttTriggersAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        if (!client.IsConnected)
            return;

        string[] paths;
        lock (_sync)
        {
            paths = _triggers
                .Where(trigger => trigger.Kind == TriggerKind.MqttValue && !string.IsNullOrWhiteSpace(trigger.Path))
                .Select(trigger => trigger.Path)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (paths.Length > 0)
        {
            MqttClientSubscribeOptionsBuilder builder = new();
            foreach (string path in paths)
                builder.WithTopicFilter(path);
            await client.SubscribeAsync(builder.Build(), cancellationToken);
        }
    }

    private static MqttClientOptions CreateMqttOptions()
    {
        string host = Environment.GetEnvironmentVariable("MQTT_SERVICE_HOST") ?? "localhost";
        string portValue = Environment.GetEnvironmentVariable("MQTT_SERVICE_PORT");
        int.TryParse(portValue, out int port);
        return new MqttClientOptionsBuilder().WithClientId("manoir-sarah-triggers").WithTcpServer(host, port > 0 ? port : 1883).Build();
    }
}