using Home.Common;
using Home.Common.Messages;
using Home.Common.Model;
using MQTTnet;
using MQTTnet.Client;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed partial class TriggerLogic
{
    public const string TriggerChangedTopic = "system.triggers.change";

    public Task<System.Collections.Generic.List<Trigger>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _mongoOperations.GetAllAsync(cancellationToken);
    }

    public async Task<Trigger> GetByIdAsync(string triggerId, CancellationToken cancellationToken = default)
    {
        string normalizedTriggerId = NormalizeTriggerId(triggerId);
        if (normalizedTriggerId == null)
            return null;

        return await _mongoOperations.GetByIdAsync(normalizedTriggerId, cancellationToken);
    }

    public async Task<Trigger> UpsertAsync(Trigger trigger, CancellationToken cancellationToken = default)
    {
        Trigger preparedTrigger = PrepareTrigger(trigger);
        if (preparedTrigger == null)
            return null;

        if (preparedTrigger.Id == null)
            preparedTrigger.Id = Guid.NewGuid().ToString("N").ToLowerInvariant();

        Trigger existingTrigger = await _mongoOperations.GetByIdAsync(preparedTrigger.Id, cancellationToken);
        if (existingTrigger == null)
        {
            await _mongoOperations.InsertAsync(preparedTrigger, cancellationToken);
        }
        else
        {
            ApplyTriggerUpdate(existingTrigger, preparedTrigger);
            await _mongoOperations.SaveAsync(existingTrigger, cancellationToken);
        }

        PublishTriggerCatalogChangedBestEffort();
        return await _mongoOperations.GetByIdAsync(preparedTrigger.Id, cancellationToken);
    }

    public async Task<bool> SetSettingsAsync(string triggerId, DateTimeOffset? probableNextOccurrence = null, CancellationToken cancellationToken = default)
    {
        Trigger trigger = await GetByIdAsync(triggerId, cancellationToken);
        if (trigger == null)
            return false;

        if (probableNextOccurrence.HasValue)
        {
            trigger.ProbableNextOccurence = probableNextOccurrence.Value;
        }
        else if (trigger.ProbableNextOccurence.HasValue && trigger.ProbableNextOccurence.Value < DateTimeOffset.Now)
        {
            trigger.ProbableNextOccurence = null;
        }

        await _mongoOperations.SaveAsync(trigger, cancellationToken);
        return true;
    }

    public async Task<bool> RaiseAsync(string triggerId, string source = null, string data = null, CancellationToken cancellationToken = default)
    {
        Trigger trigger = await GetByIdAsync(triggerId, cancellationToken);
        if (trigger == null)
            return false;

        DateTimeOffset runDate = DateTimeOffset.Now;
        trigger.LatestOccurence = runDate;
        await _mongoOperations.SaveAsync(trigger, cancellationToken);

        await PublishTriggerActivatedBestEffortAsync(trigger.Id, runDate, trigger.ToString(), cancellationToken);
        PublishRaisedMessagesBestEffort(trigger, source, data);
        return true;
    }

    public async Task<bool> DeleteAsync(string triggerId, CancellationToken cancellationToken = default)
    {
        Trigger trigger = await GetByIdAsync(triggerId, cancellationToken);
        if (trigger == null)
            return false;

        await _mongoOperations.DeleteAsync(trigger.Id, cancellationToken);
        PublishTriggerCatalogChangedBestEffort();
        return true;
    }

    private static void PublishTriggerCatalogChangedBestEffort()
    {
        try
        {
            NatsInterprocess.Push(new AgentGenericMessage(TriggerChangedTopic)
            {
                MessageContent = "Triggers changed"
            });
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to publish trigger catalog change: {exception.Message}");
        }
    }

    private static void PublishRaisedMessagesBestEffort(Trigger trigger, string source, string data)
    {
        if (trigger?.RaisedMessages == null)
            return;

        foreach (TriggerRaisedMessage message in trigger.RaisedMessages)
        {
            if (message == null || !string.IsNullOrWhiteSpace(message.Condition?.ToString()))
            {
                if (message?.Condition != null)
                    Console.WriteLine($"Trigger {trigger.Id} skipped one conditioned message because Condition evaluation is not wired yet.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(message.MessageTopic))
                continue;

            try
            {
                NatsInterprocess.Push(message.MessageTopic, trigger.ReplaceContent(message.MessageContent, source, data));
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Unable to publish raised trigger message for {trigger.Id}: {exception.Message}");
            }
        }

        if (trigger.ChangedProperties != null && trigger.ChangedProperties.Count > 0)
            Console.WriteLine($"Trigger {trigger.Id} contains ChangedProperties that are not wired yet.");
    }

    private static async Task PublishTriggerActivatedBestEffortAsync(string triggerId, DateTimeOffset triggerExecutionDate, string description, CancellationToken cancellationToken)
    {
        try
        {
            (string host, int port) = ResolveMqttEndpoint();
            MqttFactory factory = new MqttFactory();
            using IMqttClient client = factory.CreateMqttClient();

            await client.ConnectAsync(new MqttClientOptionsBuilder()
                .WithClientId("trigger-logic-" + Environment.MachineName)
                .WithTcpServer(host, port)
                .Build(), cancellationToken);

            await client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic($"manoir/mesh/triggers/{triggerId}/lastRun")
                .WithPayload(triggerExecutionDate.ToString("O"))
                .WithRetainFlag()
                .Build(), cancellationToken);

            await client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic($"manoir/mesh/triggers/{triggerId}/desc")
                .WithPayload(description)
                .WithRetainFlag()
                .Build(), cancellationToken);

            await client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to publish trigger MQTT state for {triggerId}: {exception.Message}");
        }
    }

    private static (string host, int port) ResolveMqttEndpoint()
    {
        string host = Environment.GetEnvironmentVariable("MQTT_SERVICE_HOST");
        if (string.IsNullOrWhiteSpace(host))
            host = Environment.GetEnvironmentVariable("MOSQUITTO_SERVICE_HOST");
        if (string.IsNullOrWhiteSpace(host))
            host = "localhost";

        string portValue = Environment.GetEnvironmentVariable("MQTT_SERVICE_PORT");
        if (string.IsNullOrWhiteSpace(portValue))
            portValue = Environment.GetEnvironmentVariable("MOSQUITTO_SERVICE_PORT");

        if (!int.TryParse(portValue, out int port))
            port = 1883;

        return (host, port);
    }
}