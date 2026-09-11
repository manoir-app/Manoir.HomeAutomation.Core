using Home.Common;
using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.Agents.Sarah;
using MaNoir.HomeAutomation.Devices.Shelly;
using MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;
using MaNoir.Agents.Sarah.Shelly;
using MaNoir.Agents.Sarah.Zigbee2Mqtt;
using MaNoir.Core.Contracts.Models.Entities;
using MaNoir.Core.DataAccess;
using MaNoir.Core.Entities;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Protocols.Zigbee2Mqtt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MongoDB.Bson;
using MongoDB.Driver;
using MQTTnet;
using MQTTnet.Client;
using NATS.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
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
    public async Task Zigbee2MqttRuntimeService_WhenSwitchStateArrives_ShouldUpdateExistingDevice()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "kitchen-light-managed",
                DeviceInternalName = "kitchen-light",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch],
                ConfigurationData = """
                {
                    "definition":
                    {
                        "exposes":
                        [
                            { "property": "temperature", "unit": "F" },
                            { "property": "pressure", "unit": "kPa" },
                            { "property": "voc", "unit": "ppb" },
                            { "property": "pm25", "unit": "mg/m3" },
                            { "property": "formaldehyde", "unit": "mg/m3" },
                            { "property": "power", "unit": "kW" },
                            { "property": "energy", "unit": "Wh" }
                        ]
                    }
                }
                """,
                Datas = [new DeviceData() { Name = "Switch", Value = "off", StandardDataType = DeviceData.DataTypeSwitch }]
            }
        ]);

        Zigbee2MqttRuntimeService service = new Zigbee2MqttRuntimeService(NullLogger<Zigbee2MqttRuntimeService>.Instance);
        AddRuntimeDevice(service, "kitchen-light", """
        {
            "definition": { "exposes": [{ "property": "state" }, { "property": "brightness" }, { "property": "battery", "unit": "%" }, { "property": "linkquality" }, { "property": "temperature", "unit": "F" }, { "property": "humidity", "unit": "%" }, { "property": "pressure", "unit": "kPa" }, { "property": "occupancy" }, { "property": "contact" }, { "property": "water_leak" }, { "property": "smoke" }, { "property": "carbon_monoxide" }, { "property": "tamper" }, { "property": "vibration" }, { "property": "illuminance_lux", "unit": "lx" }, { "property": "co2", "unit": "ppm" }, { "property": "voc", "unit": "ppb" }, { "property": "pm25", "unit": "mg/m3" }, { "property": "pm10", "unit": "ug/m3" }, { "property": "soil_moisture", "unit": "%" }, { "property": "noise", "unit": "dB" }, { "property": "formaldehyde", "unit": "mg/m3" }, { "property": "power", "unit": "kW" }, { "property": "energy", "unit": "Wh" }] }
        }
        """);
                await service.HandleMessageAsync("zigbee2mqtt/kitchen-light", """
                {
                    "state": "ON",
                    "brightness": 127,
                    "battery": 86,
                    "linkquality": 102,
                    "temperature": 69.8,
                    "humidity": 47.8,
                    "pressure": 100.82,
                    "occupancy": true,
                    "contact": false,
                    "water_leak": false,
                    "smoke": false,
                    "carbon_monoxide": false,
                    "tamper": true,
                    "vibration": true,
                    "illuminance_lux": 80,
                    "co2": 630,
                    "voc": 11,
                    "pm25": 0.004,
                    "pm10": 7,
                    "soil_moisture": 43,
                    "noise": 36.5,
                    "formaldehyde": 0.02,
                    "power": 0.0126,
                    "energy": 4200
                }
                """);

        Device storedDevice = await deviceLogic.GetByIdAsync("kitchen-light-managed");

        Assert.IsNotNull(storedDevice);
        Assert.AreEqual("online", storedDevice.MainStatusInfo);
        Assert.AreEqual("on", storedDevice.Datas.Find(data => data.Name == "Switch")?.Value);
        Assert.AreEqual(DeviceDataCategory.DeviceState, storedDevice.Datas.Find(data => data.Name == "Switch")?.Category);
        Assert.AreEqual("50", storedDevice.Datas.Find(data => data.Name == "Brightness")?.Value);
        Assert.AreEqual(DeviceData.DataTypeGradient, storedDevice.Datas.Find(data => data.Name == "Brightness")?.StandardDataType);
        Assert.AreEqual("86", storedDevice.Datas.Find(data => data.Name == "Battery")?.Value);
        Assert.AreEqual(DeviceDataCategory.DeviceHealth, storedDevice.Datas.Find(data => data.Name == "Battery")?.Category);
        Assert.AreEqual(DeviceData.DataTypeSensorTemperature, storedDevice.Datas.Find(data => data.Name == "Temperature")?.StandardDataType);
        Assert.AreEqual(DeviceDataCategory.SensorReading, storedDevice.Datas.Find(data => data.Name == "Temperature")?.Category);
        Assert.AreEqual("21", storedDevice.Datas.Find(data => data.Name == "Temperature")?.Value);
        Assert.AreEqual("C", storedDevice.Datas.Find(data => data.Name == "Temperature")?.ValueUnit);
        Assert.AreEqual("100820", storedDevice.Datas.Find(data => data.Name == "Pressure")?.Value);
        Assert.AreEqual("Pa", storedDevice.Datas.Find(data => data.Name == "Pressure")?.ValueUnit);
        Assert.AreEqual("true", storedDevice.Datas.Find(data => data.Name == "Occupancy")?.Value);
        Assert.AreEqual(DeviceData.DataTypeOccupancy, storedDevice.Datas.Find(data => data.Name == "Occupancy")?.StandardDataType);
        Assert.AreEqual(DeviceDataCategory.DeviceState, storedDevice.Datas.Find(data => data.Name == "Occupancy")?.Category);
        Assert.AreEqual("false", storedDevice.Datas.Find(data => data.Name == "Contact")?.Value);
        Assert.AreEqual(DeviceData.DataTypeContact, storedDevice.Datas.Find(data => data.Name == "Contact")?.StandardDataType);
        Assert.AreEqual(DeviceDataCategory.DeviceState, storedDevice.Datas.Find(data => data.Name == "Contact")?.Category);
        Assert.AreEqual(DeviceData.DataTypeWaterLeak, storedDevice.Datas.Find(data => data.Name == "WaterLeak")?.StandardDataType);
        Assert.AreEqual(DeviceData.DataTypeSmoke, storedDevice.Datas.Find(data => data.Name == "Smoke")?.StandardDataType);
        Assert.AreEqual(DeviceData.DataTypeCarbonMonoxide, storedDevice.Datas.Find(data => data.Name == "CarbonMonoxide")?.StandardDataType);
        Assert.AreEqual(DeviceData.DataTypeTamper, storedDevice.Datas.Find(data => data.Name == "Tamper")?.StandardDataType);
        Assert.AreEqual(DeviceData.DataTypeVibration, storedDevice.Datas.Find(data => data.Name == "Vibration")?.StandardDataType);
        Assert.AreEqual(DeviceDataCategory.DeviceState, storedDevice.Datas.Find(data => data.Name == "WaterLeak")?.Category);
        Assert.AreEqual(DeviceData.DataTypeSensorCo2, storedDevice.Datas.Find(data => data.Name == "CO2")?.StandardDataType);
        Assert.AreEqual(DeviceData.DataTypeSensorVoc, storedDevice.Datas.Find(data => data.Name == "VOC")?.StandardDataType);
        Assert.AreEqual("ppb", storedDevice.Datas.Find(data => data.Name == "VOC")?.ValueUnit);
        Assert.AreEqual(DeviceData.DataTypeSensorPm25, storedDevice.Datas.Find(data => data.Name == "PM2.5")?.StandardDataType);
        Assert.AreEqual("4", storedDevice.Datas.Find(data => data.Name == "PM2.5")?.Value);
        Assert.AreEqual("ug/m3", storedDevice.Datas.Find(data => data.Name == "PM2.5")?.ValueUnit);
        Assert.AreEqual(DeviceData.DataTypeSensorPm10, storedDevice.Datas.Find(data => data.Name == "PM10")?.StandardDataType);
        Assert.AreEqual(DeviceDataCategory.SensorReading, storedDevice.Datas.Find(data => data.Name == "CO2")?.Category);
        Assert.AreEqual("%", storedDevice.Datas.Find(data => data.Name == "SoilMoisture")?.ValueUnit);
        Assert.AreEqual("dB", storedDevice.Datas.Find(data => data.Name == "Noise")?.ValueUnit);
        Assert.AreEqual(DeviceData.DataTypeSensorFormaldehyde, storedDevice.Datas.Find(data => data.Name == "Formaldehyde")?.StandardDataType);
        Assert.AreEqual("20", storedDevice.Datas.Find(data => data.Name == "Formaldehyde")?.Value);
        Assert.AreEqual("ug/m3", storedDevice.Datas.Find(data => data.Name == "Formaldehyde")?.ValueUnit);
        Assert.AreEqual("lx", storedDevice.Datas.Find(data => data.Name == "Illuminance")?.ValueUnit);
        Assert.AreEqual(DeviceData.DataTypePowerCurrentConsumption, storedDevice.Datas.Find(data => data.Name == "Power")?.StandardDataType);
        Assert.AreEqual("12.6", storedDevice.Datas.Find(data => data.Name == "Power")?.Value);
        Assert.AreEqual("W", storedDevice.Datas.Find(data => data.Name == "Power")?.ValueUnit);
        Assert.AreEqual(DeviceData.DataTypePowerTotal, storedDevice.Datas.Find(data => data.Name == "Energy")?.StandardDataType);
        Assert.AreEqual("4.2", storedDevice.Datas.Find(data => data.Name == "Energy")?.Value);
        Assert.AreEqual("kWh", storedDevice.Datas.Find(data => data.Name == "Energy")?.ValueUnit);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task Zigbee2MqttRuntimeService_WhenMqttStateArrives_ShouldUpdateExistingDevice()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using MosquittoFunctionalTestHost mqttHost = new MosquittoFunctionalTestHost();
        await mqttHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", mqttHost.Host);
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", mqttHost.Port.ToString());

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "kitchen-light-managed",
                DeviceInternalName = "kitchen-light",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch],
                Datas = [new DeviceData() { Name = "Switch", Value = "off", StandardDataType = DeviceData.DataTypeSwitch }]
            }
        ]);

        Zigbee2MqttRuntimeService service = new Zigbee2MqttRuntimeService(NullLogger<Zigbee2MqttRuntimeService>.Instance);
        AddRuntimeDevice(service, "kitchen-light", """
        {
            "definition": { "exposes": [{ "property": "state" }, { "property": "brightness" }] }
        }
        """);
        await service.StartAsync(CancellationToken.None);

        try
        {
            MqttFactory factory = new MqttFactory();
            using IMqttClient publisher = factory.CreateMqttClient();
            await publisher.ConnectAsync(new MqttClientOptionsBuilder().WithClientId("zigbee2mqtt-test-publisher").WithTcpServer(mqttHost.Host, mqttHost.Port).Build());
            await publisher.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("zigbee2mqtt/kitchen-light").WithPayload("{\"state\":\"ON\"}").WithRetainFlag().Build());

            Device storedDevice = await WaitForDeviceStateAsync(deviceLogic, "kitchen-light-managed", "on");

            Assert.IsNotNull(storedDevice);
            Assert.AreEqual("online", storedDevice.MainStatusInfo);
            await publisher.DisconnectAsync();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task Zigbee2MqttRuntimeService_WhenRotaryActionArrives_ShouldPublishTransientDeviceAction()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope natsHostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope natsPortScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope natsCompatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "living-room-dial-managed",
                DeviceInternalName = "living-room-dial",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleActionnable]
            }
        ]);

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync(DeviceActionTriggeredMessage.DeviceActionTriggered);
        connection.Flush();

        Zigbee2MqttRuntimeService service = new Zigbee2MqttRuntimeService(NullLogger<Zigbee2MqttRuntimeService>.Instance);
        AddRuntimeDevice(service, "living-room-dial", """
        {
            "definition": { "exposes": [{ "property": "action", "values": ["rotate_left"] }] }
        }
        """);
        await service.HandleMessageAsync("zigbee2mqtt/living-room-dial", "{\"action\":\"rotate_left\",\"action_angle\":15}");

        Msg published = subscription.NextMessage(5000);
        DeviceActionTriggeredMessage action = BaseMessage.ReadAs<DeviceActionTriggeredMessage>(Encoding.UTF8.GetString(published.Data));
        Device storedDevice = await deviceLogic.GetByIdAsync("living-room-dial-managed");

        Assert.IsNotNull(action);
        Assert.AreEqual(DeviceActionTriggeredMessage.DeviceActionTriggered, published.Subject);
        Assert.AreEqual("living-room-dial-managed", action.DeviceId);
        Assert.AreEqual("living-room-dial", action.DeviceInternalName);
        Assert.AreEqual("rotary", action.ActionKind);
        Assert.AreEqual("rotate", action.Action);
        Assert.AreEqual("rotate_left", action.RawAction);
        Assert.AreEqual("left", action.Attributes["direction"]);
        Assert.AreEqual("-1", action.Attributes["delta"]);
        Assert.AreEqual("15", action.Attributes["action_angle"]);
        Assert.IsNotNull(storedDevice);
        Assert.HasCount(0, storedDevice.Datas);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task Zigbee2MqttRuntimeService_WhenAvailabilityArrives_ShouldUpdateExistingDeviceStatus()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using MosquittoFunctionalTestHost mqttHost = new MosquittoFunctionalTestHost();
        await mqttHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", mqttHost.Host);
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", mqttHost.Port.ToString());

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "hall-sensor-managed",
                DeviceInternalName = "hall-sensor",
                DevicePlatform = "zigbee2mqtt",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationMainRoleSensors],
                MainStatusInfo = "online"
            }
        ]);

        Zigbee2MqttRuntimeService service = new Zigbee2MqttRuntimeService(NullLogger<Zigbee2MqttRuntimeService>.Instance);
        AddRuntimeDevice(service, "hall-sensor", """
        {
            "definition": { "exposes": [{ "property": "temperature" }] }
        }
        """);
        await service.StartAsync(CancellationToken.None);

        try
        {
            MqttFactory factory = new MqttFactory();
            using IMqttClient publisher = factory.CreateMqttClient();
            await publisher.ConnectAsync(new MqttClientOptionsBuilder().WithClientId("zigbee2mqtt-availability-publisher").WithTcpServer(mqttHost.Host, mqttHost.Port).Build());
            await publisher.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("zigbee2mqtt/hall-sensor/availability").WithPayload("offline").WithRetainFlag().Build());

            Device storedDevice = await WaitForDeviceStatusAsync(deviceLogic, "hall-sensor-managed", "offline");

            Assert.IsNotNull(storedDevice);
            Assert.AreEqual("offline", storedDevice.MainStatusInfo);
            Assert.HasCount(0, storedDevice.Datas);
            await publisher.DisconnectAsync();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1RuntimeService_WhenRelayStateArrives_ShouldDiscoverAndUpdateSwitch()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        DiscoveredDeviceLogic.EnableDiscoveryMode();

        ShellyGen1RuntimeService service = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance);
        await service.HandleMessageAsync("shellies/announce", "{\"id\":\"shelly1-a1b2c3\",\"model\":\"SHSW-1\",\"ip\":\"192.168.1.10\"}");

        DiscoveredDevice discovered = (await new DiscoveredDeviceLogic().GetForAgentAsync("sarah")).Single();
        Device managed = await new DiscoveredDeviceLogic().ValidateAppDeviceAsync(discovered.Id, "Kitchen relay");
        await service.HandleMessageAsync("shellies/shelly1-a1b2c3/relay/0", "on");
        Device stored = await new DeviceLogic().GetByIdAsync(managed.Id);

        Assert.AreEqual("shelly-gen1", discovered.DevicePlatform);
        CollectionAssert.Contains(discovered.DeviceRoles, Device.HomeAutomationRoleSwitch);
        Assert.IsNotNull(stored);
        Assert.AreEqual("on", stored.Datas.Single(data => data.StandardDataType == DeviceData.DataTypeSwitch).Value);

        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shelly1-a1b2c3");
        Assert.IsNotNull(runtimeDevice);
        IDeviceElement runtimeSwitch = runtimeDevice.Elements.Single(element => element.Name == "Switch");
        IToggleSwitchDevice switchCapability = runtimeSwitch.Capabilities.OfType<IToggleSwitchDevice>().Single();
        Assert.IsTrue(switchCapability.IsOn);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1RuntimeService_WhenInputEventArrives_ShouldPublishAndUpdateRuntimeAction()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope natsHostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope natsPortScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope natsCompatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);
        DiscoveredDeviceLogic.EnableDiscoveryMode();

        ShellyGen1RuntimeService service = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance);
        await service.HandleMessageAsync("shellies/announce", "{\"id\":\"shellybutton-a1b2c3\",\"model\":\"SHBTN-1\",\"ip\":\"192.168.1.16\"}");

        DiscoveredDevice discovered = (await new DiscoveredDeviceLogic().GetForAgentAsync("sarah")).Single();
        Device managed = await new DiscoveredDeviceLogic().ValidateAppDeviceAsync(discovered.Id, "Kitchen button");
        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync(DeviceActionTriggeredMessage.DeviceActionTriggered);
        connection.Flush();

        await service.HandleMessageAsync("shellies/shellybutton-a1b2c3/input_event/0", "{\"event\":\"S\",\"event_cnt\":7}");

        Msg published = subscription.NextMessage(5000);
        DeviceActionTriggeredMessage action = BaseMessage.ReadAs<DeviceActionTriggeredMessage>(Encoding.UTF8.GetString(published.Data));
        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shellybutton-a1b2c3");
        IRuntimeActionDevice input = runtimeDevice.Elements.Single(element => element.Name == "Input 0").Capabilities.OfType<IRuntimeActionDevice>().Single();

        CollectionAssert.Contains(discovered.DeviceRoles, Device.HomeAutomationRoleActionnable);
        Assert.HasCount(4, discovered.AvailableActions);
        Assert.AreEqual(managed.Id, action.DeviceId);
        Assert.AreEqual("single_push", action.Action);
        Assert.AreEqual("S", action.RawAction);
        Assert.AreEqual("7", action.Attributes["eventCount"]);
        Assert.AreEqual("single_push", input.LastAction.Action);
        Assert.AreEqual("S", input.LastAction.RawAction);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1RuntimeService_WhenCompositeRelayAndLightStatesArrive_ShouldPersistChannelsAndMeasurements()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "composite-shelly",
                DeviceInternalName = "shelly-composite",
                DevicePlatform = "shelly-gen1",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch, Device.HomeAutomationRoleDimmer]
            }
        ]);

        ShellyGen1RuntimeService service = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance);
    await service.HandleMessageAsync("shellies/announce", "{\"id\":\"shelly-composite\",\"model\":\"SHSW-2\",\"ip\":\"192.168.1.14\"}");
        await service.HandleMessageAsync("shellies/shelly-composite/relay/0", "on");
        await service.HandleMessageAsync("shellies/shelly-composite/relay/1", "off");
        await service.HandleMessageAsync("shellies/shelly-composite/relay/1/power", "12.4");
        await service.HandleMessageAsync("shellies/shelly-composite/relay/1/energy", "720");
        await service.HandleMessageAsync("shellies/shelly-composite/light/0/status", "{\"ison\":true,\"brightness\":42,\"red\":255,\"green\":0,\"blue\":16}");

        Device stored = await deviceLogic.GetByIdAsync("composite-shelly");

        Assert.AreEqual("on", stored.Datas.Single(data => data.Name == "Switch").Value);
        Assert.AreEqual("off", stored.Datas.Single(data => data.Name == "Relay 1").Value);
        Assert.AreEqual("12.4", stored.Datas.Single(data => data.Name == "Relay 1 Power").Value);
        Assert.AreEqual("0.012", stored.Datas.Single(data => data.Name == "Relay 1 Energy").Value);
        Assert.AreEqual("kWh", stored.Datas.Single(data => data.Name == "Relay 1 Energy").ValueUnit);
        Assert.AreEqual("42", stored.Datas.Single(data => data.Name == "Light 0 Brightness").Value);
        Assert.AreEqual("#FF0010", stored.Datas.Single(data => data.Name == "Light 0 Color").Value);

        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shelly-composite");
        IDeviceElement runtimeRelay = runtimeDevice.Elements.Single(element => element.Name == "Relay 1");
        ISensorDevice meter = runtimeRelay.Capabilities.OfType<ISensorDevice>().Single();
        Assert.AreEqual(12.4M, (decimal)meter.Readings["power"].Value);
        Assert.AreEqual("W", meter.Readings["power"].Definition.CanonicalUnit);
        Assert.AreEqual(720M, (decimal)meter.Readings["energy"].Value);
        Assert.AreEqual("Wh", meter.Readings["energy"].Definition.CanonicalUnit);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1RuntimeService_WhenLightStatusArrives_ShouldDiscoverAndPersistDimmer()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "dimmer-shelly",
                DeviceInternalName = "shelly-dimmer",
                DevicePlatform = "shelly-gen1",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleSwitch, Device.HomeAutomationRoleDimmer]
            }
        ]);

        ShellyApiHandler commandHandler = new ShellyApiHandler(_ => "{}");
        using HttpClient commandHttpClient = new HttpClient(commandHandler);
        ShellyGen1RuntimeService service = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance, commandHttpClient);
        await service.HandleMessageAsync("shellies/announce", "{\"id\":\"shelly-dimmer\",\"model\":\"SHDM-1\",\"ip\":\"192.168.1.15\"}");
        await service.HandleMessageAsync("shellies/shelly-dimmer/light/0/status", "{\"ison\":true,\"brightness\":42}");

        Device stored = await new DeviceLogic().GetByIdAsync("dimmer-shelly");
        Assert.AreEqual("on", stored.Datas.Single(data => data.Name == "Light 0").Value);
        Assert.AreEqual("42", stored.Datas.Single(data => data.Name == "Light 0 Brightness").Value);

        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shelly-dimmer");
        IDeviceElement runtimeLight = runtimeDevice.Elements.Single(element => element.Name == "Light 0");
        IToggleSwitchDevice switchCapability = runtimeLight.Capabilities.OfType<IToggleSwitchDevice>().Single();
        IIntensityGradientDevice intensityCapability = runtimeLight.Capabilities.OfType<IIntensityGradientDevice>().Single();
        Assert.IsTrue(switchCapability.IsOn);
        Assert.AreEqual(42M, intensityCapability.IntensityPercent);

        await intensityCapability.SetIntensityAsync(37.5M);
        Assert.AreEqual("http://192.168.1.15/light/0?turn=on&brightness=37.5", commandHandler.RequestUris.Last().AbsoluteUri);
        await switchCapability.SetSwitchStateAsync(false);
        Assert.AreEqual("http://192.168.1.15/light/0?turn=off", commandHandler.RequestUris.Last().AbsoluteUri);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1RuntimeService_WhenRgbStatusArrives_ShouldDiscoverAndPersistColor()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        await new DeviceLogic().RegisterDevicesAsync("sarah",
        [
            new Device()
            {
                Id = "rgb-shelly",
                DeviceInternalName = "shelly-rgb",
                DevicePlatform = "shelly-gen1",
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = [Device.HomeAutomationRoleDimmer, Device.HomeAutomationRoleColorBound]
            }
        ]);

        int rollerStatusRequestCount = 0;
        ShellyApiHandler commandHandler = new ShellyApiHandler(request =>
            request.RequestUri.PathAndQuery == "/roller/0"
                ? ++rollerStatusRequestCount >= 2
                    ? "{\"state\":\"stop\",\"current_pos\":0,\"calibrating\":false,\"positioning\":false}"
                    : "{\"state\":\"stop\",\"current_pos\":62,\"calibrating\":false,\"positioning\":true}"
                : "{}");
        using HttpClient commandHttpClient = new HttpClient(commandHandler);
        ShellyGen1RuntimeService service = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance, commandHttpClient);
        await service.HandleMessageAsync("shellies/announce", "{\"id\":\"shelly-rgb\",\"model\":\"SHRGBW2\",\"ip\":\"192.168.1.16\"}");
        await service.HandleMessageAsync("shellies/shelly-rgb/color/0/status", "{\"ison\":true,\"brightness\":42,\"red\":255,\"green\":0,\"blue\":16}");

        Device stored = await new DeviceLogic().GetByIdAsync("rgb-shelly");
        Assert.AreEqual("on", stored.Datas.Single(data => data.Name == "Color 0").Value);
        Assert.AreEqual("42", stored.Datas.Single(data => data.Name == "Color 0 Brightness").Value);
        Assert.AreEqual("#FF0010", stored.Datas.Single(data => data.Name == "Color 0 Color").Value);

        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shelly-rgb");
        IDeviceElement runtimeColor = runtimeDevice.Elements.Single(element => element.Name == "Color 0");
        IChromaticColorDevice colorCapability = runtimeColor.Capabilities.OfType<IChromaticColorDevice>().Single();
        IIntensityGradientDevice intensityCapability = runtimeColor.Capabilities.OfType<IIntensityGradientDevice>().Single();
        Assert.AreEqual(new DeviceColor.Rgb(255, 0, 16), colorCapability.CurrentColor);
        Assert.AreEqual(42M, intensityCapability.IntensityPercent);

        await colorCapability.SetColorAsync(new DeviceColor.Rgb(1, 2, 3));
        Assert.AreEqual("http://192.168.1.16/color/0?red=1&green=2&blue=3&turn=on", commandHandler.RequestUris.Last().AbsoluteUri);
        await intensityCapability.SetIntensityAsync(37.5M);
        Assert.AreEqual("http://192.168.1.16/color/0?brightness=37.5&turn=on", commandHandler.RequestUris.Last().AbsoluteUri);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1RuntimeService_WhenRollerTopicsArrive_ShouldPersistCoverStatePositionAndMeasurements()
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
                DeviceRoles = [Device.HomeAutomationMainRoleShutterSwitch]
            }
        ]);

        int rollerStatusRequestCount = 0;
        ShellyApiHandler commandHandler = new ShellyApiHandler(request =>
            request.RequestUri.PathAndQuery == "/roller/0"
                ? ++rollerStatusRequestCount == 2
                    ? "{\"state\":\"stop\",\"current_pos\":0,\"calibrating\":false,\"positioning\":false}"
                    : "{\"state\":\"stop\",\"current_pos\":62,\"calibrating\":false,\"positioning\":true}"
                : "{}");
        using HttpClient commandHttpClient = new HttpClient(commandHandler);
        ShellyGen1RuntimeService service = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance, commandHttpClient);
        await service.HandleMessageAsync("shellies/announce", "{\"id\":\"shellyswitch25-a1b2c3\",\"model\":\"SHSW-25\",\"mode\":\"roller\",\"ip\":\"192.168.1.10\"}");
        await service.HandleMessageAsync("shellies/shellyswitch25-a1b2c3/roller/0", "open");
        IDevice initialRuntimeDevice = service.RuntimeRegistry.GetById("shellyswitch25-a1b2c3");
        IShutterDevice initialRuntimeCover = initialRuntimeDevice.Elements.Single(element => element.Name == "Cover 0").Capabilities.OfType<IShutterDevice>().Single();
        Assert.IsTrue(initialRuntimeCover.SupportsPosition);
        Assert.AreEqual(62M, initialRuntimeCover.PositionPercent);
        await service.HandleMessageAsync("shellies/shellyswitch25-a1b2c3/roller/0/pos", "62");
        await service.HandleMessageAsync("shellies/shellyswitch25-a1b2c3/roller/0/power", "47.2");
        await service.HandleMessageAsync("shellies/shellyswitch25-a1b2c3/roller/0/energy", "960");

        Device stored = await new DeviceLogic().GetByIdAsync("living-room-cover");

        Assert.AreEqual("open", stored.Datas.Single(data => data.Name == "Cover 0").Value);
        Assert.AreEqual(DeviceData.DataTypeShutter, stored.Datas.Single(data => data.Name == "Cover 0").StandardDataType);
        Assert.AreEqual("62", stored.Datas.Single(data => data.Name == "Cover 0 Position").Value);
        Assert.AreEqual("47.2", stored.Datas.Single(data => data.Name == "Cover 0 Power").Value);
        Assert.AreEqual("0.016", stored.Datas.Single(data => data.Name == "Cover 0 Energy").Value);
        Assert.AreEqual("kWh", stored.Datas.Single(data => data.Name == "Cover 0 Energy").ValueUnit);
        CollectionAssert.Contains(stored.DeviceCapabilities, Device.CapabilityShutterPosition);

        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shellyswitch25-a1b2c3");
        IShutterDevice runtimeCover = runtimeDevice.Elements.Single(element => element.Name == "Cover 0").Capabilities.OfType<IShutterDevice>().Single();
        Assert.AreEqual("open", runtimeCover.State);
        Assert.IsTrue(runtimeCover.SupportsPosition);
        Assert.AreEqual(62M, runtimeCover.PositionPercent);
        await runtimeCover.CloseAsync();
        Assert.AreEqual("http://192.168.1.10/roller/0?go=close", commandHandler.RequestUris.Last().AbsoluteUri);
        await runtimeCover.SetPositionAsync(37.5M);
        Assert.AreEqual("http://192.168.1.10/roller/0?go=to_pos&roller_pos=37.5", commandHandler.RequestUris.Last().AbsoluteUri);

        await service.HandleMessageAsync("shellies/shellyswitch25-a1b2c3/roller/0/pos", "-1");
        stored = await new DeviceLogic().GetByIdAsync("living-room-cover");

        CollectionAssert.DoesNotContain(stored.DeviceCapabilities, Device.CapabilityShutterPosition);
        Assert.IsTrue(runtimeCover.SupportsPosition);
        Assert.IsNull(runtimeCover.PositionPercent);

        await service.HandleMessageAsync("shellies/announce", "{\"id\":\"shellyswitch25-uncalibrated\",\"model\":\"SHSW-25\",\"mode\":\"roller\",\"ip\":\"192.168.1.11\"}");
        IDevice uncalibratedRuntimeDevice = service.RuntimeRegistry.GetById("shellyswitch25-uncalibrated");
        IDeviceElement uncalibratedCoverElement = uncalibratedRuntimeDevice.Elements.Single(element => element.Name == "Cover 0");
        Assert.IsNotNull(uncalibratedCoverElement.Capabilities.OfType<ICoverDevice>().Single());
        Assert.IsFalse(uncalibratedCoverElement.Capabilities.OfType<IPositionableCoverDevice>().Any());
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1RuntimeService_WhenShelly25AnnouncesRollerMode_ShouldDiscoverShutterRole()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        DiscoveredDeviceLogic.EnableDiscoveryMode();

        ShellyGen1RuntimeService service = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance);
        await service.HandleMessageAsync("shellies/announce", "{\"id\":\"shellyswitch25-a1b2c3\",\"model\":\"SHSW-25\",\"mode\":\"roller\",\"ip\":\"192.168.1.10\"}");

        DiscoveredDevice discovered = (await new DiscoveredDeviceLogic().GetForAgentAsync("sarah")).Single();

        CollectionAssert.Contains(discovered.DeviceRoles, Device.HomeAutomationMainRoleShutterSwitch);
        CollectionAssert.DoesNotContain(discovered.DeviceRoles, Device.HomeAutomationRoleSwitch);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1RuntimeService_WhenOnboardedWithUnprotectedDevice_ShouldConfigureAndReboot()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", "mqtt.manoir.local");
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", "2883");
        using ProcessEnvironmentVariableScope shellyPasswordScope = new ProcessEnvironmentVariableScope("SHELLY_GEN1_PASSWORD", null);
        using ProcessEnvironmentVariableScope apiKeyScope = new ProcessEnvironmentVariableScope("HOMEAUTOMATION_APIKEY", "test-password");
        DiscoveredDeviceLogic.EnableDiscoveryMode();

        ShellyApiHandler handler = new ShellyApiHandler(request => request.RequestUri.PathAndQuery switch
        {
            "/shelly" => "{\"type\":\"SHSW-1\",\"auth\":false}",
            "/settings" => "{\"mqtt\":{\"enable\":false,\"server\":\"other-broker:1883\"}}",
            _ => "{}"
        });
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen1RuntimeService service = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance, httpClient);

        bool onboarded = await service.OnboardAsync("192.168.1.10");

        Assert.IsTrue(onboarded);
        CollectionAssert.AreEqual(
            new string[] { "/shelly", "/settings/login?enabled=true&username=sarah&password=test-password", "/settings", "/settings?mqtt_enable=true&mqtt_server=mqtt.manoir.local%3A2883", "/reboot" },
            handler.RequestUris.Select(uri => uri.PathAndQuery).ToArray());
        Assert.AreEqual("Basic c2FyYWg6dGVzdC1wYXNzd29yZA==", handler.AuthorizationHeaders[2]);
        Assert.AreEqual("Basic c2FyYWg6dGVzdC1wYXNzd29yZA==", handler.AuthorizationHeaders[3]);
        Assert.AreEqual("Basic c2FyYWg6dGVzdC1wYXNzd29yZA==", handler.AuthorizationHeaders[4]);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public void SarahMessageRouter_WhenShellyOnboardingRequested_ShouldUseMessageIpAddress()
    {
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", "mqtt.manoir.local");
        using ProcessEnvironmentVariableScope shellyPasswordScope = new ProcessEnvironmentVariableScope("SHELLY_GEN1_PASSWORD", "test-password");
        ShellyApiHandler handler = new ShellyApiHandler(request => request.RequestUri.PathAndQuery switch
        {
            "/shelly" => "{\"type\":\"SHSW-1\",\"auth\":false}",
            "/settings" => "{\"mqtt\":{\"enable\":false}}",
            _ => "{}"
        });
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen1RuntimeService shellyRuntime = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance, httpClient);
        SarahMessageRouter router = new SarahMessageRouter(
            new SarahRuntime(),
            new SceneExecutionService(NullLogger<SceneExecutionService>.Instance),
            shellyRuntime);

        MessageResponse response = router.HandleMessage(
            MessageOrigin.Local,
            ShellyOnboardingRequestedMessage.OnboardingRequested,
            JsonSerializer.Serialize(new ShellyOnboardingRequestedMessage() { IpAddress = "192.168.1.10" }));

        Assert.IsFalse(response.IsFail());
        Assert.AreEqual("192.168.1.10", handler.RequestUris[0].Host);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen1RuntimeService_WhenOnboardedWithNonGen1Device_ShouldNotConfigureIt()
    {
        ShellyApiHandler handler = new ShellyApiHandler(request => request.RequestUri.PathAndQuery switch
        {
            "/shelly" => "{}",
            "/rpc/Shelly.GetDeviceInfo" => "{\"gen\":2,\"app\":\"Plus1PM\"}",
            _ => "{}"
        });
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen1RuntimeService service = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance, httpClient);

        bool onboarded = await service.OnboardAsync("192.168.1.10");

        Assert.IsFalse(onboarded);
        CollectionAssert.AreEqual(new string[] { "/shelly", "/rpc/Shelly.GetDeviceInfo" }, handler.RequestUris.Select(uri => uri.PathAndQuery).ToArray());
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen2RuntimeService_WhenMqttIsNotConfigured_ShouldConfigureAndRebootThroughRpc()
    {
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", "mqtt.manoir.local");
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", "2883");
        using ProcessEnvironmentVariableScope topicScope = new ProcessEnvironmentVariableScope("SHELLY_GEN2_TOPIC", "manoir/shelly");
        ShellyApiHandler handler = new ShellyApiHandler(request => request.Content.ReadAsStringAsync().GetAwaiter().GetResult() switch
        {
            string content when content.Contains("Shelly.GetDeviceInfo") => "{\"id\":1,\"result\":{\"id\":\"shellyplus1pm-abc\",\"gen\":2,\"app\":\"Plus1PM\"}}",
            string content when content.Contains("Mqtt.GetConfig") => "{\"id\":1,\"result\":{\"enable\":false,\"server\":null}}",
            string content when content.Contains("Shelly.GetStatus") => "{\"id\":1,\"result\":{\"switch:0\":{\"output\":false}}}",
            _ => "{\"id\":1,\"result\":{}}"
        });
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen2RuntimeService service = new ShellyGen2RuntimeService(NullLogger<ShellyGen2RuntimeService>.Instance, httpClient);

        bool onboarded = await service.OnboardAsync("192.168.1.10");

        Assert.IsTrue(onboarded);
        CollectionAssert.AreEqual(new[] { "Shelly.GetDeviceInfo", "Mqtt.GetConfig", "Shelly.GetStatus", "Mqtt.SetConfig", "Shelly.Reboot" }, handler.RequestBodies.Select(body => JsonDocument.Parse(body).RootElement.GetProperty("method").GetString()).ToArray());
        using JsonDocument configuration = JsonDocument.Parse(handler.RequestBodies[3]);
        Assert.IsTrue(configuration.RootElement.GetProperty("params").GetProperty("config").GetProperty("enable").GetBoolean());
        Assert.AreEqual("mqtt.manoir.local:2883", configuration.RootElement.GetProperty("params").GetProperty("config").GetProperty("server").GetString());
        Assert.AreEqual("manoir/shelly/shellyplus1pm-abc", configuration.RootElement.GetProperty("params").GetProperty("config").GetProperty("topic_prefix").GetString());
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen2RuntimeService_WhenSwitchStatusArrives_ShouldDiscoverAndPersistSwitchMeasurements()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        DiscoveredDeviceLogic.EnableDiscoveryMode();

        ShellyApiHandler handler = new ShellyApiHandler(request => request.Content.ReadAsStringAsync().GetAwaiter().GetResult() switch
        {
            string content when content.Contains("Shelly.GetDeviceInfo") => "{\"id\":1,\"result\":{\"id\":\"shellyplus1pm-abc\",\"gen\":2,\"app\":\"Plus1PM\"}}",
            string content when content.Contains("Mqtt.GetConfig") => "{\"id\":1,\"result\":{\"enable\":true,\"server\":\"mqtt.manoir.local\",\"topic_prefix\":\"shelly/shellyplus1pm-abc\"}}",
            string content when content.Contains("Shelly.GetStatus") => "{\"id\":1,\"result\":{\"switch:0\":{\"output\":false}}}",
            _ => "{\"id\":1,\"result\":{}}"
        });
        using HttpClient httpClient = new HttpClient(handler);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", "mqtt.manoir.local");
        using ProcessEnvironmentVariableScope topicScope = new ProcessEnvironmentVariableScope("SHELLY_GEN2_TOPIC", "shelly");
        ShellyGen2RuntimeService service = new ShellyGen2RuntimeService(NullLogger<ShellyGen2RuntimeService>.Instance, httpClient);

        Assert.IsTrue(await service.OnboardAsync("192.168.1.11"));
        DiscoveredDevice discovered = (await new DiscoveredDeviceLogic().GetForAgentAsync("sarah")).Single();
        Device managed = await new DiscoveredDeviceLogic().ValidateAppDeviceAsync(discovered.Id, "Kitchen relay");
        await service.HandleMessageAsync("shelly/shellyplus1pm-abc/status/switch:0", "{\"id\":0,\"output\":true,\"apower\":12.4,\"aenergy\":{\"total\":720.5}}");

        Device stored = await new DeviceLogic().GetByIdAsync(managed.Id);

        Assert.AreEqual("shelly-gen2", discovered.DevicePlatform);
        CollectionAssert.Contains(discovered.DeviceRoles, Device.HomeAutomationRoleSwitch);
        Assert.AreEqual("on", stored.Datas.Single(data => data.Name == "Switch").Value);
        Assert.AreEqual("12.4", stored.Datas.Single(data => data.Name == "Switch Power").Value);
        Assert.AreEqual("0.7205", stored.Datas.Single(data => data.Name == "Switch Energy").Value);
        Assert.AreEqual("kWh", stored.Datas.Single(data => data.Name == "Switch Energy").ValueUnit);

        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shellyplus1pm-abc");
        Assert.IsNotNull(runtimeDevice);
        IToggleSwitchDevice runtimeSwitch = runtimeDevice.Elements
            .SelectMany(element => element.Capabilities)
            .OfType<IToggleSwitchDevice>()
            .Single();
        Assert.IsTrue(runtimeSwitch.IsOn);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen2RuntimeService_WhenCoverAndLightAreAnnounced_ShouldDiscoverAndPersistTheirStates()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", "mqtt.manoir.local");
        using ProcessEnvironmentVariableScope topicScope = new ProcessEnvironmentVariableScope("SHELLY_GEN2_TOPIC", "shelly");
        DiscoveredDeviceLogic.EnableDiscoveryMode();

        ShellyApiHandler handler = new ShellyApiHandler(request => request.Content.ReadAsStringAsync().GetAwaiter().GetResult() switch
        {
            string content when content.Contains("Shelly.GetDeviceInfo") => "{\"id\":1,\"result\":{\"id\":\"shellyplus2pm-a1b2c3\",\"gen\":2,\"app\":\"Plus2PM\"}}",
            string content when content.Contains("Mqtt.GetConfig") => "{\"id\":1,\"result\":{\"enable\":true,\"server\":\"mqtt.manoir.local\",\"topic_prefix\":\"shelly/shellyplus2pm-a1b2c3\"}}",
            string content when content.Contains("Shelly.GetStatus") => "{\"id\":1,\"result\":{\"cover:0\":{\"state\":\"stop\",\"positioning\":true},\"light:0\":{\"output\":false}}}",
            _ => "{\"id\":1,\"result\":{}}"
        });
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen2RuntimeService service = new ShellyGen2RuntimeService(NullLogger<ShellyGen2RuntimeService>.Instance, httpClient);

        Assert.IsTrue(await service.OnboardAsync("192.168.1.12"));
        DiscoveredDevice discovered = (await new DiscoveredDeviceLogic().GetForAgentAsync("sarah")).Single();
        Device managed = await new DiscoveredDeviceLogic().ValidateAppDeviceAsync(discovered.Id, "Living room cover and light");
        await service.HandleMessageAsync("shelly/shellyplus2pm-a1b2c3/status/cover:0", "{\"id\":0,\"state\":\"stop\",\"positioning\":true,\"current_pos\":62.5,\"apower\":1.2,\"aenergy\":{\"total\":20.4}}");
        await service.HandleMessageAsync("shelly/shellyplus2pm-a1b2c3/status/light:0", "{\"id\":0,\"output\":true,\"brightness\":37.5,\"apower\":8.1,\"aenergy\":{\"total\":11.2}}");

        Device stored = await new DeviceLogic().GetByIdAsync(managed.Id);

        CollectionAssert.Contains(discovered.DeviceRoles, Device.HomeAutomationMainRoleShutterSwitch);
        CollectionAssert.Contains(discovered.DeviceRoles, Device.HomeAutomationRoleDimmer);
        Assert.AreEqual("stop", stored.Datas.Single(data => data.Name == "Cover 0").Value);
        Assert.AreEqual("62.5", stored.Datas.Single(data => data.Name == "Cover 0 Position").Value);
        CollectionAssert.Contains(stored.DeviceCapabilities, Device.CapabilityShutterPosition);
        Assert.AreEqual("on", stored.Datas.Single(data => data.Name == "Light 0").Value);
        Assert.AreEqual("37.5", stored.Datas.Single(data => data.Name == "Light 0 Brightness").Value);
        Assert.AreEqual("8.1", stored.Datas.Single(data => data.Name == "Light 0 Power").Value);
        Assert.AreEqual("0.0112", stored.Datas.Single(data => data.Name == "Light 0 Energy").Value);

        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shellyplus2pm-a1b2c3");
        IDeviceElement runtimeLight = runtimeDevice.Elements.Single(element => element.Name == "Light 0");
        Assert.IsTrue(runtimeLight.Capabilities.OfType<IToggleSwitchDevice>().Single().IsOn);
        Assert.AreEqual(37.5M, runtimeLight.Capabilities.OfType<IIntensityGradientDevice>().Single().IntensityPercent);

        IDeviceElement runtimeCover = runtimeDevice.Elements.Single(element => element.Name == "Cover 0");
        IShutterDevice shutter = runtimeCover.Capabilities.OfType<IShutterDevice>().Single();
        Assert.AreEqual("stop", shutter.State);
        Assert.IsTrue(shutter.SupportsPosition);
        Assert.AreEqual(62.5M, shutter.PositionPercent);

        await shutter.SetPositionAsync(75M);
        string coverCommandBody = handler.RequestBodies.Last(body => JsonDocument.Parse(body).RootElement.GetProperty("method").GetString() == "Cover.GoToPosition");
        using JsonDocument coverCommand = JsonDocument.Parse(coverCommandBody);
        Assert.AreEqual(75M, coverCommand.RootElement.GetProperty("params").GetProperty("pos").GetDecimal());
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen2RuntimeService_WhenRgbStatusArrives_ShouldDiscoverAndPersistColor()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", "mqtt.manoir.local");
        using ProcessEnvironmentVariableScope topicScope = new ProcessEnvironmentVariableScope("SHELLY_GEN2_TOPIC", "shelly");
        DiscoveredDeviceLogic.EnableDiscoveryMode();

        ShellyApiHandler handler = new ShellyApiHandler(request => request.Content.ReadAsStringAsync().GetAwaiter().GetResult() switch
        {
            string content when content.Contains("Shelly.GetDeviceInfo") => "{\"id\":1,\"result\":{\"id\":\"shellyplusrgbwpm-a1b2c3\",\"gen\":2,\"app\":\"PlusRGBWPM\"}}",
            string content when content.Contains("Mqtt.GetConfig") => "{\"id\":1,\"result\":{\"enable\":true,\"server\":\"mqtt.manoir.local\",\"topic_prefix\":\"shelly/shellyplusrgbwpm-a1b2c3\"}}",
            string content when content.Contains("Shelly.GetStatus") => "{\"id\":1,\"result\":{\"rgb:0\":{\"output\":false}}}",
            _ => "{\"id\":1,\"result\":{}}"
        });
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen2RuntimeService service = new ShellyGen2RuntimeService(NullLogger<ShellyGen2RuntimeService>.Instance, httpClient);

        Assert.IsTrue(await service.OnboardAsync("192.168.1.13"));
        DiscoveredDevice discovered = (await new DiscoveredDeviceLogic().GetForAgentAsync("sarah")).Single();
        Device managed = await new DiscoveredDeviceLogic().ValidateAppDeviceAsync(discovered.Id, "Living room RGB");
        await service.HandleMessageAsync("shelly/shellyplusrgbwpm-a1b2c3/status/rgb:0", "{\"id\":0,\"output\":true,\"brightness\":42,\"rgb\":[255,0,16],\"apower\":9.1,\"aenergy\":{\"total\":10.5}}");

        Device stored = await new DeviceLogic().GetByIdAsync(managed.Id);

        CollectionAssert.Contains(discovered.DeviceRoles, Device.HomeAutomationRoleColorBound);
        Assert.AreEqual("on", stored.Datas.Single(data => data.Name == "RGB 0").Value);
        Assert.AreEqual("42", stored.Datas.Single(data => data.Name == "RGB 0 Brightness").Value);
        Assert.AreEqual("#FF0010", stored.Datas.Single(data => data.Name == "RGB 0 Color").Value);
        Assert.AreEqual("9.1", stored.Datas.Single(data => data.Name == "RGB 0 Power").Value);
        Assert.AreEqual("0.0105", stored.Datas.Single(data => data.Name == "RGB 0 Energy").Value);

        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shellyplusrgbwpm-a1b2c3");
        IDeviceElement runtimeRgb = runtimeDevice.Elements.Single(element => element.Name == "RGB 0");
        IChromaticColorDevice runtimeColor = runtimeRgb.Capabilities.OfType<IChromaticColorDevice>().Single();
        Assert.IsTrue(runtimeRgb.Capabilities.OfType<IToggleSwitchDevice>().Single().IsOn);
        Assert.AreEqual(42M, runtimeRgb.Capabilities.OfType<IIntensityGradientDevice>().Single().IntensityPercent);
        Assert.AreEqual(new DeviceColor.Rgb(255, 0, 16), runtimeColor.CurrentColor);

        await runtimeColor.SetColorAsync(new DeviceColor.Rgb(10, 20, 30));
        string colorCommandBody = handler.RequestBodies.Last(body => JsonDocument.Parse(body).RootElement.GetProperty("method").GetString() == "RGB.Set");
        using JsonDocument colorCommand = JsonDocument.Parse(colorCommandBody);
        Assert.AreEqual("RGB.Set", colorCommand.RootElement.GetProperty("method").GetString());
        CollectionAssert.AreEqual(new[] { 10, 20, 30 }, colorCommand.RootElement.GetProperty("params").GetProperty("rgb").EnumerateArray().Select(value => value.GetInt32()).ToArray());
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen2RuntimeService_WhenEnvironmentalSensorStatusesArrive_ShouldDiscoverAndPersistReadings()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", "mqtt.manoir.local");
        using ProcessEnvironmentVariableScope topicScope = new ProcessEnvironmentVariableScope("SHELLY_GEN2_TOPIC", "shelly");
        DiscoveredDeviceLogic.EnableDiscoveryMode();

        ShellyApiHandler handler = new ShellyApiHandler(request => request.Content.ReadAsStringAsync().GetAwaiter().GetResult() switch
        {
            string content when content.Contains("Shelly.GetDeviceInfo") => "{\"id\":1,\"result\":{\"id\":\"shellyplusht-a1b2c3\",\"gen\":2,\"app\":\"PlusHT\"}}",
            string content when content.Contains("Mqtt.GetConfig") => "{\"id\":1,\"result\":{\"enable\":true,\"server\":\"mqtt.manoir.local\",\"topic_prefix\":\"shelly/shellyplusht-a1b2c3\"}}",
            string content when content.Contains("Shelly.GetStatus") => "{\"id\":1,\"result\":{\"temperature:0\":{\"tC\":21.5},\"humidity:0\":{\"rh\":43.2},\"illuminance:0\":{\"lux\":350}}}",
            _ => "{\"id\":1,\"result\":{}}"
        });
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen2RuntimeService service = new ShellyGen2RuntimeService(NullLogger<ShellyGen2RuntimeService>.Instance, httpClient);

        Assert.IsTrue(await service.OnboardAsync("192.168.1.14"));
        DiscoveredDevice discovered = (await new DiscoveredDeviceLogic().GetForAgentAsync("sarah")).Single();
        Device managed = await new DiscoveredDeviceLogic().ValidateAppDeviceAsync(discovered.Id, "Living room climate");
        await service.HandleMessageAsync("shelly/shellyplusht-a1b2c3/status/temperature:0", "{\"id\":0,\"tC\":21.5}");
        await service.HandleMessageAsync("shelly/shellyplusht-a1b2c3/status/humidity:0", "{\"id\":0,\"rh\":43.2}");
        await service.HandleMessageAsync("shelly/shellyplusht-a1b2c3/status/illuminance:0", "{\"id\":0,\"lux\":350}");

        Device stored = await new DeviceLogic().GetByIdAsync(managed.Id);

        CollectionAssert.Contains(discovered.DeviceRoles, Device.HomeAutomationMainRoleSensors);
        Assert.AreEqual("21.5", stored.Datas.Single(data => data.Name == "Temperature").Value);
        Assert.AreEqual("C", stored.Datas.Single(data => data.Name == "Temperature").ValueUnit);
        Assert.AreEqual("43.2", stored.Datas.Single(data => data.Name == "Humidity").Value);
        Assert.AreEqual("%", stored.Datas.Single(data => data.Name == "Humidity").ValueUnit);
        Assert.AreEqual("350", stored.Datas.Single(data => data.Name == "Illuminance").Value);
        Assert.AreEqual("lx", stored.Datas.Single(data => data.Name == "Illuminance").ValueUnit);

        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shellyplusht-a1b2c3");
        ISensorDevice temperature = runtimeDevice.Elements
            .Single(element => element.Name == "Temperature")
            .Capabilities.OfType<ISensorDevice>().Single();
        ISensorDevice humidity = runtimeDevice.Elements
            .Single(element => element.Name == "Humidity")
            .Capabilities.OfType<ISensorDevice>().Single();
        ISensorDevice illuminance = runtimeDevice.Elements
            .Single(element => element.Name == "Illuminance")
            .Capabilities.OfType<ISensorDevice>().Single();
        Assert.AreEqual(21.5M, (decimal)temperature.Readings.Single().Value.Value);
        Assert.AreEqual(43.2M, (decimal)humidity.Readings.Single().Value.Value);
        Assert.AreEqual(350M, (decimal)illuminance.Readings.Single().Value.Value);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen2RuntimeService_WhenElectricalAndSafetyStatusesArrive_ShouldDiscoverAndPersistReadings()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", "mqtt.manoir.local");
        using ProcessEnvironmentVariableScope topicScope = new ProcessEnvironmentVariableScope("SHELLY_GEN2_TOPIC", "shelly");
        DiscoveredDeviceLogic.EnableDiscoveryMode();

        ShellyApiHandler handler = new ShellyApiHandler(request => request.Content.ReadAsStringAsync().GetAwaiter().GetResult() switch
        {
            string content when content.Contains("Shelly.GetDeviceInfo") => "{\"id\":1,\"result\":{\"id\":\"shellyproem-a1b2c3\",\"gen\":2,\"app\":\"ProEM\"}}",
            string content when content.Contains("Mqtt.GetConfig") => "{\"id\":1,\"result\":{\"enable\":true,\"server\":\"mqtt.manoir.local\",\"topic_prefix\":\"shelly/shellyproem-a1b2c3\"}}",
            string content when content.Contains("Shelly.GetStatus") => "{\"id\":1,\"result\":{\"em:0\":{},\"em1:1\":{},\"em1data:1\":{},\"battery:0\":{},\"flood:0\":{},\"smoke:0\":{},\"motion:0\":{},\"presence:0\":{}}}",
            _ => "{\"id\":1,\"result\":{}}"
        });
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen2RuntimeService service = new ShellyGen2RuntimeService(NullLogger<ShellyGen2RuntimeService>.Instance, httpClient);

        Assert.IsTrue(await service.OnboardAsync("192.168.1.16"));
        DiscoveredDevice discovered = (await new DiscoveredDeviceLogic().GetForAgentAsync("sarah")).Single();
        Device managed = await new DiscoveredDeviceLogic().ValidateAppDeviceAsync(discovered.Id, "Electrical cabinet");
        await service.HandleMessageAsync("shelly/shellyproem-a1b2c3/status/em:0", "{\"id\":0,\"total_act_power\":431.2,\"a_act_power\":100.1,\"b_act_power\":150.2,\"c_act_power\":180.9}");
        await service.HandleMessageAsync("shelly/shellyproem-a1b2c3/status/em1:1", "{\"id\":1,\"act_power\":87.4}");
        await service.HandleMessageAsync("shelly/shellyproem-a1b2c3/status/em1data:1", "{\"id\":1,\"total_act_energy\":1234.5}");
        await service.HandleMessageAsync("shelly/shellyproem-a1b2c3/status/battery:0", "{\"id\":0,\"percent\":74}");
        await service.HandleMessageAsync("shelly/shellyproem-a1b2c3/status/flood:0", "{\"id\":0,\"alarm\":true,\"mute\":false}");
        await service.HandleMessageAsync("shelly/shellyproem-a1b2c3/status/smoke:0", "{\"id\":0,\"alarm\":false,\"mute\":false}");
        await service.HandleMessageAsync("shelly/shellyproem-a1b2c3/status/motion:0", "{\"id\":0,\"motion\":true}");
        await service.HandleMessageAsync("shelly/shellyproem-a1b2c3/status/presence:0", "{\"id\":0,\"live_track\":{\"timer_started_at\":1756124368.33,\"timer_duration\":60,\"interval\":1}}");

        Device stored = await new DeviceLogic().GetByIdAsync(managed.Id);

        CollectionAssert.Contains(discovered.DeviceRoles, Device.HomeAutomationMainRoleSensors);
        Assert.AreEqual("431.2", stored.Datas.Single(data => data.Name == "EM 0 Power").Value);
        Assert.AreEqual("100.1", stored.Datas.Single(data => data.Name == "EM 0 Phase A Power").Value);
        Assert.AreEqual("87.4", stored.Datas.Single(data => data.Name == "EM 1 Power").Value);
        Assert.AreEqual("1.2345", stored.Datas.Single(data => data.Name == "EM 1 Energy").Value);
        Assert.AreEqual("kWh", stored.Datas.Single(data => data.Name == "EM 1 Energy").ValueUnit);
        Assert.AreEqual("74", stored.Datas.Single(data => data.Name == "Battery 0").Value);
        Assert.AreEqual(DeviceData.DataTypeBatteryPercentage, stored.Datas.Single(data => data.Name == "Battery 0").StandardDataType);
        Assert.AreEqual("true", stored.Datas.Single(data => data.Name == "Flood").Value);
        Assert.AreEqual(DeviceData.DataTypeWaterLeak, stored.Datas.Single(data => data.Name == "Flood").StandardDataType);
        Assert.AreEqual("false", stored.Datas.Single(data => data.Name == "Smoke").Value);
        Assert.AreEqual(DeviceData.DataTypeSmoke, stored.Datas.Single(data => data.Name == "Smoke").StandardDataType);
        Assert.AreEqual("true", stored.Datas.Single(data => data.Name == "Motion").Value);
        Assert.AreEqual(DeviceData.DataTypeOccupancy, stored.Datas.Single(data => data.Name == "Motion").StandardDataType);
        Assert.AreEqual("true", stored.Datas.Single(data => data.Name == "Presence").Value);
        Assert.AreEqual(DeviceData.DataTypeOccupancy, stored.Datas.Single(data => data.Name == "Presence").StandardDataType);

        IDevice runtimeDevice = service.RuntimeRegistry.GetById("shellyproem-a1b2c3");
        IDeviceElement runtimeEm = runtimeDevice.Elements.Single(element => element.Name == "EM 0");
        ISensorDevice emReadings = runtimeEm.Capabilities.OfType<ISensorDevice>().Single();
        Assert.AreEqual(431.2M, (decimal)emReadings.Readings["power"].Value);
        Assert.AreEqual(100.1M, (decimal)emReadings.Readings["phase_a_power"].Value);

        IDeviceElement runtimeEm1 = runtimeDevice.Elements.Single(element => element.Name == "EM 1");
        ISensorDevice em1Readings = runtimeEm1.Capabilities.OfType<ISensorDevice>().Single();
        Assert.AreEqual(87.4M, (decimal)em1Readings.Readings["power"].Value);
        Assert.AreEqual(1234.5M, (decimal)em1Readings.Readings["energy"].Value);

        AssertRuntimeReading(runtimeDevice, "Battery", "battery", 74M);
        AssertRuntimeReading(runtimeDevice, "Flood", "water_leak", true);
        AssertRuntimeReading(runtimeDevice, "Smoke", "smoke", false);
        AssertRuntimeReading(runtimeDevice, "Motion", "occupancy", true);
        AssertRuntimeReading(runtimeDevice, "Presence", "occupancy", true);
    }

    private static void AssertRuntimeReading(IDevice runtimeDevice, string elementName, string readingName, object expectedValue)
    {
        IDeviceElement element = runtimeDevice.Elements.Single(candidate => candidate.Name == elementName);
        ISensorDevice sensor = element.Capabilities.OfType<ISensorDevice>().Single();
        Assert.AreEqual(expectedValue, sensor.Readings[readingName].Value);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen2RuntimeService_WhenInputEventArrives_ShouldDiscoverAndPublishDeviceAction()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope natsHostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope natsPortScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope natsCompatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", "mqtt.manoir.local");
        using ProcessEnvironmentVariableScope topicScope = new ProcessEnvironmentVariableScope("SHELLY_GEN2_TOPIC", "shelly");
        DiscoveredDeviceLogic.EnableDiscoveryMode();

        ShellyApiHandler handler = new ShellyApiHandler(request => request.Content.ReadAsStringAsync().GetAwaiter().GetResult() switch
        {
            string content when content.Contains("Shelly.GetDeviceInfo") => "{\"id\":1,\"result\":{\"id\":\"shellyplus1-a1b2c3\",\"gen\":2,\"app\":\"Plus1\"}}",
            string content when content.Contains("Mqtt.GetConfig") => "{\"id\":1,\"result\":{\"enable\":true,\"server\":\"mqtt.manoir.local\",\"topic_prefix\":\"shelly/shellyplus1-a1b2c3\"}}",
            string content when content.Contains("Shelly.GetStatus") => "{\"id\":1,\"result\":{\"input:0\":{\"state\":false}}}",
            _ => "{\"id\":1,\"result\":{}}"
        });
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen2RuntimeService service = new ShellyGen2RuntimeService(NullLogger<ShellyGen2RuntimeService>.Instance, httpClient);

        Assert.IsTrue(await service.OnboardAsync("192.168.1.15"));
        DiscoveredDevice discovered = (await new DiscoveredDeviceLogic().GetForAgentAsync("sarah")).Single();
        Device managed = await new DiscoveredDeviceLogic().ValidateAppDeviceAsync(discovered.Id, "Kitchen input");
        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync(DeviceActionTriggeredMessage.DeviceActionTriggered);
        connection.Flush();

        await service.HandleMessageAsync("shelly/shellyplus1-a1b2c3/events/rpc", "{\"method\":\"NotifyEvent\",\"params\":{\"events\":[{\"component\":\"input:0\",\"id\":0,\"event\":\"double_push\",\"ts\":1710000000}]}}");
        await service.HandleMessageAsync("shelly/shellyplus1-a1b2c3/status/input:0", "{\"id\":0,\"state\":true}");

        Msg published = subscription.NextMessage(5000);
        DeviceActionTriggeredMessage action = BaseMessage.ReadAs<DeviceActionTriggeredMessage>(Encoding.UTF8.GetString(published.Data));
        Device stored = await new DeviceLogic().GetByIdAsync(managed.Id);

        CollectionAssert.Contains(discovered.DeviceRoles, Device.HomeAutomationRoleActionnable);
        Assert.HasCount(4, discovered.AvailableActions);
        Assert.AreEqual(managed.Id, action.DeviceId);
        Assert.AreEqual("button", action.ActionKind);
        Assert.AreEqual("double_push", action.Action);
        Assert.AreEqual("double_push", action.RawAction);
        Assert.AreEqual("0", action.Attributes["input"]);
        Assert.AreEqual("true", stored.Datas.Single(data => data.Name == "Input 0").Value);
        Assert.AreEqual(DeviceData.DataTypeContact, stored.Datas.Single(data => data.Name == "Input 0").StandardDataType);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ShellyGen2RuntimeService_WhenDeviceRequiresDigestAuthentication_ShouldRetryWithHomeAutomationApiKey()
    {
        using ProcessEnvironmentVariableScope mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", "mqtt.manoir.local");
        using ProcessEnvironmentVariableScope mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", null);
        using ProcessEnvironmentVariableScope shellyPasswordScope = new ProcessEnvironmentVariableScope("SHELLY_GEN2_PASSWORD", null);
        using ProcessEnvironmentVariableScope apiKeyScope = new ProcessEnvironmentVariableScope("HOMEAUTOMATION_APIKEY", "test-password");
        using ProcessEnvironmentVariableScope topicScope = new ProcessEnvironmentVariableScope("SHELLY_GEN2_TOPIC", "shelly");
        ShellyApiHandler handler = new ShellyApiHandler(request =>
        {
            using JsonDocument body = JsonDocument.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            string method = body.RootElement.GetProperty("method").GetString();
            if (method == "Shelly.GetDeviceInfo")
                return CreateShellyResponse("{\"id\":1,\"result\":{\"id\":\"shellyplus1pm-abc\",\"gen\":2,\"app\":\"Plus1PM\"}}");

            if (request.Headers.Authorization == null
                || !string.Equals(request.Headers.Authorization.Scheme, "Digest", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseMessage unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                unauthorized.Headers.Add("WWW-Authenticate", "Digest qop=\"auth\", realm=\"shellyplus1pm-abc\", nonce=\"nonce-123\", algorithm=SHA-256");
                return unauthorized;
            }

            Assert.IsTrue(request.Headers.Authorization.Parameter.Contains("username=\"admin\"", StringComparison.Ordinal));
            Assert.IsTrue(request.Headers.Authorization.Parameter.Contains("realm=\"shellyplus1pm-abc\"", StringComparison.Ordinal));
            Assert.IsTrue(request.Headers.Authorization.Parameter.Contains("uri=\"/rpc\"", StringComparison.Ordinal));
            Assert.IsTrue(request.Headers.Authorization.Parameter.Contains("response=\"", StringComparison.Ordinal));
            return method switch
            {
                "Mqtt.GetConfig" => CreateShellyResponse("{\"id\":1,\"result\":{\"enable\":true,\"server\":\"mqtt.manoir.local\",\"topic_prefix\":\"shelly/shellyplus1pm-abc\"}}"),
                "Shelly.GetStatus" => CreateShellyResponse("{\"id\":1,\"result\":{\"switch:0\":{\"output\":false}}}"),
                _ => CreateShellyResponse("{\"id\":1,\"result\":{}}")
            };
        });
        using HttpClient httpClient = new HttpClient(handler);
        ShellyGen2RuntimeService service = new ShellyGen2RuntimeService(NullLogger<ShellyGen2RuntimeService>.Instance, httpClient);

        bool onboarded = await service.OnboardAsync("192.168.1.10");

        Assert.IsTrue(onboarded);
        Assert.AreEqual(5, handler.RequestBodies.Count);
        using JsonDocument authenticatedRequest = JsonDocument.Parse(handler.RequestBodies[2]);
        Assert.AreEqual("Mqtt.GetConfig", authenticatedRequest.RootElement.GetProperty("method").GetString());
    }

    private static HttpResponseMessage CreateShellyResponse(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
    }

    private static void AddRuntimeDevice(Zigbee2MqttRuntimeService service, string deviceId, string discoveryJson)
    {
        using JsonDocument discovery = JsonDocument.Parse(discoveryJson);
        service.RuntimeRegistry.ApplySnapshot(
            "zigbee2mqtt",
            [ZigbeeDevice.Create(deviceId, discovery.RootElement, new Zigbee2MqttProtocol((_, _) => Task.CompletedTask))]);
    }

    private sealed class ShellyApiHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _responseBody;
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public ShellyApiHandler(Func<HttpRequestMessage, string> responseBody)
        {
            _responseBody = responseBody;
        }

        public ShellyApiHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public List<Uri> RequestUris { get; } = [];
        public List<string> AuthorizationHeaders { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri);
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            RequestBodies.Add(request.Content == null ? null : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            if (_response != null)
                return Task.FromResult(_response(request));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody(request))
            });
        }
    }

        [TestMethod]
        [TestCategory("Functional")]
        public async Task Zigbee2MqttRuntimeService_WhenBridgeDevicesArrive_ShouldPersistNativeDiscovery()
        {
                await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
                await mongoHost.StartAsync();
                using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
                DiscoveredDeviceLogic.EnableDiscoveryMode();

                Zigbee2MqttRuntimeService service = new Zigbee2MqttRuntimeService(NullLogger<Zigbee2MqttRuntimeService>.Instance);
                await service.HandleMessageAsync("zigbee2mqtt/bridge/devices", """
                [
                    { "friendly_name": "Coordinator", "type": "Coordinator" },
                    {
                        "friendly_name": "Kitchen light",
                        "type": "EndDevice",
                        "definition":
                        {
                            "exposes":
                            [
                                { "property": "state" },
                                { "property": "brightness" },
                                { "property": "action", "values": ["single", "double", "rotate_left", "rotate_right"] },
                                { "property": "color", "features": [{ "property": "x" }, { "property": "y" }] },
                                { "property": "color_temp" }
                            ]
                        }
                    }
                ]
                """);

                DiscoveredDeviceLogic discoveryLogic = new DiscoveredDeviceLogic();
                List<DiscoveredDevice> discoveredDevices = await discoveryLogic.GetForAgentAsync("sarah");
                DiscoveredDevice bridge = discoveredDevices.Single(device => device.DeviceInternalName == "zigbee2mqtt-bridge");
                DiscoveredDevice discoveredLight = discoveredDevices.Single(device => device.DeviceInternalName == "kitchen light");
                Device managedDevice = await discoveryLogic.ValidateAppDeviceAsync(discoveredLight.Id, "Kitchen light");

                Assert.HasCount(2, discoveredDevices);
                Assert.AreEqual("zigbee2mqtt", bridge.DevicePlatform);
                CollectionAssert.Contains(bridge.DeviceRoles, Device.HomeAutomationMainRoleBridge);
                Assert.AreEqual("kitchen light", discoveredLight.DeviceInternalName);
                Assert.AreEqual("zigbee2mqtt", discoveredLight.DevicePlatform);
                CollectionAssert.AreEquivalent(new[] { Device.HomeAutomationRoleSwitch, Device.HomeAutomationRoleDimmer, Device.HomeAutomationRoleActionnable, Device.HomeAutomationRoleColorBound }, discoveredLight.DeviceRoles);
                CollectionAssert.AreEquivalent(new[] { Device.CapabilityColorXy, Device.CapabilityColorTemperature }, discoveredLight.DeviceCapabilities);
                Assert.HasCount(4, discoveredLight.AvailableActions);
                Assert.AreEqual("rotate", discoveredLight.AvailableActions.Single(action => action.RawAction == "rotate_left").Action);
                Assert.AreEqual("left", discoveredLight.AvailableActions.Single(action => action.RawAction == "rotate_left").Attributes["direction"]);
                Assert.IsNotNull(managedDevice);
                CollectionAssert.AreEquivalent(new[] { Device.CapabilityColorXy, Device.CapabilityColorTemperature }, managedDevice.DeviceCapabilities);
                Assert.HasCount(4, managedDevice.AvailableActions);
                Assert.AreEqual("right", managedDevice.AvailableActions.Single(action => action.RawAction == "rotate_right").Attributes["direction"]);
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

    [TestMethod]
    [TestCategory("Functional")]
    public async Task DeviceDomains_ShouldBeNormalizedSearchableAndProjectedSeparatelyFromRoles()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        DeviceLogic deviceLogic = new DeviceLogic();
        await deviceLogic.RegisterDevicesAsync("agent-a",
        [
            new Device()
            {
                Id = "Entry-Tablet",
                DeviceInternalName = "entry-tablet",
                DeviceKind = Device.DeviceKindDisplay,
                DeviceDomains = ["Informative", "Home-Automation"],
                DeviceRoles = ["notification", "dashboard"]
            },
            new Device()
            {
                Id = "Alice-Phone",
                DeviceInternalName = "alice-phone",
                DeviceKind = Device.DeviceKindMobileDevice,
                DeviceDomains = ["Personal", "Informative"],
                DeviceRoles = ["notification", "presence-provider"]
            }
        ]);

        List<Device> informativeDevices = await deviceLogic.FindAsync(domain: Device.DeviceDomainInformative);
        List<Device> personalDevices = await deviceLogic.FindAsync(domain: "PERSONAL");
        Entity projectedTablet = await new EntityLogic(HomeAutomationEntityProjectionRegistry.CreateDefault())
            .GetByIdAsync(DeviceEntityConstants.Kinds.Display, "entry-tablet");

        CollectionAssert.AreEquivalent(new[] { "alice-phone", "entry-tablet" }, informativeDevices.Select(device => device.Id).ToArray());
        Assert.HasCount(1, personalDevices);
        Assert.AreEqual("alice-phone", personalDevices[0].Id);
        Assert.IsNotNull(projectedTablet);
        CollectionAssert.Contains(projectedTablet.Roles, "domain:informative");
        CollectionAssert.Contains(projectedTablet.Roles, "domain:home-automation");
        CollectionAssert.Contains(projectedTablet.Roles, "display:notification");
        CollectionAssert.DoesNotContain(projectedTablet.Roles, "display:informative");
    }

    private static async Task<Device> WaitForDeviceStateAsync(DeviceLogic deviceLogic, string deviceId, string expectedSwitchState)
    {
        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            Device storedDevice = await deviceLogic.GetByIdAsync(deviceId);
            if (string.Equals(storedDevice?.Datas.Find(data => data.Name == "Switch")?.Value, expectedSwitchState, StringComparison.Ordinal))
            {
                return storedDevice;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }

        Assert.Fail("Le runtime Zigbee2MQTT n'a pas projete l'etat MQTT du device dans le delai imparti.");
        return null;
    }

    private static async Task<Device> WaitForDeviceStatusAsync(DeviceLogic deviceLogic, string deviceId, string expectedStatus)
    {
        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            Device storedDevice = await deviceLogic.GetByIdAsync(deviceId);
            if (string.Equals(storedDevice?.MainStatusInfo, expectedStatus, StringComparison.Ordinal))
                return storedDevice;

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }

        Assert.Fail("Le runtime Zigbee2MQTT n'a pas projete la disponibilite MQTT du device dans le delai imparti.");
        return null;
    }
}