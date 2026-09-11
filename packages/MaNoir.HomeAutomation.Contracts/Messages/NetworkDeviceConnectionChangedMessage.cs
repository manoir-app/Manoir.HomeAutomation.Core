using Home.Common.Messages;

namespace Home.Common.Messages;

public sealed class NetworkDeviceConnectionChangedMessage : BaseMessage
{
    public const string TopicName = "system.network.device.connection.changed";

    public NetworkDeviceConnectionChangedMessage() : base(TopicName)
    {
    }

    public string DeviceId { get; set; }
    public string DeviceName { get; set; }
    public string IpAddress { get; set; }
    public string MacAddress { get; set; }
    public string Vendor { get; set; }
    public string Model { get; set; }
    public bool IsConnected { get; set; }
}