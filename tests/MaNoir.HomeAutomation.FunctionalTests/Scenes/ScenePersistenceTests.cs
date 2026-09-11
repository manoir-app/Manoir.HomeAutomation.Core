using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.Core.Files;
using MaNoir.Agents.Sarah;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Client;
using NATS.Client;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaNoir.HomeAutomation.FunctionalTests.Scenes;

[TestClass]
[DoNotParallelize]
public sealed class ScenePersistenceTests
{
    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertGroupAsync_AndGetGroupsAsync_ShouldPersistAndFilterRemoteGroups()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room", Order = 2, VisibleInRemote = true });
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "private-room", Label = "Private room", Order = 1, VisibleInRemote = false });

        List<SceneGroup> allGroups = await logic.GetGroupsAsync();
        List<SceneGroup> remoteGroups = await logic.GetGroupsAsync(true);

        Assert.HasCount(2, allGroups);
        Assert.AreEqual("private-room", allGroups[0].Id);
        Assert.AreEqual("living-room", allGroups[1].Id);
        Assert.HasCount(1, remoteGroups);
        Assert.AreEqual("living-room", remoteGroups[0].Id);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertSceneAsync_AndGetScenesAsync_ShouldPersistAndFilterRemoteScenes()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room", VisibleInRemote = true });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie", OrderInGroup = 2, VisibleInRemote = true });
        await logic.UpsertSceneAsync(new Scene() { Id = "cleanup", GroupId = "living-room", Label = "Cleanup", OrderInGroup = 1, VisibleInRemote = false });

        List<Scene> allScenes = await logic.GetScenesAsync("living-room");
        List<Scene> remoteScenes = await logic.GetScenesAsync("living-room", true);

        Assert.HasCount(2, allScenes);
        Assert.AreEqual("cleanup", allScenes[0].Id);
        Assert.AreEqual("movie", allScenes[1].Id);
        Assert.HasCount(1, remoteScenes);
        Assert.AreEqual("movie", remoteScenes[0].Id);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpdateActiveScenesForGroupAsync_WhenOnlyRemote_ShouldIgnoreNonRemoteScenes()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room", VisibleInRemote = true });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie", VisibleInRemote = true });
        await logic.UpsertSceneAsync(new Scene() { Id = "service", GroupId = "living-room", Label = "Service", VisibleInRemote = false });

        SceneGroup updatedGroup = await logic.UpdateActiveScenesForGroupAsync("living-room", new[] { "movie", "service" }, true);

        Assert.IsNotNull(updatedGroup);
        CollectionAssert.AreEqual(new[] { "movie" }, updatedGroup.CurrentActiveScenes);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task DeleteSceneAsync_ShouldRemoveActiveReferenceAndPublishContentChangedMessage()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room", CurrentActiveScenes = ["movie"] });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie" });
        await logic.UpdateActiveScenesForGroupAsync("living-room", new[] { "movie" });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync(ScenarioContentChangedMessage.ScenarioChangedTopic);
        connection.Flush();

        bool deleted = await logic.DeleteSceneAsync("movie");
        Msg message = subscription.NextMessage(5000);
        ScenarioContentChangedMessage published = BaseMessage.ReadAs<ScenarioContentChangedMessage>(Encoding.UTF8.GetString(message.Data));
        SceneGroup group = await logic.GetGroupAsync("living-room");
        Scene deletedScene = await logic.GetSceneByIdAsync("movie");

        Assert.IsTrue(deleted);
        Assert.IsNotNull(published);
        Assert.AreEqual("living-room", published.SceneGroupId);
        Assert.AreEqual("movie", published.SceneId);
        Assert.IsNotNull(group);
        Assert.HasCount(0, group.CurrentActiveScenes);
        Assert.IsNull(deletedScene);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ExecuteSceneAsync_ShouldPublishLegacyExecuteMessage()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie" });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync("homeautomation.scenario.execute");
        connection.Flush();

        bool executed = await logic.ExecuteSceneAsync("movie");
        Msg message = subscription.NextMessage(5000);
        ExecuteScenarioHomeAutomationMessage published = BaseMessage.ReadAs<ExecuteScenarioHomeAutomationMessage>(Encoding.UTF8.GetString(message.Data));

        Assert.IsTrue(executed);
        Assert.IsNotNull(published);
        Assert.AreEqual("homeautomation.scenario.execute", message.Subject);
        Assert.AreEqual("movie", published.SceneId);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_ShouldExecuteActivationStepsFromSceneMessage()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });
        await logic.UpsertSceneAsync(new Scene()
        {
            Id = "movie",
            GroupId = "living-room",
            Label = "Movie",
            ActivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Agent, Message = "lighting.living-room.movie", MessageBody = "{\"state\":\"on\"}" }
            ]
        });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync("lighting.living-room.movie");
        connection.Flush();

        SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance);
        SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
        string messageBody = JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage("movie"));
        MessageResponse response = router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.execute", messageBody);
        Msg published = subscription.NextMessage(5000);

        Assert.IsNotNull(response);
        Assert.AreEqual("lighting.living-room.movie", published.Subject);
        Assert.AreEqual("{\"state\":\"on\"}", Encoding.UTF8.GetString(published.Data));
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_WhenDeviceActionMatchesTrigger_ShouldExecuteScene()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });
        await logic.UpsertSceneAsync(new Scene()
        {
            Id = "dim-living-room",
            GroupId = "living-room",
            Label = "Dim living room",
            DetectionCriteria =
            [
                new SceneDetectionCriteria()
                {
                    DeviceActionTrigger = new SceneDetectionDeviceActionTrigger()
                    {
                        DeviceId = "living-room-dial",
                        ActionKind = "rotary",
                        Action = "rotate",
                        RequiredAttributes = { ["direction"] = "left" }
                    }
                }
            ],
            ActivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Agent, Message = "lighting.living-room.dim", MessageBody = "{\"level\":40}" }
            ]
        });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync("lighting.living-room.dim");
        connection.Flush();

        SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance);
        SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
        DeviceActionTriggeredMessage action = new DeviceActionTriggeredMessage()
        {
            DeviceId = "living-room-dial",
            ActionKind = "rotary",
            Action = "rotate",
            RawAction = "rotate_left",
            Attributes = { ["direction"] = "left", ["delta"] = "-1" }
        };

        MessageResponse response = router.HandleMessage(MessageOrigin.Local, DeviceActionTriggeredMessage.DeviceActionTriggered, JsonSerializer.Serialize(action));
        Msg published = subscription.NextMessage(5000);

        Assert.IsNotNull(response);
        Assert.AreEqual("lighting.living-room.dim", published.Subject);
        Assert.AreEqual("{\"level\":40}", Encoding.UTF8.GetString(published.Data));
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_WhenDeviceActionDoesNotMatchTrigger_ShouldNotExecuteScene()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });
        await logic.UpsertSceneAsync(new Scene()
        {
            Id = "dim-living-room",
            GroupId = "living-room",
            Label = "Dim living room",
            DetectionCriteria =
            [
                new SceneDetectionCriteria()
                {
                    DeviceActionTrigger = new SceneDetectionDeviceActionTrigger()
                    {
                        DeviceId = "living-room-dial",
                        Action = "rotate",
                        RequiredAttributes = { ["DIRECTION"] = "left" }
                    }
                }
            ],
            ActivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Agent, Message = "lighting.living-room.dim", MessageBody = "{\"level\":40}" }
            ]
        });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync("lighting.living-room.dim");
        connection.Flush();

        SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance);
        SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
        DeviceActionTriggeredMessage action = new DeviceActionTriggeredMessage()
        {
            DeviceId = "living-room-dial",
            Action = "rotate",
            Attributes = { ["direction"] = "right" }
        };

        MessageResponse response = router.HandleMessage(MessageOrigin.Local, DeviceActionTriggeredMessage.DeviceActionTriggered, JsonSerializer.Serialize(action));

        Assert.IsNotNull(response);
        Assert.ThrowsExactly<NATSTimeoutException>(() => subscription.NextMessage(250));
    }

    // Protocol-specific scene tests were removed with the legacy command transports.
#if false
    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_ShouldExecuteZigbee2MqttSwitchDeviceStep()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using MosquittoFunctionalTestHost mqttHost = new MosquittoFunctionalTestHost();
        await mqttHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", mqttHost.Host);
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", mqttHost.Port.ToString());

        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "kitchen-light-managed",
                DeviceInternalName = "kitchen-light",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch]
            }
        ]);
        SceneLogic sceneLogic = new SceneLogic();
        await sceneLogic.UpsertGroupAsync(new SceneGroup() { Id = "kitchen", Label = "Kitchen" });
        await sceneLogic.UpsertSceneAsync(new Scene()
        {
            Id = "kitchen-on",
            GroupId = "kitchen",
            Label = "Kitchen on",
            ActivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Device, TargetId = "kitchen-light-managed", Message = Device.HomeAutomationRoleSwitch, MessageBody = "on" }
            ]
        });

        MqttFactory factory = new MqttFactory();
        using IMqttClient subscriber = factory.CreateMqttClient();
        TaskCompletionSource<string> receivedPayload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == "zigbee2mqtt/kitchen-light/set")
                receivedPayload.TrySetResult(Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()));

            return Task.CompletedTask;
        };
        await subscriber.ConnectAsync(new MqttClientOptionsBuilder().WithClientId("scene-zigbee2mqtt-subscriber").WithTcpServer(mqttHost.Host, mqttHost.Port).Build());
        await subscriber.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("zigbee2mqtt/kitchen-light/set").Build());

        try
        {
            Zigbee2MqttCommandService commandService = new Zigbee2MqttCommandService(NullLogger<Zigbee2MqttCommandService>.Instance);
            SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance, commandService);
            SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
            MessageResponse response = router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.execute", JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage("kitchen-on")));
            string payload = await receivedPayload.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsNotNull(response);
            Assert.AreEqual("{\"state\":\"ON\"}", payload);
        }
        finally
        {
            await subscriber.DisconnectAsync();
        }
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_ShouldExecuteShellyGen1SwitchDeviceStep()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);

        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "kitchen-shelly-managed",
                DeviceInternalName = "shelly1-a1b2c3",
                DevicePlatform = "shelly-gen1",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch],
                ConfigurationData = "{\"ip\":\"192.168.1.10\"}"
            }
        ]);
        SceneLogic sceneLogic = new SceneLogic();
        await sceneLogic.UpsertGroupAsync(new SceneGroup() { Id = "kitchen", Label = "Kitchen" });
        await sceneLogic.UpsertSceneAsync(new Scene()
        {
            Id = "kitchen-shelly-on",
            GroupId = "kitchen",
            Label = "Kitchen Shelly on",
            ActivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Device, TargetId = "kitchen-shelly-managed", TargetDataName = "Relay 1", Message = Device.HomeAutomationRoleSwitch, MessageBody = "on" }
            ]
        });

        RecordingHttpMessageHandler handler = new RecordingHttpMessageHandler();
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen1CommandService commandService = new ShellyGen1CommandService(NullLogger<ShellyGen1CommandService>.Instance, httpClient);
        SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance, shellyGen1CommandService: commandService);
        SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
        router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.execute", JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage("kitchen-shelly-on")));

        Assert.IsNotNull(handler.RequestUri);
        Assert.AreEqual("http://192.168.1.10/relay/1?turn=on", handler.RequestUri.AbsoluteUri);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_ShouldExecuteShellyGen2SwitchDeviceStep()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);

        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "kitchen-shelly-gen2-managed",
                DeviceInternalName = "shellyplus2pm-a1b2c3",
                DevicePlatform = "shelly-gen2",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch],
                ConfigurationData = "{\"ip\":\"192.168.1.11\"}"
            }
        ]);
        SceneLogic sceneLogic = new SceneLogic();
        await sceneLogic.UpsertGroupAsync(new SceneGroup() { Id = "kitchen", Label = "Kitchen" });
        await sceneLogic.UpsertSceneAsync(new Scene()
        {
            Id = "kitchen-shelly-gen2-on",
            GroupId = "kitchen",
            Label = "Kitchen Shelly Gen2 on",
            ActivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Device, TargetId = "kitchen-shelly-gen2-managed", TargetDataName = "Switch 1", Message = Device.HomeAutomationRoleSwitch, MessageBody = "on" }
            ]
        });

        RecordingHttpMessageHandler handler = new RecordingHttpMessageHandler();
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen2CommandService commandService = new ShellyGen2CommandService(NullLogger<ShellyGen2CommandService>.Instance, httpClient);
        SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance, shellyGen2CommandService: commandService);
        SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
        MessageResponse response = router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.execute", JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage("kitchen-shelly-gen2-on")));

        Assert.IsNotNull(response);
        Assert.AreEqual("http://192.168.1.11/rpc", handler.RequestUri.AbsoluteUri);
        using JsonDocument request = JsonDocument.Parse(handler.RequestBody);
        Assert.AreEqual("Switch.Set", request.RootElement.GetProperty("method").GetString());
        Assert.AreEqual(1, request.RootElement.GetProperty("params").GetProperty("id").GetInt32());
        Assert.IsTrue(request.RootElement.GetProperty("params").GetProperty("on").GetBoolean());
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen2CommandService_ShouldControlLightAndCoverThroughRpc()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "living-room-shelly-gen2",
                DeviceInternalName = "shellyplus2pm-a1b2c3",
                DevicePlatform = "shelly-gen2",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch, Device.HomeAutomationRoleDimmer, Device.HomeAutomationMainRoleShutterSwitch],
                DeviceCapabilities = [Device.CapabilityShutterPosition],
                ConfigurationData = "{\"ip\":\"192.168.1.12\"}"
            }
        ]);

        RecordingHttpMessageHandler handler = new RecordingHttpMessageHandler();
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen2CommandService service = new ShellyGen2CommandService(NullLogger<ShellyGen2CommandService>.Instance, httpClient);

        Assert.IsTrue(await service.ExecuteAsync(new SceneStep() { TargetId = "living-room-shelly-gen2", TargetDataName = "Light 0", Message = Device.HomeAutomationRoleDimmer, MessageBody = "37.5" }));
        using (JsonDocument lightRequest = JsonDocument.Parse(handler.RequestBody))
        {
            Assert.AreEqual("Light.Set", lightRequest.RootElement.GetProperty("method").GetString());
            Assert.AreEqual(37.5M, lightRequest.RootElement.GetProperty("params").GetProperty("brightness").GetDecimal());
            Assert.IsTrue(lightRequest.RootElement.GetProperty("params").GetProperty("on").GetBoolean());
        }

        Assert.IsTrue(await service.ExecuteAsync(new SceneStep() { TargetId = "living-room-shelly-gen2", TargetDataName = "Cover 0", Message = Device.HomeAutomationMainRoleShutterSwitch, MessageBody = "62.5" }));
        using JsonDocument coverRequest = JsonDocument.Parse(handler.RequestBody);
        Assert.AreEqual("Cover.GoToPosition", coverRequest.RootElement.GetProperty("method").GetString());
        Assert.AreEqual(62.5M, coverRequest.RootElement.GetProperty("params").GetProperty("pos").GetDecimal());
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen2CommandService_ShouldSetRgbColorThroughRpc()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "living-room-rgb-gen2",
                DeviceInternalName = "shellyplusrgbwpm-a1b2c3",
                DevicePlatform = "shelly-gen2",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch, Device.HomeAutomationRoleDimmer, Device.HomeAutomationRoleColorBound],
                ConfigurationData = "{\"ip\":\"192.168.1.13\"}"
            }
        ]);

        RecordingHttpMessageHandler handler = new RecordingHttpMessageHandler();
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen2CommandService service = new ShellyGen2CommandService(NullLogger<ShellyGen2CommandService>.Instance, httpClient);

        Assert.IsTrue(await service.ExecuteAsync(new SceneStep() { TargetId = "living-room-rgb-gen2", TargetDataName = "RGB 0", Message = Device.HomeAutomationRoleColorBound, MessageBody = "{\"rgb\":\"#FF0010\"}" }));
        using JsonDocument request = JsonDocument.Parse(handler.RequestBody);
        Assert.AreEqual("RGB.Set", request.RootElement.GetProperty("method").GetString());
        Assert.IsTrue(request.RootElement.GetProperty("params").GetProperty("on").GetBoolean());
        CollectionAssert.AreEqual(new[] { 255, 0, 16 }, request.RootElement.GetProperty("params").GetProperty("rgb").EnumerateArray().Select(value => value.GetInt32()).ToArray());
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1CommandService_ShouldAddressGradientAndColorOutputs()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "shelly-color-managed",
                DeviceInternalName = "shellyrgbw2-a1b2c3",
                DevicePlatform = "shelly-gen1",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleDimmer, Device.HomeAutomationRoleColorBound],
                ConfigurationData = "{\"ip\":\"192.168.1.10\"}"
            }
        ]);

        RecordingHttpMessageHandler handler = new RecordingHttpMessageHandler();
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen1CommandService commandService = new ShellyGen1CommandService(NullLogger<ShellyGen1CommandService>.Instance, httpClient);

        bool gradientExecuted = await commandService.ExecuteAsync(new SceneStep() { TargetId = "shelly-color-managed", TargetDataName = "White 2", Message = Device.HomeAutomationRoleDimmer, MessageBody = "37.5" });
        Assert.IsTrue(gradientExecuted);
        Assert.AreEqual("http://192.168.1.10/white/2?turn=on&brightness=37.5", handler.RequestUri.AbsoluteUri);

        bool colorExecuted = await commandService.ExecuteAsync(new SceneStep() { TargetId = "shelly-color-managed", TargetDataName = "Color 0", Message = Device.HomeAutomationRoleColorBound, MessageBody = "{\"rgb\":\"#FF0010\"}" });
        Assert.IsTrue(colorExecuted);
        Assert.AreEqual("http://192.168.1.10/color/0?red=255&green=0&blue=16&turn=on", handler.RequestUri.AbsoluteUri);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1CommandService_ShouldControlRollerAndTargetPosition()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "living-room-cover",
                DeviceInternalName = "shellyswitch25-a1b2c3",
                DevicePlatform = "shelly-gen1",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationMainRoleShutterSwitch],
                DeviceCapabilities = [Device.CapabilityShutterPosition],
                ConfigurationData = "{\"ip\":\"192.168.1.10\"}"
            }
        ]);

        RecordingHttpMessageHandler handler = new RecordingHttpMessageHandler();
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen1CommandService commandService = new ShellyGen1CommandService(NullLogger<ShellyGen1CommandService>.Instance, httpClient);

        bool closeExecuted = await commandService.ExecuteAsync(new SceneStep() { TargetId = "living-room-cover", Message = Device.HomeAutomationMainRoleShutterSwitch, MessageBody = "close" });
        Assert.IsTrue(closeExecuted);
        Assert.AreEqual("http://192.168.1.10/roller/0?go=close", handler.RequestUri.AbsoluteUri);

        bool positionExecuted = await commandService.ExecuteAsync(new SceneStep() { TargetId = "living-room-cover", TargetDataName = "Cover 0", Message = Device.HomeAutomationMainRoleShutterSwitch, MessageBody = "62.5" });
        Assert.IsTrue(positionExecuted);
        Assert.AreEqual("http://192.168.1.10/roller/0?go=to_pos&roller_pos=62.5", handler.RequestUri.AbsoluteUri);

        await new DeviceLogic().SetCapabilityAsync("living-room-cover", Device.CapabilityShutterPosition, false);
        bool unsupportedPositionExecuted = await commandService.ExecuteAsync(new SceneStep() { TargetId = "living-room-cover", Message = Device.HomeAutomationMainRoleShutterSwitch, MessageBody = "50" });
        Assert.IsFalse(unsupportedPositionExecuted);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public Uri RequestUri { get; private set; }
        public string RequestBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_ShouldExecuteZigbee2MqttDimmerDeviceStep()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using MosquittoFunctionalTestHost mqttHost = new MosquittoFunctionalTestHost();
        await mqttHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", mqttHost.Host);
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", mqttHost.Port.ToString());

        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "kitchen-dimmer-managed",
                DeviceInternalName = "kitchen-dimmer",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleDimmer]
            }
        ]);
        SceneLogic sceneLogic = new SceneLogic();
        await sceneLogic.UpsertGroupAsync(new SceneGroup() { Id = "kitchen", Label = "Kitchen" });
        await sceneLogic.UpsertSceneAsync(new Scene()
        {
            Id = "kitchen-dim",
            GroupId = "kitchen",
            Label = "Kitchen dim",
            ActivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Device, TargetId = "kitchen-dimmer-managed", Message = Device.HomeAutomationRoleDimmer, MessageBody = "50" }
            ]
        });

        MqttFactory factory = new MqttFactory();
        using IMqttClient subscriber = factory.CreateMqttClient();
        TaskCompletionSource<string> receivedPayload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == "zigbee2mqtt/kitchen-dimmer/set")
                receivedPayload.TrySetResult(Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()));

            return Task.CompletedTask;
        };
        await subscriber.ConnectAsync(new MqttClientOptionsBuilder().WithClientId("scene-zigbee2mqtt-dimmer-subscriber").WithTcpServer(mqttHost.Host, mqttHost.Port).Build());
        await subscriber.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("zigbee2mqtt/kitchen-dimmer/set").Build());

        try
        {
            Zigbee2MqttCommandService commandService = new Zigbee2MqttCommandService(NullLogger<Zigbee2MqttCommandService>.Instance);
            SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance, commandService);
            SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
            router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.execute", JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage("kitchen-dim")));
            string payload = await receivedPayload.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual("{\"brightness\":127}", payload);
        }
        finally
        {
            await subscriber.DisconnectAsync();
        }
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_ShouldExecuteZigbee2MqttXyColorDeviceStep()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using MosquittoFunctionalTestHost mqttHost = new MosquittoFunctionalTestHost();
        await mqttHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", mqttHost.Host);
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", mqttHost.Port.ToString());

        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "kitchen-color-managed",
                DeviceInternalName = "kitchen-color",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleColorBound],
                DeviceCapabilities = [Device.CapabilityColorXy]
            }
        ]);
        SceneLogic sceneLogic = new SceneLogic();
        await sceneLogic.UpsertGroupAsync(new SceneGroup() { Id = "kitchen", Label = "Kitchen" });
        await sceneLogic.UpsertSceneAsync(new Scene()
        {
            Id = "kitchen-color",
            GroupId = "kitchen",
            Label = "Kitchen color",
            ActivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Device, TargetId = "kitchen-color-managed", Message = Device.HomeAutomationRoleColorBound, MessageBody = "{\"rgb\":\"#FF0000\"}" }
            ]
        });

        MqttFactory factory = new MqttFactory();
        using IMqttClient subscriber = factory.CreateMqttClient();
        TaskCompletionSource<string> receivedPayload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == "zigbee2mqtt/kitchen-color/set")
                receivedPayload.TrySetResult(Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()));

            return Task.CompletedTask;
        };
        await subscriber.ConnectAsync(new MqttClientOptionsBuilder().WithClientId("scene-zigbee2mqtt-color-subscriber").WithTcpServer(mqttHost.Host, mqttHost.Port).Build());
        await subscriber.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("zigbee2mqtt/kitchen-color/set").Build());

        try
        {
            Zigbee2MqttCommandService commandService = new Zigbee2MqttCommandService(NullLogger<Zigbee2MqttCommandService>.Instance);
            SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance, commandService);
            SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
            router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.execute", JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage("kitchen-color")));
            string payload = await receivedPayload.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement color = document.RootElement.GetProperty("color");
            Assert.AreEqual(0.6401D, color.GetProperty("x").GetDouble(), 0.0001D);
            Assert.AreEqual(0.33D, color.GetProperty("y").GetDouble(), 0.0001D);
        }
        finally
        {
            await subscriber.DisconnectAsync();
        }
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_ShouldExecuteZigbee2MqttHsColorDeviceStep()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using MosquittoFunctionalTestHost mqttHost = new MosquittoFunctionalTestHost();
        await mqttHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", mqttHost.Host);
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", mqttHost.Port.ToString());

        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "kitchen-hs-color-managed",
                DeviceInternalName = "kitchen-hs-color",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleColorBound],
                DeviceCapabilities = [Device.CapabilityColorHs]
            }
        ]);
        SceneLogic sceneLogic = new SceneLogic();
        await sceneLogic.UpsertGroupAsync(new SceneGroup() { Id = "kitchen", Label = "Kitchen" });
        await sceneLogic.UpsertSceneAsync(new Scene()
        {
            Id = "kitchen-hs-color",
            GroupId = "kitchen",
            Label = "Kitchen HS color",
            ActivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Device, TargetId = "kitchen-hs-color-managed", Message = Device.HomeAutomationRoleColorBound, MessageBody = "{\"rgb\":\"#00FF00\"}" }
            ]
        });

        MqttFactory factory = new MqttFactory();
        using IMqttClient subscriber = factory.CreateMqttClient();
        TaskCompletionSource<string> receivedPayload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == "zigbee2mqtt/kitchen-hs-color/set")
                receivedPayload.TrySetResult(Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()));

            return Task.CompletedTask;
        };
        await subscriber.ConnectAsync(new MqttClientOptionsBuilder().WithClientId("scene-zigbee2mqtt-hs-color-subscriber").WithTcpServer(mqttHost.Host, mqttHost.Port).Build());
        await subscriber.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("zigbee2mqtt/kitchen-hs-color/set").Build());

        try
        {
            Zigbee2MqttCommandService commandService = new Zigbee2MqttCommandService(NullLogger<Zigbee2MqttCommandService>.Instance);
            SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance, commandService);
            SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
            router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.execute", JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage("kitchen-hs-color")));
            string payload = await receivedPayload.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement color = document.RootElement.GetProperty("color");
            Assert.AreEqual(120D, color.GetProperty("hue").GetDouble(), 0.0001D);
            Assert.AreEqual(100D, color.GetProperty("saturation").GetDouble(), 0.0001D);
        }
        finally
        {
            await subscriber.DisconnectAsync();
        }
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_ShouldExecuteZigbee2MqttColorTemperatureDeviceStep()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using MosquittoFunctionalTestHost mqttHost = new MosquittoFunctionalTestHost();
        await mqttHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", mqttHost.Host);
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", mqttHost.Port.ToString());

        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "kitchen-color-temperature-managed",
                DeviceInternalName = "kitchen-color-temperature",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleColorBound],
                DeviceCapabilities = [Device.CapabilityColorTemperature],
                ConfigurationData = """
                {
                    "definition":
                    {
                        "exposes":
                        [
                            { "property": "color_temp", "value_min": 200, "value_max": 370 }
                        ]
                    }
                }
                """
            }
        ]);
        SceneLogic sceneLogic = new SceneLogic();
        await sceneLogic.UpsertGroupAsync(new SceneGroup() { Id = "kitchen", Label = "Kitchen" });
        await sceneLogic.UpsertSceneAsync(new Scene()
        {
            Id = "kitchen-color-temperature",
            GroupId = "kitchen",
            Label = "Kitchen color temperature",
            ActivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Device, TargetId = "kitchen-color-temperature-managed", Message = Device.HomeAutomationRoleColorBound, MessageBody = "{\"temperatureKelvin\":6500}" }
            ]
        });

        MqttFactory factory = new MqttFactory();
        using IMqttClient subscriber = factory.CreateMqttClient();
        TaskCompletionSource<string> receivedPayload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == "zigbee2mqtt/kitchen-color-temperature/set")
                receivedPayload.TrySetResult(Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()));

            return Task.CompletedTask;
        };
        await subscriber.ConnectAsync(new MqttClientOptionsBuilder().WithClientId("scene-zigbee2mqtt-color-temperature-subscriber").WithTcpServer(mqttHost.Host, mqttHost.Port).Build());
        await subscriber.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("zigbee2mqtt/kitchen-color-temperature/set").Build());

        try
        {
            Zigbee2MqttCommandService commandService = new Zigbee2MqttCommandService(NullLogger<Zigbee2MqttCommandService>.Instance);
            SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance, commandService);
            SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
            router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.execute", JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage("kitchen-color-temperature")));
            string payload = await receivedPayload.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual("{\"color_temp\":200}", payload);
        }
        finally
        {
            await subscriber.DisconnectAsync();
        }
    }

#endif

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_ShouldExecuteDeactivationStepsFromSceneMessage()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic logic = new SceneLogic();
    await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room", CurrentActiveScenes = ["movie"] });
        await logic.UpsertSceneAsync(new Scene()
        {
            Id = "movie",
            GroupId = "living-room",
            Label = "Movie",
            DeactivationSteps =
            [
                new SceneStep() { TargetKind = SceneStepTargetKind.Agent, Message = "lighting.living-room.restore", MessageBody = "{\"state\":\"off\"}" }
            ]
        });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync("lighting.living-room.restore");
        connection.Flush();

        SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance);
        SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
        string messageBody = JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetDisableMessage("movie"));
        MessageResponse response = router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.disable", messageBody);
        Msg published = subscription.NextMessage(5000);
        SceneGroup updatedGroup = await logic.GetGroupAsync("living-room");

        Assert.IsNotNull(response);
        Assert.AreEqual("lighting.living-room.restore", published.Subject);
        Assert.AreEqual("{\"state\":\"off\"}", Encoding.UTF8.GetString(published.Data));
        Assert.HasCount(0, updatedGroup.CurrentActiveScenes);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_WhenGroupIsExclusive_ShouldDeactivateCurrentSceneAndReplaceActiveState()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room", SceneIsExclusive = true, CurrentActiveScenes = ["movie"] });
        await logic.UpsertSceneAsync(new Scene()
        {
            Id = "movie",
            GroupId = "living-room",
            Label = "Movie",
            DeactivationSteps = [new SceneStep() { TargetKind = SceneStepTargetKind.Agent, Message = "lighting.movie.restore", MessageBody = "off" }]
        });
        await logic.UpsertSceneAsync(new Scene()
        {
            Id = "dinner",
            GroupId = "living-room",
            Label = "Dinner",
            ActivationSteps = [new SceneStep() { TargetKind = SceneStepTargetKind.Agent, Message = "lighting.dinner.activate", MessageBody = "on" }]
        });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription deactivationSubscription = connection.SubscribeSync("lighting.movie.restore");
        using ISyncSubscription activationSubscription = connection.SubscribeSync("lighting.dinner.activate");
        connection.Flush();

        SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance);
        SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
        string messageBody = JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage("dinner"));
        MessageResponse response = router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.execute", messageBody);
        Msg deactivation = deactivationSubscription.NextMessage(5000);
        Msg activation = activationSubscription.NextMessage(5000);
        SceneGroup updatedGroup = await logic.GetGroupAsync("living-room");

        Assert.IsNotNull(response);
        Assert.AreEqual("off", Encoding.UTF8.GetString(deactivation.Data));
        Assert.AreEqual("on", Encoding.UTF8.GetString(activation.Data));
        CollectionAssert.AreEqual(new[] { "dinner" }, updatedGroup.CurrentActiveScenes);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SarahMessageRouter_WhenGroupIsNotExclusive_ShouldKeepExistingActiveScenes()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room", CurrentActiveScenes = ["movie"] });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie" });
        await logic.UpsertSceneAsync(new Scene() { Id = "dinner", GroupId = "living-room", Label = "Dinner" });

        SceneExecutionService executor = new SceneExecutionService(NullLogger<SceneExecutionService>.Instance);
        SarahMessageRouter router = new SarahMessageRouter(new SarahRuntime(), executor);
        string messageBody = JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage("dinner"));
        MessageResponse response = router.HandleMessage(MessageOrigin.Local, "homeautomation.scenario.execute", messageBody);
        SceneGroup updatedGroup = await logic.GetGroupAsync("living-room");

        Assert.IsNotNull(response);
        CollectionAssert.AreEqual(new[] { "movie", "dinner" }, updatedGroup.CurrentActiveScenes);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertImageAsync_AndDeleteImageAsync_ShouldStorePublicPngAndUpdateSceneImages()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();

        string tempFolder = Path.Combine(Path.GetTempPath(), "manoir-homeautomation-scenes-tests", Guid.NewGuid().ToString("N"));

        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);
        using ProcessEnvironmentVariableScope fileScope = new ProcessEnvironmentVariableScope("MANOIR_FILE_STORAGE_FOLDER", tempFolder);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie" });

        await using MemoryStream uploadStream = CreateJpegImageStream();

        Scene storedScene = await logic.UpsertImageAsync("movie", "Banner", uploadStream);

        string localFile = Path.Combine(tempFolder, "public", "home-automation", "images", "scenes", "movie", "banner.png");
        Assert.IsNotNull(storedScene);
        Assert.IsTrue(File.Exists(localFile));
        Assert.IsTrue(storedScene.Images.ContainsKey("banner"));
        Assert.AreEqual("/api/core/files/public/home-automation/images/scenes/movie/banner.png", storedScene.Images["banner"]);
        Assert.AreEqual("image/png", FileStorageHelper.GetStoredFileMetadata(localFile)?.ContentType);

        Scene updatedScene = await logic.DeleteImageAsync("movie", "banner");

        Assert.IsNotNull(updatedScene);
        Assert.IsFalse(File.Exists(localFile));
        Assert.IsFalse(updatedScene.Images.ContainsKey("banner"));

        if (Directory.Exists(tempFolder))
            Directory.Delete(tempFolder, true);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertSceneAsync_WithoutCustomImages_ShouldGenerateBundledIconAndBanner()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();

        string tempFolder = Path.Combine(Path.GetTempPath(), "manoir-homeautomation-scenes-generated-tests", Guid.NewGuid().ToString("N"));

        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);
        using ProcessEnvironmentVariableScope fileScope = new ProcessEnvironmentVariableScope("MANOIR_FILE_STORAGE_FOLDER", tempFolder);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });

        Scene storedScene = await logic.UpsertSceneAsync(new Scene() { Id = "movie-night", GroupId = "living-room", Label = "Movie Night" });

        string iconFile = Path.Combine(tempFolder, "public", "home-automation", "images", "scenes", "movie-night", "icon.png");
        string bannerFile = Path.Combine(tempFolder, "public", "home-automation", "images", "scenes", "movie-night", "banner.png");

        Assert.IsNotNull(storedScene);
        Assert.IsTrue(File.Exists(iconFile));
        Assert.IsTrue(File.Exists(bannerFile));
        Assert.AreEqual("/api/core/files/public/home-automation/images/scenes/movie-night/icon.png", storedScene.IconUrl);
        Assert.AreEqual("/api/core/files/public/home-automation/images/scenes/movie-night/banner.png", storedScene.BannerUrl);
        Assert.AreEqual(storedScene.IconUrl, storedScene.Images["icon"]);
        Assert.AreEqual(storedScene.BannerUrl, storedScene.Images["banner"]);
        Assert.AreEqual("image/png", FileStorageHelper.GetStoredFileMetadata(iconFile)?.ContentType);
        Assert.AreEqual("image/png", FileStorageHelper.GetStoredFileMetadata(bannerFile)?.ContentType);

        if (Directory.Exists(tempFolder))
            Directory.Delete(tempFolder, true);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertSceneAsync_WhenLabelChangesAndImagesAreGenerated_ShouldRegenerateVisuals()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();

        string tempFolder = Path.Combine(Path.GetTempPath(), "manoir-homeautomation-scenes-regenerated-tests", Guid.NewGuid().ToString("N"));

        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);
        using ProcessEnvironmentVariableScope fileScope = new ProcessEnvironmentVariableScope("MANOIR_FILE_STORAGE_FOLDER", tempFolder);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });

        Scene createdScene = await logic.UpsertSceneAsync(new Scene() { Id = "movie-night", GroupId = "living-room", Label = "Movie Night" });

        string iconFile = Path.Combine(tempFolder, "public", "home-automation", "images", "scenes", "movie-night", "icon.png");
        string bannerFile = Path.Combine(tempFolder, "public", "home-automation", "images", "scenes", "movie-night", "banner.png");
        byte[] initialIcon = File.ReadAllBytes(iconFile);
        byte[] initialBanner = File.ReadAllBytes(bannerFile);

        Scene updatedScene = await logic.UpsertSceneAsync(new Scene()
        {
            Id = "movie-night",
            GroupId = "living-room",
            Label = "Cinema Mode"
        });

        byte[] updatedIcon = File.ReadAllBytes(iconFile);
        byte[] updatedBanner = File.ReadAllBytes(bannerFile);

        Assert.IsNotNull(createdScene);
        Assert.IsNotNull(updatedScene);
        CollectionAssert.AreNotEqual(initialIcon, updatedIcon);
        CollectionAssert.AreNotEqual(initialBanner, updatedBanner);
        Assert.AreEqual("/api/core/files/public/home-automation/images/scenes/movie-night/icon.png", updatedScene.IconUrl);
        Assert.AreEqual("/api/core/files/public/home-automation/images/scenes/movie-night/banner.png", updatedScene.BannerUrl);

        if (Directory.Exists(tempFolder))
            Directory.Delete(tempFolder, true);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertSceneAsync_WhenBannerWasCustomized_ShouldNotOverwriteCustomBannerOnLabelChange()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();

        string tempFolder = Path.Combine(Path.GetTempPath(), "manoir-homeautomation-scenes-custom-banner-tests", Guid.NewGuid().ToString("N"));

        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);
        using ProcessEnvironmentVariableScope fileScope = new ProcessEnvironmentVariableScope("MANOIR_FILE_STORAGE_FOLDER", tempFolder);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie-night", GroupId = "living-room", Label = "Movie Night" });

        await using MemoryStream customBanner = CreateJpegImageStream();
        Assert.IsTrue(customBanner.Length > 0);
        customBanner.Position = 0;
        Scene customizedScene = await logic.UpsertImageAsync("movie-night", "banner", customBanner);

        string bannerFile = Path.Combine(tempFolder, "public", "home-automation", "images", "scenes", "movie-night", "banner.png");
        byte[] customBannerBytes = File.ReadAllBytes(bannerFile);

        Scene updatedScene = await logic.UpsertSceneAsync(new Scene()
        {
            Id = "movie-night",
            GroupId = "living-room",
            Label = "Cinema Mode"
        });

        byte[] bannerAfterLabelChange = File.ReadAllBytes(bannerFile);

        Assert.IsNotNull(customizedScene);
        Assert.IsNotNull(updatedScene);
        CollectionAssert.AreEqual(customBannerBytes, bannerAfterLabelChange);
        Assert.AreEqual("/api/core/files/public/home-automation/images/scenes/movie-night/banner.png", updatedScene.BannerUrl);

        if (Directory.Exists(tempFolder))
            Directory.Delete(tempFolder, true);
    }

    private static MemoryStream CreateJpegImageStream()
    {
        MemoryStream stream = new MemoryStream();
        using SKBitmap bitmap = new SKBitmap(1, 1);
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }
}