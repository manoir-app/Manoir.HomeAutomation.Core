using Home.Common.Model;
using MaNoir.Agents.Sarah.Hue;
using MaNoir.Agents.Sarah;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Devices.Hue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class RuntimeSceneStepExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_ShouldTargetTheRequestedElementAndToggleIt()
    {
        RecordingToggle firstToggle = new();
        RecordingToggle secondToggle = new();
        RuntimeDevice device = new(
            "multi-switch",
            [
                new DeviceElement("Switch 1", [firstToggle]),
                new DeviceElement("Switch 2", [secondToggle])
            ]);

        bool executed = await new RuntimeSceneStepExecutor().ExecuteAsync(device, new SceneStep()
        {
            TargetKind = SceneStepTargetKind.Device,
            TargetDataName = "switch 2",
            Message = Device.HomeAutomationRoleSwitch,
            MessageBody = "on"
        });

        Assert.IsTrue(executed);
        Assert.IsTrue(firstToggle.IsOn != true);
        Assert.IsTrue(secondToggle.IsOn);
    }

    [TestMethod]
    public async Task ExecuteAsync_ShouldParseRgbColor()
    {
        RecordingColor color = new();
        RuntimeDevice device = new("light", [new DeviceElement("Light", [color])]);

        bool executed = await new RuntimeSceneStepExecutor().ExecuteAsync(device, new SceneStep()
        {
            TargetKind = SceneStepTargetKind.Device,
            Message = Device.HomeAutomationRoleColorBound,
            MessageBody = "{\"rgb\":\"#FF1000\"}"
        });

        Assert.IsTrue(executed);
        Assert.AreEqual(new DeviceColor.Rgb(255, 16, 0), color.CurrentColor);
    }

    [TestMethod]
    public async Task ExecuteAsync_ShouldUseWhiteTemperatureCapabilitySeparately()
    {
        RecordingTemperature temperature = new();
        RuntimeDevice device = new("light", [new DeviceElement("Light", [temperature])]);

        bool executed = await new RuntimeSceneStepExecutor().ExecuteAsync(device, new SceneStep()
        {
            TargetKind = SceneStepTargetKind.Device,
            Message = "color-temperature",
            MessageBody = "{\"kelvin\":3000}"
        });

        Assert.IsTrue(executed);
        Assert.AreEqual(3000, temperature.CurrentKelvin);
    }

    [TestMethod]
    public async Task ExecuteAsync_ShouldSendARealHueRuntimeCapabilityCommand()
    {
        HueHandler handler = new();
        using HttpClient httpClient = new(handler);
        HueRuntimeService runtimeService = new(Microsoft.Extensions.Logging.Abstractions.NullLogger<HueRuntimeService>.Instance, httpClient);
        IDevice device = HueLightDevice.Create(
            "hue-3",
            "192.168.1.20",
            "test-key",
            new HueLight()
            {
                Capabilities = new HueLightCapabilities()
                {
                    Control = new HueLightControl()
                    {
                        ColorGamut = [[0.7, 0.3], [0.2, 0.7], [0.1, 0.1]]
                    }
                }
            },
            runtimeService.Protocol,
            HueRuntimeService.CreateCommand);

        bool executed = await new RuntimeSceneStepExecutor().ExecuteAsync(device, new SceneStep()
        {
            TargetKind = SceneStepTargetKind.Device,
            TargetDataName = "Light",
            Message = Device.HomeAutomationRoleColorBound,
            MessageBody = "{\"rgb\":\"#FF0000\"}"
        });

        Assert.IsTrue(executed);
        Assert.AreEqual("PUT", handler.Method);
        Assert.AreEqual("/api/test-key/lights/3/state", handler.RequestUri.AbsolutePath);
        StringAssert.Contains(handler.Body, "\"xy\"");
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

    private sealed class RecordingColor : IChromaticColorDevice
    {
        public IReadOnlyList<ColorModel> SupportedColorModels { get; } = [ColorModel.Rgb];

        public DeviceColor CurrentColor { get; private set; }

        public Task SetColorAsync(DeviceColor color, CancellationToken cancellationToken = default)
        {
            CurrentColor = color;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTemperature : IColorTemperatureDevice
    {
        public int? CurrentKelvin { get; private set; }

        public int MinimumKelvin => 1500;

        public int MaximumKelvin => 6500;

        public Task SetColorTemperatureAsync(int kelvin, CancellationToken cancellationToken = default)
        {
            CurrentKelvin = kelvin;
            return Task.CompletedTask;
        }
    }

    private sealed class HueHandler : HttpMessageHandler
    {
        public string Method { get; private set; }

        public Uri RequestUri { get; private set; }

        public string Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method.Method;
            RequestUri = request.RequestUri;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
