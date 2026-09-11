using System;
using MaNoir.HomeAutomation.Devices;

namespace MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;

public sealed partial class ZigbeeDevice
{
    /// <summary>
    /// Tracks availability and last-seen information for a Zigbee device.
    /// </summary>
    private sealed class ZigbeeAvailabilityCapability : IRuntimeAvailabilityDevice
    {
        public bool? IsAvailable { get; private set; }

        public DateTimeOffset? LastSeenUtc { get; private set; }

        public void ApplyState()
        {
            IsAvailable = true;
            LastSeenUtc = DateTimeOffset.UtcNow;
        }

        public void ApplyAvailability(string availability)
        {
            if (string.Equals(availability, "online", StringComparison.OrdinalIgnoreCase))
                IsAvailable = true;
            else if (string.Equals(availability, "offline", StringComparison.OrdinalIgnoreCase))
                IsAvailable = false;
            else
                return;

            LastSeenUtc = DateTimeOffset.UtcNow;
        }
    }
}
