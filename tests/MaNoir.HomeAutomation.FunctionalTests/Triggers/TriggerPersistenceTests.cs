using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Client;
using NATS.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Triggers;

[TestClass]
[DoNotParallelize]
public sealed class TriggerPersistenceTests
{
    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertAsync_AndDeleteAsync_ShouldPersistTriggerAndPublishCatalogChanges()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        TriggerLogic logic = new TriggerLogic();

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync(TriggerLogic.TriggerChangedTopic);
        connection.Flush();

        Trigger trigger = await logic.UpsertAsync(new Trigger()
        {
            Id = "wake-up",
            Kind = TriggerKind.Clock,
            Label = "Wake up",
            Offset = TimeSpan.FromHours(7)
        });

        Msg changedMessage = subscription.NextMessage(5000);
        string changedPayload = Encoding.UTF8.GetString(changedMessage.Data);
        List<Trigger> allTriggers = await logic.GetAllAsync();
        bool deleted = await logic.DeleteAsync("wake-up");
        Msg deletedMessage = subscription.NextMessage(5000);
        string deletedPayload = Encoding.UTF8.GetString(deletedMessage.Data);

        Assert.IsNotNull(trigger);
        Assert.AreEqual("wake-up", trigger.Id);
        Assert.HasCount(1, allTriggers);
        Assert.AreEqual(TriggerLogic.TriggerChangedTopic, changedMessage.Subject);
        StringAssert.Contains(changedPayload, "Triggers changed");
        Assert.IsTrue(deleted);
        Assert.AreEqual(TriggerLogic.TriggerChangedTopic, deletedMessage.Subject);
        StringAssert.Contains(deletedPayload, "Triggers changed");
        Assert.HasCount(0, await logic.GetAllAsync());
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task RaiseAsync_ShouldUpdateLatestOccurrenceAndPublishLegacyMessages()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        await using MosquittoFunctionalTestHost mqttHost = new MosquittoFunctionalTestHost();
        await mqttHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", mqttHost.Host);
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", mqttHost.Port.ToString());
        using ProcessEnvironmentVariableScope mosquittoHostScope = new ProcessEnvironmentVariableScope("MOSQUITTO_SERVICE_HOST", null);
        using ProcessEnvironmentVariableScope mosquittoPortScope = new ProcessEnvironmentVariableScope("MOSQUITTO_SERVICE_PORT", null);

        TriggerLogic logic = new TriggerLogic();
        await logic.UpsertAsync(new Trigger()
        {
            Id = "doorbell",
            Kind = TriggerKind.Webhook,
            Label = "Door bell",
            RaisedMessages = new List<TriggerRaisedMessage>()
            {
                new TriggerRaisedMessage()
                {
                    MessageTopic = "tests.trigger.raised",
                    MessageContent = "source={{source}};data={{rawdata}}"
                }
            }
        });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync("tests.trigger.raised");
        connection.Flush();

        MqttFactory mqttFactory = new MqttFactory();
        using IMqttClient client = mqttFactory.CreateMqttClient();
        TaskCompletionSource<string> mqttDescription = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == "manoir/mesh/triggers/doorbell/desc")
                mqttDescription.TrySetResult(Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()));
            return Task.CompletedTask;
        };
        await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId("functional-trigger-mqtt-subscriber")
            .WithTcpServer(mqttHost.Host, mqttHost.Port)
            .Build());
        await client.SubscribeAsync("manoir/mesh/triggers/doorbell/desc");

        bool raised = await logic.RaiseAsync("doorbell", "sarah", "ding");
        Msg raisedMessage = subscription.NextMessage(5000);
        string raisedPayload = Encoding.UTF8.GetString(raisedMessage.Data);
        string mqttPayload = await mqttDescription.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Trigger updatedTrigger = await logic.GetByIdAsync("doorbell");

        Assert.IsTrue(raised);
        Assert.AreEqual("tests.trigger.raised", raisedMessage.Subject);
        Assert.AreEqual("source=sarah;data=ding", raisedPayload);
        Assert.AreEqual("Webhook", mqttPayload);
        Assert.IsNotNull(updatedTrigger);
        Assert.IsTrue(updatedTrigger.LatestOccurence.HasValue);

        await client.DisconnectAsync();
    }
}