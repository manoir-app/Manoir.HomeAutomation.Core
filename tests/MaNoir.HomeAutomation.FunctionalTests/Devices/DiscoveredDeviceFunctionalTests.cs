using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NATS.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Devices;

[TestClass]
[DoNotParallelize]
public sealed class DiscoveredDeviceFunctionalTests
{
    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertAsync_ShouldPersistDiscoveredDevice_AndPublishLegacyDiscoveryMessages()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        DiscoveredDeviceLogic.EnableDiscoveryMode();
        DiscoveredDeviceLogic logic = new DiscoveredDeviceLogic();

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription generalSubscription = connection.SubscribeSync("mobiledevice.discovery.webapp");
        using ISyncSubscription roleSubscription = connection.SubscribeSync("mobiledevice.discovery.webapp.switch");
        connection.Flush();

        bool changed = await logic.UpsertAsync(new DiscoveredDevice()
        {
            DeviceInternalName = " Mobile-App ",
            DeviceAgentId = " Sarah ",
            DevicePlatform = "WebApp",
            DeviceKind = Device.DeviceKindMobileDevice,
            DeviceRoles = new List<string>() { "Switch" },
            DefaultConfigurationData = "{\"mode\":\"new\"}"
        });

        List<DiscoveredDevice> storedDevices = await logic.GetAllAsync(kind: Device.DeviceKindMobileDevice, agentId: "sarah");
        Msg generalMessage = generalSubscription.NextMessage(5000);
        Msg roleMessage = roleSubscription.NextMessage(5000);
        DeviceDiscoveredMessage generalPayload = BaseMessage.ReadAs<DeviceDiscoveredMessage>(Encoding.UTF8.GetString(generalMessage.Data));
        DeviceDiscoveredMessage rolePayload = BaseMessage.ReadAs<DeviceDiscoveredMessage>(Encoding.UTF8.GetString(roleMessage.Data));

        Assert.IsTrue(changed);
        Assert.HasCount(1, storedDevices);
        Assert.AreEqual("local", storedDevices[0].MeshId);
        Assert.AreEqual("mobile-app", storedDevices[0].DeviceInternalName);
        Assert.AreEqual("sarah", storedDevices[0].DeviceAgentId);
        Assert.AreEqual("webapp", storedDevices[0].DevicePlatform);
        Assert.AreEqual(Device.DeviceKindMobileDevice, storedDevices[0].DeviceKind);
        CollectionAssert.AreEqual(new[] { "switch" }, storedDevices[0].DeviceRoles);
        Assert.IsNotNull(generalPayload);
        Assert.IsNotNull(rolePayload);
        Assert.AreEqual("mobiledevice.discovery.webapp", generalPayload.Topic);
        Assert.AreEqual("mobiledevice.discovery.webapp.switch", rolePayload.Topic);
        Assert.AreEqual(storedDevices[0].Id, generalPayload.Device.Id);
        Assert.AreEqual(storedDevices[0].Id, rolePayload.Device.Id);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ValidateAppDeviceAsync_ShouldCreateManagedDeviceFromDiscovery()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);

        DiscoveredDeviceLogic.EnableDiscoveryMode();
        DiscoveredDeviceLogic discoveryLogic = new DiscoveredDeviceLogic();

        DiscoveredDevice created = await discoveryLogic.CreateForAppDeviceAsync();
        Device device = await discoveryLogic.ValidateAppDeviceAsync(created.Id, "John phone");
        bool isAssociated = await discoveryLogic.CheckIfAssociatedAsync(created.Id);
        Device reloaded = await new DeviceLogic().GetByIdAsync(created.Id);

        Assert.IsNotNull(created);
        Assert.AreEqual("local", created.MeshId);
        Assert.AreEqual("webapp", created.DevicePlatform);
        Assert.AreEqual(Device.DeviceKindMobileDevice, created.DeviceKind);
        Assert.AreEqual(6, created.DeviceCode.Length);
        Assert.IsNotNull(device);
        Assert.AreEqual(created.Id, device.Id);
        Assert.AreEqual("John phone", device.DeviceGivenName);
        Assert.IsTrue(isAssociated);
        Assert.IsNotNull(reloaded);
        Assert.AreEqual(created.Id, reloaded.Id);
    }
}