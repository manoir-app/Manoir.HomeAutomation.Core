using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.HomeAutomation.Api;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NATS.Client;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Devices;

[TestClass]
[DoNotParallelize]
public sealed class DiscoveredDeviceApiFunctionalTests
{
    [TestMethod]
    [TestCategory("Functional")]
    public async Task DiscoveryApi_ShouldEnableUpsertListAndValidateAppDevice()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);

        await using WebApplication app = CreateApplication();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();

        HttpResponseMessage enableResponse = await client.PostAsync("/v1.0/devices/discovery/enabled", null);
        bool discoveryEnabled = await (await client.GetAsync("/v1.0/devices/discovery/enabled")).Content.ReadFromJsonAsync<bool>();

        HttpResponseMessage appDeviceResponse = await client.PostAsJsonAsync("/v1.0/devices/discovery/appdevice", new DevicesController.AppDevice());
        DiscoveredDevice appDiscoveredDevice = await appDeviceResponse.Content.ReadFromJsonAsync<DiscoveredDevice>();
        bool appDeviceAssociatedBefore = await (await client.GetAsync(string.Concat("/v1.0/devices/discovery/appdevice/", appDiscoveredDevice.Id, "/check"))).Content.ReadFromJsonAsync<bool>();

        HttpResponseMessage multipleResponse = await client.PostAsJsonAsync("/v1.0/devices/discovery/multiple",
        new[]
        {
            new DiscoveredDevice()
            {
                DeviceInternalName = "sensor-1",
                DeviceAgentId = "sarah",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = new List<string>() { "switch" }
            }
        });
        bool multipleInserted = await multipleResponse.Content.ReadFromJsonAsync<bool>();
        List<DiscoveredDevice> sarahDevices = await (await client.GetAsync("/v1.0/devices/discovery/agent/sarah/devices")).Content.ReadFromJsonAsync<List<DiscoveredDevice>>();

        HttpResponseMessage validateResponse = await client.PutAsync(string.Concat("/v1.0/devices/discovery/appdevice/", appDiscoveredDevice.Id, "/validate?deviceName=John%20phone"), null);
        Device createdDevice = await validateResponse.Content.ReadFromJsonAsync<Device>();
        bool appDeviceAssociatedAfter = await (await client.GetAsync(string.Concat("/v1.0/devices/discovery/appdevice/", appDiscoveredDevice.Id, "/check"))).Content.ReadFromJsonAsync<bool>();
        List<DiscoveredDevice> allDiscovered = await (await client.GetAsync("/v1.0/devices/discovered?agent=sarah&kind=homeautomation")).Content.ReadFromJsonAsync<List<DiscoveredDevice>>();

        Assert.AreEqual(HttpStatusCode.NoContent, enableResponse.StatusCode);
        Assert.IsTrue(discoveryEnabled);
        Assert.AreEqual(HttpStatusCode.OK, appDeviceResponse.StatusCode);
        Assert.IsNotNull(appDiscoveredDevice);
        Assert.AreEqual("webapp", appDiscoveredDevice.DevicePlatform);
        Assert.IsFalse(appDeviceAssociatedBefore);
        Assert.AreEqual(HttpStatusCode.OK, multipleResponse.StatusCode);
        Assert.IsTrue(multipleInserted);
        Assert.IsNotNull(sarahDevices);
        Assert.HasCount(1, sarahDevices);
        Assert.AreEqual("sensor-1", sarahDevices[0].DeviceInternalName);
        Assert.AreEqual(HttpStatusCode.OK, validateResponse.StatusCode);
        Assert.IsNotNull(createdDevice);
        Assert.AreEqual(appDiscoveredDevice.Id, createdDevice.Id);
        Assert.AreEqual("John phone", createdDevice.DeviceGivenName);
        Assert.IsTrue(appDeviceAssociatedAfter);
        Assert.IsNotNull(allDiscovered);
        Assert.HasCount(1, allDiscovered);
        Assert.AreEqual("sarah", allDiscovered[0].DeviceAgentId);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyOnboardingApi_ShouldPublishRequestedIpAddress()
    {
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);
        await using WebApplication app = CreateApplication();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();
        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync(ShellyOnboardingRequestedMessage.OnboardingRequested);
        connection.Flush();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/v1.0/devices/shelly/onboard",
            new DevicesController.ShellyOnboardingRequest() { IpAddress = "192.168.1.10" });
        Msg message = subscription.NextMessage(5000);
        ShellyOnboardingRequestedMessage request = BaseMessage.ReadAs<ShellyOnboardingRequestedMessage>(Encoding.UTF8.GetString(message.Data));

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.IsNotNull(request);
        Assert.AreEqual("192.168.1.10", request.IpAddress);
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