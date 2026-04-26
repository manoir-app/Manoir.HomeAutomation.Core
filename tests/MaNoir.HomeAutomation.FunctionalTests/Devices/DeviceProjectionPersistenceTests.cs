using Home.Common.Model;
using MaNoir.Core.Contracts.Models.Entities;
using MaNoir.Core.DataAccess;
using MaNoir.Core.Entities;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Devices;

[TestClass]
[DoNotParallelize]
public sealed class DeviceProjectionPersistenceTests
{
    [TestMethod]
    [TestCategory("Functional")]
    public async Task RegisterDevicesAsync_ShouldPersistDeviceInMongoOwnedCollection()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        DeviceLogic logic = new DeviceLogic();

        List<Device> insertedDevices = await logic.RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                DeviceInternalName = "Kitchen-Light",
                DeviceGivenName = "Kitchen light",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch],
                Datas =
                [
                    new DeviceData() { Name = "Switch", Value = "off", StandardDataType = DeviceData.DataTypeSwitch }
                ]
            }
        ]);

        MongoDbHelper mongo = new MongoDbHelper();
        Device storedDevice = await mongo.GetCollection<Device>().Find(device => device.Id == "kitchen-light").FirstOrDefaultAsync();

        Assert.HasCount(1, insertedDevices);
        Assert.IsNotNull(storedDevice);
        Assert.AreEqual("kitchen-light", insertedDevices[0].Id);
        Assert.AreEqual("kitchen-light", storedDevice.Id);
        Assert.AreEqual("local", storedDevice.MeshId);
        Assert.AreEqual(Device.DeviceKindHomeAutomation, storedDevice.DeviceKind);
        CollectionAssert.AreEqual(new[] { Device.HomeAutomationRoleSwitch }, storedDevice.DeviceRoles);
        Assert.AreEqual("Kitchen light", storedDevice.DeviceGivenName);
        Assert.AreEqual("off", storedDevice.Datas[0].Value);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task EntityProjection_ShouldExposeReadOnlyEntityWithoutNativeEntityPersistence()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "Kitchen-Light",
                DeviceInternalName = "kitchen-light",
                DeviceGivenName = "Kitchen light",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch, Device.HomeAutomationRoleColorBound],
                Datas =
                [
                    new DeviceData() { Name = "Switch", Value = "on", StandardDataType = DeviceData.DataTypeSwitch },
                    new DeviceData() { Name = "Brightness", Value = "42" }
                ]
            }
        ]);

        EntityProjectionRepositoryRegistry registry = HomeAutomationEntityProjectionRegistry.CreateDefault();
        EntityLogic entityLogic = new EntityLogic(registry);
        MongoDbHelper mongo = new MongoDbHelper();

        Entity projectedEntity = await entityLogic.GetByIdAsync(DeviceEntityConstants.Kinds.HomeAutomation, "KITCHEN-LIGHT");
        List<Entity> projectedEntities = await entityLogic.GetByKindsAsync([DeviceEntityConstants.Kinds.HomeAutomation]);
        BsonDocument nativeEntityDocument = await mongo.GetCollection("EntityDocuments")
            .Find(new BsonDocument("_id", string.Concat(DeviceEntityConstants.Kinds.HomeAutomation, "::kitchen-light")))
            .FirstOrDefaultAsync();

        Assert.IsNotNull(projectedEntity);
        Assert.IsTrue(projectedEntity.IsReadOnly);
        Assert.AreEqual("devices/catalog", projectedEntity.Source);
        Assert.AreEqual("kitchen-light", projectedEntity.Id);
        Assert.AreEqual(DeviceEntityConstants.Kinds.HomeAutomation, projectedEntity.EntityKind);
        Assert.AreEqual("Kitchen light", projectedEntity.Name);
        Assert.AreEqual("local", projectedEntity.MeshId);
        CollectionAssert.AreEquivalent(new[] { Device.DeviceKindHomeAutomation, "homeautomation:switch", "homeautomation:color-bound" }, projectedEntity.Roles);
        Assert.AreEqual("on", projectedEntity.Datas["Switch"].SimpleValue);
        Assert.AreEqual("42", projectedEntity.Datas["Brightness"].SimpleValue);
        Assert.HasCount(1, projectedEntities);
        Assert.IsNull(nativeEntityDocument);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task OnDeviceStateChangedAsync_ShouldPersistUpdatedStateAndRefreshProjectedEntity()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("sarah",
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

        bool changed = await deviceLogic.OnDeviceStateChangedAsync("shelly", "KITCHEN-LIGHT", Device.HomeAutomationRoleSwitch, "online",
            new Home.Common.Messages.DeviceStateChangedMessage.DeviceStateValue() { Name = "Switch", Value = "on" },
            new Home.Common.Messages.DeviceStateChangedMessage.DeviceStateValue() { Name = "Brightness", Value = "75" });

        Device storedDevice = await deviceLogic.GetByIdAsync("kitchen-light");
        EntityLogic entityLogic = new EntityLogic(HomeAutomationEntityProjectionRegistry.CreateDefault());
        Entity projectedEntity = await entityLogic.GetByIdAsync(DeviceEntityConstants.Kinds.HomeAutomation, "kitchen-light");

        Assert.IsTrue(changed);
        Assert.IsNotNull(storedDevice);
        Assert.AreEqual("online", storedDevice.MainStatusInfo);
        Assert.HasCount(2, storedDevice.Datas);
        Assert.AreEqual("on", storedDevice.Datas.Find(data => data.Name == "Switch")?.Value);
        Assert.AreEqual("75", storedDevice.Datas.Find(data => data.Name == "Brightness")?.Value);
        Assert.IsNotNull(projectedEntity);
        Assert.IsTrue(projectedEntity.IsReadOnly);
        Assert.AreEqual("on", projectedEntity.Datas["Switch"].SimpleValue);
        Assert.AreEqual("75", projectedEntity.Datas["Brightness"].SimpleValue);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ReplaceDataAndStatusAsync_ShouldReplacePersistedDatasAndProjectedEntitySnapshot()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("sarah",
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
                    new DeviceData() { Name = "Switch", Value = "off", StandardDataType = DeviceData.DataTypeSwitch },
                    new DeviceData() { Name = "Brightness", Value = "10" }
                ]
            }
        ]);

        bool replaced = await deviceLogic.ReplaceDataAndStatusAsync("KITCHEN-LIGHT", "standby",
        [
            new DeviceData() { Name = "Switch", Value = "off", StandardDataType = DeviceData.DataTypeSwitch },
            new DeviceData() { Name = "Temperature", Value = "21.5", ValueUnit = "C" }
        ]);

        Device storedDevice = await deviceLogic.GetByIdAsync("kitchen-light");
        EntityLogic entityLogic = new EntityLogic(HomeAutomationEntityProjectionRegistry.CreateDefault());
        Entity projectedEntity = await entityLogic.GetByIdAsync(DeviceEntityConstants.Kinds.HomeAutomation, "kitchen-light");

        Assert.IsTrue(replaced);
        Assert.IsNotNull(storedDevice);
        Assert.AreEqual("standby", storedDevice.MainStatusInfo);
        Assert.HasCount(2, storedDevice.Datas);
        Assert.IsNotNull(storedDevice.Datas.Find(data => data.Name == "Switch"));
        Assert.IsNotNull(storedDevice.Datas.Find(data => data.Name == "Temperature"));
        Assert.IsNull(storedDevice.Datas.Find(data => data.Name == "Brightness"));
        Assert.IsNotNull(projectedEntity);
        Assert.AreEqual("off", projectedEntity.Datas["Switch"].SimpleValue);
        Assert.AreEqual("21.5", projectedEntity.Datas["Temperature"].SimpleValue);
        Assert.IsFalse(projectedEntity.Datas.ContainsKey("Brightness"));
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task RegisterDevicesAsync_WhenDeviceAlreadyExistsByInternalName_ShouldUpdateInPlaceWithoutMongoDuplication()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        DeviceLogic deviceLogic = new DeviceLogic();

        List<Device> firstInsert = await deviceLogic.RegisterDevicesAsync("agent-a",
        [
            new Device()
            {
                DeviceInternalName = "Kitchen-Light",
                DeviceGivenName = "Kitchen ceiling",
                DevicePlatform = "shelly",
                DeviceKind = Device.DeviceKindHomeAutomation,
                MeshId = "mesh-a",
                DeviceRoles = [Device.HomeAutomationRoleSwitch],
                DeviceAddresses = ["192.168.1.10"],
                SupportPrivacyMode = true
            }
        ]);

        List<Device> secondInsert = await deviceLogic.RegisterDevicesAsync("agent-b",
        [
            new Device()
            {
                DeviceInternalName = "kitchen-light",
                DeviceGivenName = "Should not replace existing display name",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                MeshId = "mesh-b",
                DeviceRoles = ["Switch", "Color-Bound"],
                DeviceAddresses = ["zigbee://bridge/kitchen-light"],
                SupportPrivacyMode = false
            }
        ]);

        Device reloadedDevice = await deviceLogic.GetByIdAsync("kitchen-light");
        MongoDbHelper mongo = new MongoDbHelper();
        List<Device> allStoredDevices = await mongo.GetCollection<Device>().Find(Builders<Device>.Filter.Empty).ToListAsync();

        Assert.HasCount(1, firstInsert);
        Assert.HasCount(0, secondInsert);
        Assert.IsNotNull(reloadedDevice);
        Assert.AreEqual("kitchen-light", reloadedDevice.Id);
        Assert.AreEqual("mesh-a", reloadedDevice.MeshId);
        Assert.AreEqual("Kitchen ceiling", reloadedDevice.DeviceGivenName);
        Assert.AreEqual("zigbee2mqtt", reloadedDevice.DevicePlatform);
        Assert.IsFalse(reloadedDevice.SupportPrivacyMode);
        CollectionAssert.AreEqual(new[] { "switch", "color-bound" }, reloadedDevice.DeviceRoles);
        CollectionAssert.AreEqual(new[] { "zigbee://bridge/kitchen-light" }, reloadedDevice.DeviceAddresses);
        Assert.HasCount(1, allStoredDevices);
        Assert.AreEqual("kitchen-light", allStoredDevices.Single().Id);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task FindAsync_AndGetAllAsync_ShouldRespectMeshRoleKindAndIgnoredFilters()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("agent-a",
        [
            new Device()
            {
                Id = "Kitchen-Light",
                DeviceInternalName = "kitchen-light",
                DeviceKind = Device.DeviceKindHomeAutomation,
                MeshId = "local",
                DeviceRoles = [Device.HomeAutomationRoleSwitch]
            },
            new Device()
            {
                Id = "Kitchen-Sensor",
                DeviceInternalName = "kitchen-sensor",
                DeviceKind = Device.DeviceKindHomeAutomation,
                MeshId = "local",
                DeviceRoles = [Device.HomeAutomationMainRoleSensors]
            },
            new Device()
            {
                Id = "Hall-Display",
                DeviceInternalName = "hall-display",
                DeviceKind = Device.DeviceKindDisplay,
                MeshId = "secondary",
                DeviceRoles = [Device.DisplayRoleImageDisplay]
            }
        ]);

        DeviceMongoOperations mongoOperations = new DeviceMongoOperations();
        Device ignoredDevice = await mongoOperations.GetByIdAsync("kitchen-sensor");
        ignoredDevice.IsIgnored = true;
        await mongoOperations.SaveAsync(ignoredDevice);

        List<Device> defaultLocalDevices = await deviceLogic.GetAllAsync();
        List<Device> includingIgnoredLocalDevices = await deviceLogic.GetAllAsync(returnIgnored: true);
        List<Device> switchDevices = await deviceLogic.FindAsync(role: Device.HomeAutomationRoleSwitch, meshId: "local");
        List<Device> displayDevices = await deviceLogic.FindAsync(kind: Device.DeviceKindDisplay, meshId: "secondary");
        List<Device> exactIdMatch = await deviceLogic.FindAsync(id: "HALL-DISPLAY", returnIgnored: true, meshId: "secondary");

        Assert.HasCount(1, defaultLocalDevices);
        Assert.AreEqual("kitchen-light", defaultLocalDevices[0].Id);
        Assert.HasCount(2, includingIgnoredLocalDevices);
        CollectionAssert.AreEquivalent(new[] { "kitchen-light", "kitchen-sensor" }, includingIgnoredLocalDevices.Select(device => device.Id).ToArray());
        Assert.HasCount(1, switchDevices);
        Assert.AreEqual("kitchen-light", switchDevices[0].Id);
        Assert.HasCount(1, displayDevices);
        Assert.AreEqual("hall-display", displayDevices[0].Id);
        Assert.HasCount(1, exactIdMatch);
        Assert.AreEqual("hall-display", exactIdMatch[0].Id);
    }
}