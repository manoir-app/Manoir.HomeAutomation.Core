using Home.Common;
using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.Core.DataPublication;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Client;
using NATS.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Devices;

[TestClass]
[DoNotParallelize]
public sealed class DevicePublicationFunctionalTests
{
    [TestMethod]
    [TestCategory("Functional")]
    public async Task ChangeDataAsync_ShouldPublishProjectedEntityDataToMosquittoBroker()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using MosquittoFunctionalTestHost mqttHost = new MosquittoFunctionalTestHost();
        await mqttHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", mqttHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", mqttHost.Port.ToString());
        using ProcessEnvironmentVariableScope mosquittoHostScope = new ProcessEnvironmentVariableScope("MOSQUITTO_SERVICE_HOST", null);
        using ProcessEnvironmentVariableScope mosquittoPortScope = new ProcessEnvironmentVariableScope("MOSQUITTO_SERVICE_PORT", null);

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("agent-a",
        [
            new Device()
            {
                Id = "Kitchen-Light",
                DeviceInternalName = "kitchen-light",
                DeviceGivenName = "Kitchen light",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch],
                Datas =
                [
                    new DeviceData() { Name = "Switch", Value = "off", StandardDataType = DeviceData.DataTypeSwitch }
                ]
            }
        ]);

        MqttFactory factory = new MqttFactory();
        using IMqttClient client = factory.CreateMqttClient();
        TaskCompletionSource<string> receivedPayload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == "manoir/mesh/home-automation/kitchen-light/Switch")
            {
                receivedPayload.TrySetResult(Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()));
            }

            return Task.CompletedTask;
        };

        await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId("functional-device-mqtt-subscriber")
            .WithTcpServer(mqttHost.Host, mqttHost.Port)
            .Build());
        await client.SubscribeAsync("manoir/mesh/home-automation/kitchen-light/Switch");

        try
        {
            MqttDataPublisher.Start("functional-device-tests");

            bool changed = await deviceLogic.ChangeDataAsync("kitchen-light", new DeviceData()
            {
                Name = "Switch",
                Value = "on",
                StandardDataType = DeviceData.DataTypeSwitch
            }, "online");

            string payload = await receivedPayload.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(changed);
            Assert.AreEqual("on", payload);
        }
        finally
        {
            MqttDataPublisher.Stop();
            await client.DisconnectAsync();
        }
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task OnDeviceStateChangedAsync_ShouldPublishLegacyJsonToNatsBroker()
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
        await deviceLogic.RegisterDevicesAsync("agent-a",
        [
            new Device()
            {
                Id = "Kitchen-Light",
                DeviceInternalName = "kitchen-light",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch],
                Datas =
                [
                    new DeviceData() { Name = "Switch", Value = "off", StandardDataType = DeviceData.DataTypeSwitch }
                ]
            }
        ]);

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync(DeviceStateChangedMessage.DeviceStateChanged);
        connection.Flush();

        bool changed = await deviceLogic.OnDeviceStateChangedAsync("shelly", "kitchen-light", Device.HomeAutomationRoleSwitch, "online",
            new DeviceStateChangedMessage.DeviceStateValue() { Name = "Switch", Value = "on" });

        Msg message = subscription.NextMessage(5000);
        string json = Encoding.UTF8.GetString(message.Data);
        JObject payload = JObject.Parse(json);
        DeviceStateChangedMessage publishedMessage = BaseMessage.ReadAs<DeviceStateChangedMessage>(json);

        Assert.IsTrue(changed);
        Assert.AreEqual(DeviceStateChangedMessage.DeviceStateChanged, message.Subject);
        Assert.AreEqual(DeviceStateChangedMessage.DeviceStateChanged, payload.Value<string>("Topic"));
        Assert.AreEqual("shelly", publishedMessage.DevicePlatform);
        Assert.AreEqual("kitchen-light", publishedMessage.DeviceId);
        Assert.AreEqual(Device.HomeAutomationRoleSwitch, publishedMessage.DeviceRole);
        Assert.HasCount(1, publishedMessage.ChangedValues);
        Assert.AreEqual("Switch", publishedMessage.ChangedValues[0].Name);
        Assert.AreEqual("on", publishedMessage.ChangedValues[0].Value);
    }
}