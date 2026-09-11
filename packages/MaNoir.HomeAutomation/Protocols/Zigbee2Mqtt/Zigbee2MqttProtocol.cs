using MaNoir.Core.DataPublication;
using MQTTnet;
using MQTTnet.Protocol;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Protocols.Zigbee2Mqtt;

/// <summary>
/// Publishes commands to Zigbee2MQTT through the process-wide MQTT connection.
/// </summary>
public sealed class Zigbee2MqttProtocol
{
    private readonly Func<MqttApplicationMessage, CancellationToken, Task> _publishAsync;

    public Zigbee2MqttProtocol(string topicRoot = "zigbee2mqtt")
        : this(
            (message, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return MqttConnectionManager.Shared.EnqueueAsync(message);
            },
            topicRoot)
    {
    }

    public Zigbee2MqttProtocol(
        Func<MqttApplicationMessage, CancellationToken, Task> publishAsync,
        string topicRoot = "zigbee2mqtt")
    {
        _publishAsync = publishAsync ?? throw new ArgumentNullException(nameof(publishAsync));
        if (string.IsNullOrWhiteSpace(topicRoot))
            throw new ArgumentException("A Zigbee2MQTT topic root is required.", nameof(topicRoot));

        TopicRoot = topicRoot.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(TopicRoot))
            throw new ArgumentException("A Zigbee2MQTT topic root is required.", nameof(topicRoot));
    }

    public string TopicRoot { get; }

    public Task PublishCommandAsync(
        string deviceId,
        IReadOnlyDictionary<string, object> command,
        CancellationToken cancellationToken = default)
    {
        return PublishJsonAsync(deviceId, "set", command, cancellationToken);
    }

    public Task PublishGetAsync(
        string deviceId,
        IReadOnlyDictionary<string, object> request,
        CancellationToken cancellationToken = default)
    {
        return PublishJsonAsync(deviceId, "get", request, cancellationToken);
    }

    private Task PublishJsonAsync(
        string deviceId,
        string operation,
        IReadOnlyDictionary<string, object> payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("A Zigbee device identifier is required.", nameof(deviceId));
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        MqttApplicationMessage message = new MqttApplicationMessageBuilder()
            .WithTopic(string.Concat(TopicRoot, "/", deviceId.Trim(), "/", operation))
            .WithPayload(JsonSerializer.Serialize(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        return _publishAsync(message, cancellationToken);
    }
}
