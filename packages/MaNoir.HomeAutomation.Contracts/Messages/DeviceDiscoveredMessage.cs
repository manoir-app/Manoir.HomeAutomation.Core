using Home.Common.Model;
using System;

namespace Home.Common.Messages;

public class DeviceDiscoveredMessage : BaseMessage
{
    public DeviceDiscoveredMessage() : base(string.Empty)
    {
    }

    public DeviceDiscoveredMessage(string messageTopic) : base(messageTopic)
    {
    }

    public DiscoveredDevice Device { get; set; }
    public DateTimeOffset DiscoveryTime { get; set; }
}