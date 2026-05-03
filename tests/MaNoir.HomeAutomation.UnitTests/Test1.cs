using Home.Common.Model;
using MaNoir.Core.Contracts.Models.Entities;
using MaNoir.Core.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class DeviceLogicTests
{
    [TestMethod]
    public void ChangeData_ShouldUpdateExistingEntryAndMainStatus()
    {
        Device device = new Device()
        {
            MainStatusInfo = "offline",
            Datas = new List<DeviceData>()
            {
                new DeviceData()
                {
                    Name = "Switch",
                    Value = "off",
                    ValueUnit = "binary",
                    StandardDataType = DeviceData.DataTypeSwitch,
                    IsMainData = false,
                    LastUpdated = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero)
                }
            }
        };

        bool changed = DeviceLogic.ChangeData(device, new DeviceData()
        {
            Name = "Switch",
            Value = "on",
            ValueUnit = "state",
            StandardDataType = DeviceData.DataTypeSwitch,
            IsMainData = true
        }, "online");

        Assert.IsTrue(changed);
        Assert.AreEqual("online", device.MainStatusInfo);
        Assert.HasCount(1, device.Datas);
        Assert.AreEqual("on", device.Datas[0].Value);
        Assert.AreEqual("state", device.Datas[0].ValueUnit);
        Assert.AreEqual(DeviceData.DataTypeSwitch, device.Datas[0].StandardDataType);
        Assert.IsTrue(device.Datas[0].IsMainData);
        Assert.IsTrue(device.Datas[0].LastUpdated > new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero));
    }

    [TestMethod]
    public void ChangeData_WhenEntryIsMissing_ShouldAppendAndKeepPreviousMainStatus()
    {
        Device device = new Device()
        {
            MainStatusInfo = "steady",
            Datas = new List<DeviceData>()
            {
                new DeviceData()
                {
                    Name = "Existing",
                    Value = "1"
                }
            }
        };

        bool changed = DeviceLogic.ChangeData(device, new DeviceData()
        {
            Name = "Humidity",
            Value = "45",
            StandardDataType = DeviceData.DataTypeSensorHumidity,
            ValueUnit = "%"
        }, "should-be-ignored");

        Assert.IsTrue(changed);
        Assert.AreEqual("steady", device.MainStatusInfo);
        Assert.HasCount(2, device.Datas);
        Assert.AreEqual("Humidity", device.Datas[1].Name);
        Assert.AreEqual("45", device.Datas[1].Value);
        Assert.AreEqual("%", device.Datas[1].ValueUnit);
        Assert.IsTrue(device.Datas[0].LastUpdated <= DateTimeOffset.Now);
        Assert.IsTrue(device.Datas[1].LastUpdated <= DateTimeOffset.Now);
    }

    [TestMethod]
    public void PrepareDeviceForRegistration_ShouldDefaultMeshAndLowercaseRoles()
    {
        Device device = new Device()
        {
            MeshId = null,
            DeviceRoles = new List<string>() { "Switch", "Color-Bound" }
        };

        Device prepared = DeviceLogic.PrepareForRegistration(device);

        Assert.AreSame(device, prepared);
        Assert.AreEqual("local", device.MeshId);
        CollectionAssert.AreEqual(new[] { "switch", "color-bound" }, device.DeviceRoles);
        Assert.IsNotNull(device.DeviceAddresses);
        Assert.IsNotNull(device.Datas);
        Assert.IsNotNull(device.SecondaryDatas);
        Assert.IsNotNull(device.Images);
    }

    [TestMethod]
    public void CreateProjectedEntity_ShouldKeepLegacyDeviceMapping()
    {
        Device device = new Device()
        {
            Id = "DEVICE/ALPHA",
            MeshId = "LOCAL",
            DeviceKind = Device.DeviceKindHomeAutomation,
            DeviceInternalName = "device/alpha",
            DeviceGivenName = "Kitchen",
            DeviceRoles = new List<string>() { "switch", "color-bound" },
            Datas = new List<DeviceData>()
            {
                new DeviceData() { Name = "Switch", Value = "on" },
                new DeviceData() { Name = "Brightness", Value = "42" }
            }
        };

        Entity entity = DeviceProjectedEntityRepository.CreateProjectedEntity(device);

        Assert.IsNotNull(entity);
        Assert.AreEqual("device/alpha", entity.Id);
        Assert.AreEqual(DeviceEntityConstants.Kinds.HomeAutomation, entity.EntityKind);
        Assert.AreEqual("Kitchen", entity.Name);
        Assert.AreEqual("local", entity.MeshId);
        CollectionAssert.AreEquivalent(new[] { Device.DeviceKindHomeAutomation, "homeautomation:switch", "homeautomation:color-bound" }, entity.Roles);
        Assert.AreEqual("on", entity.Datas["Switch"].SimpleValue);
        Assert.AreEqual("42", entity.Datas["Brightness"].SimpleValue);
        Assert.AreEqual(string.Empty, entity.Datas["Switch"].Category);
    }

    [TestMethod]
    public void CreateDefault_ShouldRegisterDeviceProjectedRepository()
    {
        EntityProjectionRepositoryRegistry registry = HomeAutomationEntityProjectionRegistry.CreateDefault();

        List<IProjectedEntityRepository> repositories = registry.GetRepositoriesForKinds([DeviceEntityConstants.Kinds.HomeAutomation]);

        Assert.HasCount(1, repositories);
        Assert.IsInstanceOfType<DeviceProjectedEntityRepository>(repositories[0]);
    }

    [TestMethod]
    public void ApplyRegistrationUpdate_ShouldKeepExistingMeshAndDisplayNameWhenExistingDisplayNameIsAlreadyMeaningful()
    {
        Device existing = new Device()
        {
            MeshId = "mesh-a",
            DeviceGivenName = "Kitchen ceiling",
            DevicePlatform = "shelly",
            DeviceRoles = new List<string>() { "switch" },
            DeviceAddresses = new List<string>() { "192.168.1.10" },
            SupportPrivacyMode = true
        };

        Device incoming = new Device()
        {
            MeshId = "mesh-b",
            DeviceInternalName = "kitchen-light",
            DeviceGivenName = "Different incoming name",
            DevicePlatform = "zigbee2mqtt",
            DeviceRoles = new List<string>() { "switch", "color-bound" },
            DeviceAddresses = new List<string>() { "zigbee://bridge/kitchen-light" },
            SupportPrivacyMode = false
        };

        DeviceLogic.ApplyRegistrationUpdate(existing, incoming);

        Assert.AreEqual("mesh-a", existing.MeshId);
        Assert.AreEqual("Kitchen ceiling", existing.DeviceGivenName);
        Assert.AreEqual("zigbee2mqtt", existing.DevicePlatform);
        Assert.IsFalse(existing.SupportPrivacyMode);
        CollectionAssert.AreEqual(new[] { "switch", "color-bound" }, existing.DeviceRoles);
        CollectionAssert.AreEqual(new[] { "zigbee://bridge/kitchen-light" }, existing.DeviceAddresses);
    }

    [TestMethod]
    public void ApplyRegistrationUpdate_ShouldRefreshDisplayNameWhenIncomingGivenNameMatchesInternalNameConvention()
    {
        Device existing = new Device()
        {
            MeshId = null,
            DeviceGivenName = "Old label"
        };

        Device incoming = new Device()
        {
            MeshId = "local",
            DeviceInternalName = "kitchen-light",
            DeviceGivenName = "kitchen-light"
        };

        DeviceLogic.ApplyRegistrationUpdate(existing, incoming);

        Assert.AreEqual("local", existing.MeshId);
        Assert.AreEqual("kitchen-light", existing.DeviceGivenName);
    }

    [TestMethod]
    public void PrepareForDiscovery_ShouldNormalizeDiscoveryShape()
    {
        DiscoveredDevice device = new DiscoveredDevice()
        {
            Id = " APP-1 ",
            MeshId = null,
            DeviceInternalName = " Mobile-App ",
            DeviceAgentId = " SARAH ",
            DevicePlatform = " WebApp ",
            DeviceKind = " MobileDevice ",
            DeviceRoles = new List<string>() { " Switch ", "switch", " Actionnable " }
        };

        DiscoveredDevice prepared = DiscoveredDeviceLogic.PrepareForDiscovery(device);

        Assert.AreSame(device, prepared);
        Assert.AreEqual("app-1", prepared.Id);
        Assert.AreEqual("local", prepared.MeshId);
        Assert.AreEqual("mobile-app", prepared.DeviceInternalName);
        Assert.AreEqual("sarah", prepared.DeviceAgentId);
        Assert.AreEqual("webapp", prepared.DevicePlatform);
        Assert.AreEqual("mobiledevice", prepared.DeviceKind);
        CollectionAssert.AreEqual(new[] { "switch", "actionnable" }, prepared.DeviceRoles);
    }

    [TestMethod]
    public void CreateManagedDeviceFromDiscovery_ShouldKeepLegacyMapping()
    {
        DiscoveredDevice discoveredDevice = new DiscoveredDevice()
        {
            Id = "mobile-01",
            MeshId = "local",
            DeviceInternalName = "mobile-01",
            DevicePlatform = "webapp",
            DeviceKind = Device.DeviceKindMobileDevice,
            DeviceRoles = new List<string>() { "switch" },
            DefaultConfigurationData = "{\"mode\":\"paired\"}"
        };

        Device device = DiscoveredDeviceLogic.CreateManagedDeviceFromDiscovery(discoveredDevice, "Phone");

        Assert.IsNotNull(device);
        Assert.AreEqual("mobile-01", device.Id);
        Assert.AreEqual("local", device.MeshId);
        Assert.AreEqual("mobile-01", device.DeviceInternalName);
        Assert.AreEqual("Phone", device.DeviceGivenName);
        Assert.AreEqual("webapp", device.DevicePlatform);
        Assert.AreEqual(Device.DeviceKindMobileDevice, device.DeviceKind);
        Assert.AreEqual("{\"mode\":\"paired\"}", device.ConfigurationData);
        CollectionAssert.AreEqual(new[] { "switch" }, device.DeviceRoles);
    }
}
