using System;
using System.Collections.Generic;

namespace Home.Common.Model;

public class DiscoveredDevice
{
    public DiscoveredDevice()
    {
        DeviceRoles = new List<string>();
        DeviceCapabilities = new List<string>();
        AvailableActions = new List<DeviceAvailableAction>();
    }

    public string DeviceCode { get; set; }
    public string Id { get; set; }
    public string MeshId { get; set; }
    public DateTimeOffset DiscoveryDate { get; set; }
    public string DeviceInternalName { get; set; }
    public string DeviceAgentId { get; set; }
    public string DevicePlatform { get; set; }
    public string DeviceKind { get; set; }
    public List<string> DeviceRoles { get; set; }
    public List<string> DeviceCapabilities { get; set; }
    public List<DeviceAvailableAction> AvailableActions { get; set; }
    public string DefaultConfigurationData { get; set; }
}