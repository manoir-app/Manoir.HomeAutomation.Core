using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.HomeAutomation.Api;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MQTTnet;
using MQTTnet.Client;
using NATS.Client;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Triggers;

[TestClass]
[DoNotParallelize]
public sealed class TriggerApiFunctionalTests
{
    [TestMethod]
    [TestCategory("Functional")]
    public async Task TriggerApi_ShouldCrudRaiseAndDeleteTrigger()
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

        await using WebApplication app = CreateApplication();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription changedSubscription = connection.SubscribeSync(TriggerLogic.TriggerChangedTopic);
        using ISyncSubscription raisedSubscription = connection.SubscribeSync("tests.trigger.api.raised");
        connection.Flush();

        MqttFactory mqttFactory = new MqttFactory();
        using IMqttClient mqttClient = mqttFactory.CreateMqttClient();
        TaskCompletionSource<string> mqttDescription = new(TaskCreationOptions.RunContinuationsAsynchronously);
        mqttClient.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == "manoir/mesh/triggers/alarm/desc")
                mqttDescription.TrySetResult(Encoding.UTF8.GetString(args.ApplicationMessage.Payload ?? Array.Empty<byte>()));
            return Task.CompletedTask;
        };
        await mqttClient.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId("functional-trigger-api-mqtt-subscriber")
            .WithTcpServer(mqttHost.Host, mqttHost.Port)
            .Build());
        await mqttClient.SubscribeAsync("manoir/mesh/triggers/alarm/desc");

        HttpResponseMessage upsertResponse = await client.PostAsJsonAsync("/v1.0/system/mesh/local/triggers", new Trigger()
        {
            Id = "alarm",
            Kind = TriggerKind.Webhook,
            Label = "Alarm",
            RaisedMessages = new List<TriggerRaisedMessage>()
            {
                new TriggerRaisedMessage() { MessageTopic = "tests.trigger.api.raised", MessageContent = "{{rawdata}}" }
            }
        });
        Trigger storedTrigger = await upsertResponse.Content.ReadFromJsonAsync<Trigger>();
        Msg changedMessage = changedSubscription.NextMessage(5000);

        HttpResponseMessage getAllResponse = await client.GetAsync("/v1.0/system/mesh/local/triggers");
        List<Trigger> triggers = await getAllResponse.Content.ReadFromJsonAsync<List<Trigger>>();

        HttpResponseMessage settingsResponse = await client.GetAsync("/v1.0/system/mesh/local/triggers/alarm/settings?probableNextOccurrence=2032-01-01T06:30:00Z");
        bool settingsUpdated = await settingsResponse.Content.ReadFromJsonAsync<bool>();

        using HttpRequestMessage raiseRequest = new(HttpMethod.Post, "/v1.0/system/mesh/local/triggers/alarm/raise")
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain")
        };
        HttpResponseMessage raiseResponse = await client.SendAsync(raiseRequest);
        bool raised = await raiseResponse.Content.ReadFromJsonAsync<bool>();
        Msg raisedMessage = raisedSubscription.NextMessage(5000);
        string mqttPayload = await mqttDescription.Task.WaitAsync(TimeSpan.FromSeconds(5));

        HttpResponseMessage deleteResponse = await client.DeleteAsync("/v1.0/system/mesh/local/triggers/alarm");
        bool deleted = await deleteResponse.Content.ReadFromJsonAsync<bool>();
        Msg deletedMessage = changedSubscription.NextMessage(5000);
        HttpResponseMessage missingResponse = await client.GetAsync("/v1.0/system/mesh/local/triggers/alarm");

        Assert.AreEqual(HttpStatusCode.OK, upsertResponse.StatusCode);
        Assert.IsNotNull(storedTrigger);
        Assert.AreEqual("alarm", storedTrigger.Id);
        Assert.AreEqual(TriggerLogic.TriggerChangedTopic, changedMessage.Subject);
        Assert.AreEqual(HttpStatusCode.OK, getAllResponse.StatusCode);
        Assert.IsNotNull(triggers);
        Assert.HasCount(1, triggers);
        Assert.AreEqual(HttpStatusCode.OK, settingsResponse.StatusCode);
        Assert.IsTrue(settingsUpdated);
        Assert.AreEqual(HttpStatusCode.OK, raiseResponse.StatusCode);
        Assert.IsTrue(raised);
        Assert.AreEqual("payload", Encoding.UTF8.GetString(raisedMessage.Data));
        Assert.AreEqual("Webhook", mqttPayload);
        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.IsTrue(deleted);
        Assert.AreEqual(TriggerLogic.TriggerChangedTopic, deletedMessage.Subject);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);

        await mqttClient.DisconnectAsync();
    }

    private static WebApplication CreateApplication()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions() { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        HomeAutomationApiModule.ConfigureBuilder(builder);

        WebApplication app = builder.Build();
        HomeAutomationApiModule.ConfigureApplication(app);
        return app;
    }
}