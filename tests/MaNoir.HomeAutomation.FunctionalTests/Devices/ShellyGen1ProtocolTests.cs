using MaNoir.HomeAutomation.Protocols.Shelly;
using System.Text.Json;

namespace MaNoir.HomeAutomation.FunctionalTests.Devices;

[TestClass]
public sealed class ShellyGen1ProtocolTests
{
    [TestMethod]
    public void GetMqttTopic_ShouldUseConfiguredMqttId()
    {
        using JsonDocument settings = JsonDocument.Parse("{\"mqtt\":{\"id\":\"kitchen-plug\"}}");

        string topic = ShellyGen1Protocol.GetMqttTopic(settings.RootElement, "shellyplug-s-a1b2c3");

        Assert.AreEqual("shellies/kitchen-plug", topic);
    }

    [TestMethod]
    public void GetMqttTopic_ShouldFallbackToDetectedDeviceId()
    {
        using JsonDocument settings = JsonDocument.Parse("{\"mqtt\":{\"id\":\"\"}}");

        string topic = ShellyGen1Protocol.GetMqttTopic(settings.RootElement, "shellyplug-s-a1b2c3");

        Assert.AreEqual("shellies/shellyplug-s-a1b2c3", topic);
    }
}