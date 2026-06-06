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
using NATS.Client.Rx;

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

    [TestMethod]
    [TestCategory("Functional")]
    public async Task RaiseAsync_ShouldPublishConditionedMessage_WhenSceneIsActive()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic sceneLogic = new SceneLogic();
        await sceneLogic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });
        await sceneLogic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie" });
        await sceneLogic.UpdateActiveScenesForGroupAsync("living-room", ["movie"]);

        TriggerLogic logic = new TriggerLogic();
        await logic.UpsertAsync(new Trigger()
        {
            Id = "scene-check",
            Kind = TriggerKind.Webhook,
            RaisedMessages = new List<TriggerRaisedMessage>()
            {
                new TriggerRaisedMessage()
                {
                    Condition = new Condition()
                    {
                        Kind = ConditionKind.SceneCheck,
                        ElementId = "movie",
                        PropertyName = "is-active"
                    },
                    MessageTopic = "tests.trigger.scene.active",
                    MessageContent = "scene-active"
                }
            }
        });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync("tests.trigger.scene.active");
        connection.Flush();

        bool raised = await logic.RaiseAsync("scene-check");
        Msg published = subscription.NextMessage(5000);

        Assert.IsTrue(raised);
        Assert.AreEqual("tests.trigger.scene.active", published.Subject);
        Assert.AreEqual("scene-active", Encoding.UTF8.GetString(published.Data));
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task RaiseAsync_ShouldSkipOrPublishConditionedMessage_DependingOnDeviceState()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "kitchen-light",
                DeviceInternalName = "kitchen-light",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch],
                Datas =
                [
                    new DeviceData() { Name = "Switch", Value = "off", StandardDataType = DeviceData.DataTypeSwitch }
                ]
            }
        ]);

        TriggerLogic logic = new TriggerLogic();
        await logic.UpsertAsync(new Trigger()
        {
            Id = "device-check",
            Kind = TriggerKind.Webhook,
            RaisedMessages = new List<TriggerRaisedMessage>()
            {
                new TriggerRaisedMessage()
                {
                    Condition = new Condition()
                    {
                        Kind = ConditionKind.DeviceCheck,
                        ElementId = "kitchen-light",
                        PropertyName = "Switch",
                        Operator = "==",
                        Value = "on"
                    },
                    MessageTopic = "tests.trigger.device.state",
                    MessageContent = "device-on"
                }
            }
        });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using IAsyncSubscription subscription = connection.SubscribeAsync("tests.trigger.device.state");
        TaskCompletionSource<string> publishedPayload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        subscription.MessageHandler += (_, args) => publishedPayload.TrySetResult(Encoding.UTF8.GetString(args.Message.Data));
        subscription.Start();
        connection.Flush();

        bool firstRaise = await logic.RaiseAsync("device-check");
        bool timedOut = false;
        try
        {
            await publishedPayload.Task.WaitAsync(TimeSpan.FromMilliseconds(750));
        }
        catch (TimeoutException)
        {
            timedOut = true;
        }

        await deviceLogic.ChangeDataAsync("kitchen-light", new DeviceData() { Name = "Switch", Value = "on", StandardDataType = DeviceData.DataTypeSwitch });

        bool secondRaise = await logic.RaiseAsync("device-check");
        string payload = await publishedPayload.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(firstRaise);
        Assert.IsTrue(timedOut);
        Assert.IsTrue(secondRaise);
        Assert.AreEqual("device-on", payload);
    }
}