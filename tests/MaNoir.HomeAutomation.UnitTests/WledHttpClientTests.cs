using MaNoir.Agents.Sarah.Wled;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Devices.Wled;
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
public sealed class WledHttpClientTests
{
    [TestMethod]
    public async Task GetEffectsAsync_ShouldReadNativeEffectNames()
    {
        MockWledHandler handler = new("[\"Solid\",\"Rainbow\",\"Fire\"]");
        using HttpClient httpClient = new(handler);
        WledHttpClient client = new("192.168.1.50", httpClient);

        IReadOnlyList<string> effects = await client.GetEffectsAsync();

        Assert.AreEqual("/json/eff", handler.RequestUri.AbsolutePath);
        CollectionAssert.AreEqual(new[] { "Solid", "Rainbow", "Fire" }, effects.ToArray());
    }

    [TestMethod]
    public async Task SetAnimationAsync_ShouldWriteWledEffectPayload()
    {
        MockWledHandler handler = new("{}");
        using HttpClient httpClient = new(handler);
        WledHttpClient client = new("192.168.1.50", httpClient);

        await client.SetAnimationAsync(new LightAnimationRequest(
            "wled.effect.7",
            new Dictionary<string, object>()
            {
                ["palette"] = 12,
                ["speed"] = 180,
                ["intensity"] = 220
            }));

        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("/json/state", handler.RequestUri.AbsolutePath);
        StringAssert.Contains(handler.Body, "\"fx\":7");
        StringAssert.Contains(handler.Body, "\"pal\":12");
        StringAssert.Contains(handler.Body, "\"sx\":180");
        StringAssert.Contains(handler.Body, "\"ix\":220");
    }

    [TestMethod]
    public async Task SetAnimationAsync_None_ShouldTurnWledOff()
    {
        MockWledHandler handler = new("{}");
        using HttpClient httpClient = new(handler);
        WledHttpClient client = new("192.168.1.50", httpClient);

        await client.SetAnimationAsync(new LightAnimationRequest("wled.none"));

        Assert.AreEqual(HttpMethod.Post, handler.Method);
        StringAssert.Contains(handler.Body, "\"id\":0");
        StringAssert.Contains(handler.Body, "\"fx\":0");
    }

    [TestMethod]
    public async Task WledDevice_ShouldExposeAndTargetEachSegment()
    {
        List<int> stateSegments = [];
        List<int> animationSegments = [];
        WledDevice device = WledDevice.Create(
            "strip",
            (segmentId, isOn, intensity, color, cancellationToken) =>
            {
                stateSegments.Add(segmentId);
                return Task.CompletedTask;
            },
            (segmentId, request, cancellationToken) =>
            {
                animationSegments.Add(segmentId);
                return Task.CompletedTask;
            },
            [
                new LightAnimationDefinition("wled.effect.0", "Solid", "effect", true),
                new LightAnimationDefinition("wled.effect.1", "Rainbow", "effect", true)
            ]);

        using JsonDocument state = JsonDocument.Parse("""
        {
            "on": true,
            "bri": 128,
            "seg": [
                { "id": 0, "fx": 1, "col": [[255, 0, 0]] },
                { "id": 1, "fx": 0, "col": [[0, 0, 255]] }
            ]
        }
        """);

        device.ApplyState(state.RootElement);

        Assert.HasCount(2, device.Elements);
        Assert.AreEqual("Segment 1", device.Elements[1].Name);
        await ((IChromaticColorDevice)device.Elements[1].Capabilities.Single(capability => capability is IChromaticColorDevice))
            .SetColorAsync(new DeviceColor.Rgb(0, 255, 0));
        await ((ILightAnimationDevice)device.Elements[1].Capabilities.Single(capability => capability is ILightAnimationDevice))
            .StartAnimationAsync(new LightAnimationRequest("wled.effect.1"));

        CollectionAssert.AreEqual(new[] { 1 }, stateSegments.ToArray());
        CollectionAssert.AreEqual(new[] { 1 }, animationSegments.ToArray());
    }

    [TestMethod]
    public async Task WledDevice_ShouldExposeGlobalPowerCapability()
    {
        List<bool> requestedStates = [];
        WledDevice device = WledDevice.Create(
            "strip",
            (segmentId, isOn, intensity, color, cancellationToken) => Task.CompletedTask,
            null,
            [],
            (isOn, cancellationToken) =>
            {
                requestedStates.Add(isOn);
                return Task.CompletedTask;
            });

        IToggleSwitchDevice power = (IToggleSwitchDevice)device.Capabilities.Single();
        await power.SetSwitchStateAsync(false);

        CollectionAssert.AreEqual(new[] { false }, requestedStates.ToArray());
    }

    private sealed class MockWledHandler : HttpMessageHandler
    {
        private readonly string _response;

        public MockWledHandler(string response)
        {
            _response = response;
        }

        public HttpMethod Method { get; private set; }

        public Uri RequestUri { get; private set; }

        public string Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            };
        }
    }
}
