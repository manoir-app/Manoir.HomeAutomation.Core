using Home.Common.Model;
using System.Collections.Generic;

namespace Home.Common.Messages;

public class DeviceStateChangedMessage : BaseMessage
{
    public class DeviceStateValue
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public DeviceDataCategory Category { get; set; }
        public string StandardDataType { get; set; }
        public string ValueUnit { get; set; }
        public bool IsMainData { get; set; }

        public static implicit operator DeviceData(DeviceStateValue stateValue)
        {
            return new DeviceData()
            {
                Name = stateValue.Name,
                Value = stateValue.Value,
                Category = stateValue.Category,
                StandardDataType = stateValue.StandardDataType,
                ValueUnit = stateValue.ValueUnit,
                IsMainData = stateValue.IsMainData
            };
        }

        public static implicit operator DeviceStateValue(DeviceData data)
        {
            return new DeviceStateValue()
            {
                Name = data.Name,
                Value = data.Value,
                Category = data.Category,
                StandardDataType = data.StandardDataType,
                ValueUnit = data.ValueUnit,
                IsMainData = data.IsMainData
            };
        }
    }

    public const string DeviceStateChanged = "homeautomation.devices.state.changed";

    public DeviceStateChangedMessage() : base(DeviceStateChanged)
    {
        ChangedValues = new List<DeviceStateValue>();
    }

    public DeviceStateChangedMessage(string devicePlatform, string deviceId, string role, params DeviceStateValue[] changedValues) : this()
    {
        DevicePlatform = devicePlatform;
        DeviceId = deviceId;
        DeviceRole = role;
        ChangedValues.AddRange(changedValues);
    }

    public string DevicePlatform { get; set; }
    public string DeviceId { get; set; }
    public string DeviceRole { get; set; }
    public List<DeviceStateValue> ChangedValues { get; set; }
}