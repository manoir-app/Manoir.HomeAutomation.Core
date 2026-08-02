using System.Collections.Generic;

namespace Home.Common.Messages;

public class DeviceActionTriggeredMessage : BaseMessage
{
    public const string DeviceActionTriggered = "homeautomation.devices.action";

    public DeviceActionTriggeredMessage() : base(DeviceActionTriggered)
    {
        Attributes = new Dictionary<string, string>();
    }

    public string DeviceId { get; set; }
    public string DeviceInternalName { get; set; }
    public string DevicePlatform { get; set; }
    public string ActionKind { get; set; }
    public string Action { get; set; }
    public string RawAction { get; set; }
    public Dictionary<string, string> Attributes { get; set; }
}