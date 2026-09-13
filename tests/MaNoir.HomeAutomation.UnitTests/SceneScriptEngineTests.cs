using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Scripting;
using Jint.Runtime;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class SceneScriptEngineTests
{
    [TestMethod]
    public void Execute_ShouldExposeStandardDeviceOperations()
    {
        RecordingToggle toggle = new();
        RecordingIntensity intensity = new();
        RuntimeDevice device = new("kitchen-light", [new DeviceElement("Main", [toggle, intensity])]);

        new SceneScriptEngine().Execute(
            "var light = home.devices.find('kitchen-light'); var element = light.elements[0]; element.setSwitch(true); element.setIntensity(40);",
            [device]);

        Assert.IsTrue(toggle.IsOn);
        Assert.AreEqual(40M, intensity.IntensityPercent);
    }

    [TestMethod]
    public void Execute_ShouldExposeParametersWithoutExposingClrTypes()
    {
        RecordingToggle toggle = new();
        RuntimeDevice device = new("hall-light", [new DeviceElement("Main", [toggle])]);

        new SceneScriptEngine().Execute(
            "home.devices.find('hall-light').elements[0].setSwitch(home.parameters['state'] === 'on');",
            [device],
            new Dictionary<string, string>() { ["state"] = "on" });

        Assert.IsTrue(toggle.IsOn);
    }

    [TestMethod]
    public void Execute_ShouldStopInfiniteScript()
    {
        SceneScriptEngine engine = new(timeout: System.TimeSpan.FromMilliseconds(100), maxStatements: 1_000);

        Assert.ThrowsExactly<StatementsCountOverflowException>(() =>
        {
            engine.Execute("while (true) { }", []);
        });
    }

    private sealed class RecordingToggle : IToggleSwitchDevice
    {
        public bool? IsOn { get; private set; }

        public Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
        {
            IsOn = isOn;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingIntensity : IIntensityGradientDevice
    {
        public decimal? IntensityPercent { get; private set; }

        public Task SetIntensityAsync(decimal intensityPercent, CancellationToken cancellationToken = default)
        {
            IntensityPercent = intensityPercent;
            return Task.CompletedTask;
        }
    }
}