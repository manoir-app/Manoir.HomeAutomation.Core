using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.Agents.Sarah;

namespace MaNoir.HomeAutomation.FunctionalTests.Triggers;

[TestClass]
public sealed class NetworkTriggerRuntimeTests
{
    [TestMethod]
    [TestCategory("Functional")]
    public void NetworkTrigger_ShouldMatchDeviceIdentityAndConnectionState()
    {
        Trigger trigger = new Trigger()
        {
            Kind = TriggerKind.NetworkDeviceConnectionChanged,
            NetworkDeviceName = "living-room-tv",
            NetworkDeviceTriggerKind = NetworkDeviceTriggerKind.Connection
        };

        NetworkDeviceConnectionChangedMessage message = new NetworkDeviceConnectionChangedMessage()
        {
            DeviceId = "living-room-tv",
            DeviceName = "Living room TV",
            IsConnected = true
        };

        Assert.IsTrue(TriggerRuntimeService.MatchesNetworkDevice(trigger, message));
        Assert.IsTrue(TriggerRuntimeService.MatchesNetworkState(trigger, message.IsConnected));
        Assert.IsFalse(TriggerRuntimeService.MatchesNetworkState(trigger, false));
    }
}