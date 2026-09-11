using Home.Common.Model;
using MaNoir.Agents.Sarah.Hue;
using MaNoir.Agents.Sarah;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Devices.Hue;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class HueRuntimeServiceTests
{
    [TestMethod]
    public void CreateDevices_ShouldMapBridgeAndLightCapabilities()
    {
        Dictionary<string, HueLight> lights = new()
        {
            ["7"] = new HueLight()
            {
                Name = "Living room",
                State = new HueLightState()
                {
                    On = true,
                    Brightness = 127,
                    Color = new HueColor() { X = 0.31, Y = 0.33 }
                },
                Capabilities = new HueLightCapabilities()
                {
                    Control = new HueLightControl()
                    {
                        ColorGamut = new[] { new[] { 0.7, 0.3 }, new[] { 0.2, 0.7 }, new[] { 0.1, 0.1 } },
                        ColorTemperature = new HueColorTemperatureRange() { Minimum = 153, Maximum = 500 }
                    }
                }
            }
        };

        List<Device> devices = HueRuntimeService.CreateDevices("192.168.1.20", lights);
        Device bridge = devices[0];
        Device light = devices[1];

        Assert.AreEqual("hue-bridge", bridge.Id);
        Assert.Contains("hue-bridge", bridge.DeviceRoles);
        Assert.AreEqual("hue-7", light.Id);
        Assert.AreNotEqual(bridge.Id, light.Id);
        CollectionAssert.AreEquivalent(
            new[] { Device.HomeAutomationRoleSwitch, Device.HomeAutomationMainRoleLight, Device.HomeAutomationRoleDimmer, Device.HomeAutomationRoleColorBound },
            light.DeviceRoles);
        CollectionAssert.Contains(light.DeviceCapabilities, Device.CapabilityColorXy);
        CollectionAssert.Contains(light.DeviceCapabilities, Device.CapabilityColorTemperature);
        Assert.AreEqual("on", light.Datas.Find(data => data.Name == "Switch")?.Value);
        Assert.AreEqual("50", light.Datas.Find(data => data.Name == "Brightness")?.Value);
    }

    [TestMethod]
    public void BuildLightsUri_ShouldAcceptAddressWithOrWithoutScheme()
    {
        Assert.AreEqual(
            "http://192.168.1.20/api/test-key/lights",
            HueRuntimeService.BuildLightsUri("192.168.1.20", "test-key").ToString());
        Assert.AreEqual(
            "https://hue.local/api/test-key/lights",
            HueRuntimeService.BuildLightsUri("https://hue.local/", "test-key").ToString());
    }

    [TestMethod]
    public void CreateRuntimeBridge_ShouldExposeDiscoveredLightsAsChildReferences()
    {
        Dictionary<string, HueLight> lights = new()
        {
            ["3"] = new HueLight() { Name = "Kitchen" },
            ["7"] = new HueLight() { Name = "Living room" }
        };
        HueRuntimeService runtimeService = new(NullLogger<HueRuntimeService>.Instance);

        HueBridgeDevice bridge = HueRuntimeService.CreateRuntimeBridge(
            "192.168.1.20",
            "test-key",
            lights,
            runtimeService);
        IHubDevice hub = (IHubDevice)bridge.Capabilities[0];

        Assert.AreEqual("hue-bridge", bridge.Id);
        Assert.AreEqual("192.168.1.20", bridge.BridgeAddress);
        CollectionAssert.AreEquivalent(
            new[] { "hue-3", "hue-7" },
            hub.ChildDevices.Select(reference => reference.Id).ToArray());
        Assert.AreEqual(0, bridge.Elements.Count);
    }

    [TestMethod]
    public void TryCreateCommand_ShouldConvertSwitchAndBrightness()
    {
        Assert.IsTrue(HueRuntimeService.TryCreateCommand(
            new SceneStep() { Message = Device.HomeAutomationRoleSwitch, MessageBody = "on" },
            out Dictionary<string, object> switchCommand));
        Assert.AreEqual(true, switchCommand["on"]);

        Assert.IsTrue(HueRuntimeService.TryCreateCommand(
            new SceneStep() { Message = Device.HomeAutomationRoleDimmer, MessageBody = "50" },
            out Dictionary<string, object> brightnessCommand));
        Assert.AreEqual(127, brightnessCommand["bri"]);
        Assert.IsFalse(HueRuntimeService.TryCreateCommand(
            new SceneStep() { Message = Device.HomeAutomationRoleDimmer, MessageBody = "101" },
            out _));
        Assert.IsTrue(HueRuntimeService.TryCreateCommand(
            new SceneStep() { Message = Device.HomeAutomationRoleDimmer, MessageBody = "0" },
            out Dictionary<string, object> minimumBrightnessCommand));
        Assert.AreEqual(1, minimumBrightnessCommand["bri"]);
        Assert.IsTrue(JsonSerializer.Serialize(brightnessCommand).Contains("\"bri\":127", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TryCreateCommand_ShouldConvertHueColorSpacesToNativePayloads()
    {
        Device xyDevice = new() { DeviceCapabilities = [Device.CapabilityColorXy] };
        Assert.IsTrue(HueRuntimeService.TryCreateCommand(
            new SceneStep() { Message = Device.HomeAutomationRoleColorBound, MessageBody = "{\"rgb\":\"#FF0000\"}" },
            xyDevice,
            out Dictionary<string, object> xyCommand));
        double[] xy = (double[])xyCommand["xy"];
        Assert.AreEqual(0.64, xy[0], 0.02);
        Assert.AreEqual(0.33, xy[1], 0.02);

        Device hsDevice = new() { DeviceCapabilities = [Device.CapabilityColorHs] };
        Assert.IsTrue(HueRuntimeService.TryCreateCommand(
            new SceneStep() { Message = Device.HomeAutomationRoleColorBound, MessageBody = "{\"rgb\":\"#FF0000\"}" },
            hsDevice,
            out Dictionary<string, object> hsCommand));
        Assert.AreEqual(0, hsCommand["hue"]);
        Assert.AreEqual(254, hsCommand["sat"]);

        Device temperatureDevice = new()
        {
            DeviceCapabilities = [Device.CapabilityColorTemperature],
            ConfigurationData = "{\"capabilities\":{\"control\":{\"ct\":{\"min\":200,\"max\":400}}}}"
        };
        Assert.IsTrue(HueRuntimeService.TryCreateCommand(
            new SceneStep() { Message = Device.HomeAutomationRoleColorBound, MessageBody = "{\"temperatureKelvin\":6500}" },
            temperatureDevice,
            out Dictionary<string, object> temperatureCommand));
        Assert.AreEqual(200, temperatureCommand["ct"]);
    }

    [TestMethod]
    public void ClipToColorGamut_ShouldProjectOutOfRangeColorOnNearestEdge()
    {
        (double x, double y) clipped = HueRuntimeService.ClipToColorGamut(
            0.64,
            0.33,
            [
                [0.3, 0.3],
                [0.4, 0.3],
                [0.3, 0.4]
            ]);

        Assert.AreEqual(0.4, clipped.x, 0.001);
        Assert.AreEqual(0.3, clipped.y, 0.001);
    }

    [TestMethod]
    public async Task FetchLightsAsync_ShouldReadLightsFromMockBridge()
    {
        MockHueBridgeHandler handler = new();
        using HttpClient httpClient = new(handler);
        HueRuntimeService service = new(NullLogger<HueRuntimeService>.Instance, httpClient);

        Dictionary<string, HueLight> lights = await service.FetchLightsAsync(
            "192.168.1.20",
            "test-key");

        Assert.AreEqual("GET", handler.Method);
        Assert.AreEqual("/api/test-key/lights", handler.RequestUri?.AbsolutePath);
        Assert.AreEqual(1, lights.Count);
        Assert.AreEqual("Kitchen", lights["3"].Name);
        Assert.IsTrue(lights["3"].State.On);
        Assert.AreEqual(200, lights["3"].State.Brightness);
        Assert.AreEqual(0.31, lights["3"].State.Color.X, 0.001);
        Assert.IsFalse(lights["3"].State.Reachable);
    }

    [TestMethod]
    public async Task HueDiscoverySource_ShouldReturnBridgeAndDiscoveredLights()
    {
        MockHueBridgeHandler handler = new();
        using HttpClient httpClient = new(handler);
        HueRuntimeService runtimeService = new(NullLogger<HueRuntimeService>.Instance, httpClient);
        HueDiscoverySource source = new(runtimeService, "192.168.1.20", "test-key");

        IReadOnlyList<IDevice> devices = await source.DiscoverAsync();

        Assert.AreEqual("hue-bridge", source.SourceId);
        Assert.AreEqual(2, devices.Count);
        Assert.AreEqual("hue-bridge", devices[0].Id);
        Assert.AreEqual("hue-3", devices[1].Id);
        Assert.AreEqual("hue-3", ((IHubDevice)devices[0].Capabilities[0]).ChildDevices[0].Id);
    }

    [TestMethod]
    public async Task SendCommandAsync_ShouldWriteHueStatePayloadToMockBridge()
    {
        MockHueBridgeHandler handler = new();
        using HttpClient httpClient = new(handler);
        HueRuntimeService service = new(NullLogger<HueRuntimeService>.Instance, httpClient);

        using HttpResponseMessage response = await service.SendCommandAsync(
            "192.168.1.20",
            "test-key",
            "3",
            new Dictionary<string, object>() { ["bri"] = 127 });

        Assert.IsTrue(response.IsSuccessStatusCode);
        Assert.AreEqual("PUT", handler.Method);
        Assert.AreEqual("/api/test-key/lights/3/state", handler.RequestUri?.AbsolutePath);
        Assert.AreEqual("{\"bri\":127}", handler.Body);
    }

    [TestMethod]
    public async Task HueLightDevice_ShouldComposeCapabilitiesAndSendCommands()
    {
        MockHueBridgeHandler handler = new();
        using HttpClient httpClient = new(handler);
        HueRuntimeService runtimeService = new(NullLogger<HueRuntimeService>.Instance, httpClient);
        HueLight light = new()
        {
            Name = "Kitchen",
            State = new HueLightState() { On = false, Brightness = 127 },
            Capabilities = new HueLightCapabilities()
            {
                Control = new HueLightControl()
                {
                    ColorGamut = [[0.7, 0.3], [0.2, 0.7], [0.1, 0.1]],
                    ColorTemperature = new HueColorTemperatureRange() { Minimum = 153, Maximum = 500 }
                }
            }
        };

        IDevice device = HueLightDevice.Create("hue-3", "192.168.1.20", "test-key", light, runtimeService.Protocol, HueRuntimeService.CreateCommand);
        IDeviceElement element = device.Elements[0];
        IToggleSwitchDevice toggle = (IToggleSwitchDevice)element.Capabilities[0];
        IChromaticColorDevice color = (IChromaticColorDevice)element.Capabilities[2];
        IColorTemperatureDevice temperature = (IColorTemperatureDevice)element.Capabilities[3];

        await toggle.SetSwitchStateAsync(true);
        Assert.AreEqual("{\"on\":true}", handler.Body);

        await color.SetColorAsync(new DeviceColor.Rgb(255, 0, 0));
        Assert.IsTrue(handler.Body.Contains("\"xy\"", StringComparison.Ordinal));
        Assert.IsTrue(handler.Body.Contains("0.64", StringComparison.Ordinal));
        Assert.AreEqual(ColorModel.Rgb, color.CurrentColor.Model);

        await temperature.SetColorTemperatureAsync(4000);
        Assert.IsTrue(handler.Body.Contains("\"ct\"", StringComparison.Ordinal));
        Assert.AreEqual(4000, temperature.CurrentKelvin);
        Assert.AreEqual(4, element.Capabilities.Count);
    }

    [TestMethod]
    public async Task FetchLightsAsync_ShouldIncludeHueErrorBody()
    {
        MockHueBridgeHandler handler = new()
        {
            StatusCode = HttpStatusCode.Unauthorized,
            ResponseBody = "{\"error\":{\"type\":1,\"description\":\"unauthorized user\"}}"
        };
        using HttpClient httpClient = new(handler);
        HueRuntimeService service = new(NullLogger<HueRuntimeService>.Instance, httpClient);

        HttpRequestException exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => service.FetchLightsAsync("192.168.1.20", "bad-key"));

        StringAssert.Contains(exception.Message, "unauthorized user");
    }

    [TestMethod]
    public async Task FetchLightsAsync_ShouldCancelSlowBridgeRequest()
    {
        MockHueBridgeHandler handler = new() { Delay = TimeSpan.FromSeconds(1) };
        using HttpClient httpClient = new(handler);
        HueRuntimeService service = new(
            NullLogger<HueRuntimeService>.Instance,
            httpClient,
            TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => service.FetchLightsAsync("192.168.1.20", "test-key"));
    }

    private sealed class MockHueBridgeHandler : HttpMessageHandler
    {
        public string Method { get; private set; }
        public Uri RequestUri { get; private set; }
        public string Body { get; private set; }
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = "{\"3\":{\"name\":\"Kitchen\",\"state\":{\"on\":true,\"bri\":200,\"xy\":[0.31,0.33],\"reachable\":false}}}";
        public TimeSpan Delay { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);

            Method = request.Method.Method;
            RequestUri = request.RequestUri;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody)
            };
        }
    }
}