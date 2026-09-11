using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class RuntimeDeviceModelTests
{
    [TestMethod]
    public void RuntimeDevice_ShouldKeepCapabilitiesOnTheirElements()
    {
        IDeviceCapability switchCapability = new TestCapability();
        IDeviceCapability powerCapability = new TestCapability();
        RuntimeDevice device = new(
            "shelly-2pm",
            [
                new DeviceElement("Switch 0", [switchCapability, powerCapability]),
                new DeviceElement("Switch 1", [switchCapability])
            ]);

        Assert.AreEqual(2, device.Elements.Count);
        Assert.AreEqual(2, device.Elements[0].Capabilities.Count);
        Assert.AreEqual(1, device.Elements[1].Capabilities.Count);
        Assert.AreEqual(0, device.Capabilities.Count);
    }

    [TestMethod]
    public void RuntimeDevice_ShouldExposeHubAsDeviceCapability()
    {
        IHubDevice hub = new TestHub([new DeviceReference("hue-light-1"), new DeviceReference("hue-light-2")]);
        RuntimeDevice device = new("hue-bridge", [], [hub]);

        Assert.AreEqual(2, ((IHubDevice)device.Capabilities.Single()).ChildDevices.Count);
        Assert.AreEqual("hue-light-1", ((IHubDevice)device.Capabilities.Single()).ChildDevices[0].Id);
    }

    [TestMethod]
    public void RuntimeDeviceRegistry_ShouldDiffSnapshotsPerDiscoverySource()
    {
        RuntimeDeviceRegistry registry = new();
        RuntimeDevice firstLight = new("hue-light-1", []);
        RuntimeDevice secondLight = new("hue-light-2", []);
        RuntimeDevice updatedSecondLight = new("hue-light-2", []);

        RuntimeDeviceChangeSet first = registry.ApplySnapshot("hue-bridge", [firstLight, secondLight]);
        RuntimeDeviceChangeSet second = registry.ApplySnapshot("hue-bridge", [updatedSecondLight]);

        Assert.HasCount(2, first.Added);
        Assert.HasCount(0, first.Updated);
        Assert.HasCount(0, first.Removed);
        Assert.HasCount(0, second.Added);
        Assert.HasCount(1, second.Updated);
        Assert.HasCount(1, second.Removed);
        Assert.HasCount(1, registry.Devices);
        Assert.AreEqual("hue-light-2", registry.Devices[0].Id);
        Assert.AreSame(updatedSecondLight, registry.GetById("HUE-LIGHT-2"));
        Assert.IsNull(registry.GetById("hue-light-1"));
    }

    [TestMethod]
    public async Task RuntimeDiscoveryCoordinator_ShouldPollSourcesThroughTheRegistry()
    {
        RuntimeDevice device = new("hue-light-1", []);
        RuntimeDeviceRegistry registry = new();
        RuntimeDiscoveryCoordinator coordinator = new(
            [new TestDiscoverySource("hue-bridge", [device])],
            registry,
            TimeSpan.FromMinutes(1));

        IReadOnlyList<RuntimeDeviceChangeSet> changes = await coordinator.DiscoverOnceAsync();

        Assert.HasCount(1, changes);
        Assert.AreEqual("hue-bridge", changes[0].SourceId);
        Assert.HasCount(1, changes[0].Added);
        Assert.AreEqual("hue-light-1", registry.Devices[0].Id);
    }

    [TestMethod]
    public async Task ToggleCapability_ShouldBelongToOneElement()
    {
        TestToggleCapability toggle = new();
        RuntimeDevice device = new("shelly-2pm", [new DeviceElement("Switch 1", [toggle])]);

        await ((IToggleSwitchDevice)device.Elements[0].Capabilities.Single()).SetSwitchStateAsync(true);

        Assert.AreEqual("Switch 1", device.Elements[0].Name);
        Assert.IsTrue(toggle.IsOn);
    }

    [TestMethod]
    public async Task IntensityCapability_ShouldUsePercentageOnTheSameElement()
    {
        TestIntensityCapability intensity = new();
        RuntimeDevice device = new(
            "shelly-rgbw",
            [new DeviceElement("White 0", [new TestToggleCapability(), intensity])]);

        await ((IIntensityGradientDevice)device.Elements[0].Capabilities.Single(capability => capability is IIntensityGradientDevice))
            .SetIntensityAsync(37.5M);

        Assert.AreEqual(37.5M, intensity.IntensityPercent);
        Assert.AreEqual(2, device.Elements[0].Capabilities.Count);
    }

    [TestMethod]
    public void ColorModels_ShouldKeepTheirNativeComponents()
    {
        DeviceColor.Rgb rgb = new(255, 16, 0);
        DeviceColor.Xy xy = new(0.31, 0.33);

        Assert.AreEqual(ColorModel.Rgb, rgb.Model);
        Assert.AreEqual(255, rgb.Red);
        Assert.AreEqual(ColorModel.Xy, xy.Model);
        Assert.AreEqual(0.31, xy.X);
    }

    [TestMethod]
    public void ColorModels_ShouldRejectInvalidComponents()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DeviceColor.Xy(1.1, 0.3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DeviceColor.Hsv(360, 1, 1));
    }

    [TestMethod]
    public async Task ColorCapability_ShouldBeAttachedToOneElementAndDeclareSupportedModels()
    {
        TestColorCapability color = new();
        RuntimeDevice device = new("hue-light-1", [new DeviceElement("Light", [color])]);

        await ((IChromaticColorDevice)device.Elements[0].Capabilities.Single()).SetColorAsync(new DeviceColor.Rgb(255, 0, 0));

        CollectionAssert.AreEquivalent(
            new[] { ColorModel.Xy },
            color.SupportedColorModels.ToArray());
        Assert.AreEqual(ColorModel.Rgb, color.CurrentColor.Model);
    }

    private sealed class TestCapability : IDeviceCapability
    {
    }

    private sealed class TestDiscoverySource : IDeviceDiscoverySource
    {
        private readonly IReadOnlyList<IDevice> _devices;

        public TestDiscoverySource(string sourceId, IReadOnlyList<IDevice> devices)
        {
            SourceId = sourceId;
            _devices = devices;
        }

        public string SourceId { get; }

        public Task<IReadOnlyList<IDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_devices);
        }
    }

    private sealed class TestToggleCapability : IToggleSwitchDevice
    {
        public bool? IsOn { get; private set; }

        public Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
        {
            IsOn = isOn;
            return Task.CompletedTask;
        }
    }

    private sealed class TestIntensityCapability : IIntensityGradientDevice
    {
        public decimal? IntensityPercent { get; private set; }

        public Task SetIntensityAsync(decimal intensityPercent, CancellationToken cancellationToken = default)
        {
            if (intensityPercent is < 0M or > 100M)
                throw new ArgumentOutOfRangeException(nameof(intensityPercent));

            IntensityPercent = intensityPercent;
            return Task.CompletedTask;
        }
    }

    private sealed class TestColorCapability : IChromaticColorDevice
    {
        public IReadOnlyList<ColorModel> SupportedColorModels { get; } = [ColorModel.Xy];

        public DeviceColor CurrentColor { get; private set; }

        public Task SetColorAsync(DeviceColor color, CancellationToken cancellationToken = default)
        {
            CurrentColor = color;
            return Task.CompletedTask;
        }
    }

    private sealed class TestHub : IHubDevice
    {
        public TestHub(DeviceReference[] childDevices)
        {
            ChildDevices = childDevices;
        }

        public IReadOnlyList<DeviceReference> ChildDevices { get; }
    }
}