using Home.Common.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MaNoir.HomeAutomation;

public sealed partial class DiscoveredDeviceLogic
{
    private static DateTimeOffset? _discoveryEnabledSinceUtc;

    public static string NormalizeDiscoveredDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        return deviceId.Trim().ToLowerInvariant();
    }

    public static string NormalizeDiscoveredDeviceInternalName(string deviceInternalName)
    {
        if (string.IsNullOrWhiteSpace(deviceInternalName))
            return null;

        return deviceInternalName.Trim().ToLowerInvariant();
    }

    public static string NormalizeDiscoveredDeviceAgentId(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return null;

        return agentId.Trim().ToLowerInvariant();
    }

    public static string NormalizeDiscoveredDeviceKind(string deviceKind)
    {
        if (string.IsNullOrWhiteSpace(deviceKind))
            return null;

        return deviceKind.Trim().ToLowerInvariant();
    }

    public static string NormalizeDiscoveredDevicePlatform(string devicePlatform)
    {
        if (string.IsNullOrWhiteSpace(devicePlatform))
            return null;

        return devicePlatform.Trim().ToLowerInvariant();
    }

    public static List<string> NormalizeDiscoveredDeviceRoles(IEnumerable<string> deviceRoles)
    {
        List<string> normalizedRoles = new List<string>();
        if (deviceRoles == null)
            return normalizedRoles;

        foreach (string deviceRole in deviceRoles)
        {
            if (string.IsNullOrWhiteSpace(deviceRole))
                continue;

            string normalizedRole = deviceRole.Trim().ToLowerInvariant();
            if (!normalizedRoles.Exists(current => string.Equals(current, normalizedRole, StringComparison.Ordinal)))
                normalizedRoles.Add(normalizedRole);
        }

        return normalizedRoles;
    }

    public static List<string> NormalizeDiscoveredDeviceCapabilities(IEnumerable<string> deviceCapabilities)
    {
        return NormalizeDiscoveredDeviceRoles(deviceCapabilities);
    }

    public static DiscoveredDevice PrepareForDiscovery(DiscoveredDevice device)
    {
        if (device == null)
            return null;

        device.Id = NormalizeDiscoveredDeviceId(device.Id);
        device.MeshId = "local";
        device.DeviceInternalName = NormalizeDiscoveredDeviceInternalName(device.DeviceInternalName);
        device.DeviceAgentId = NormalizeDiscoveredDeviceAgentId(device.DeviceAgentId);
        device.DevicePlatform = NormalizeDiscoveredDevicePlatform(device.DevicePlatform);
        device.DeviceKind = NormalizeDiscoveredDeviceKind(device.DeviceKind);
        device.DeviceRoles = NormalizeDiscoveredDeviceRoles(device.DeviceRoles);
        device.DeviceCapabilities = NormalizeDiscoveredDeviceCapabilities(device.DeviceCapabilities);
        if (device.DiscoveryDate == default)
            device.DiscoveryDate = DateTimeOffset.Now;

        return device;
    }

    public static void EnableDiscoveryMode()
    {
        _discoveryEnabledSinceUtc = DateTimeOffset.Now;
    }

    public static bool IsDiscoveryEnabled()
    {
        if (!_discoveryEnabledSinceUtc.HasValue)
            return false;

        if (_discoveryEnabledSinceUtc.Value.AddMinutes(1) < DateTimeOffset.Now)
        {
            _discoveryEnabledSinceUtc = null;
            return false;
        }

        return true;
    }

    public static Device CreateManagedDeviceFromDiscovery(DiscoveredDevice discoveredDevice, string deviceName)
    {
        discoveredDevice = PrepareForDiscovery(discoveredDevice);
        if (discoveredDevice == null || discoveredDevice.Id == null)
            return null;

        return new Device()
        {
            Id = discoveredDevice.Id,
            ConfigurationData = discoveredDevice.DefaultConfigurationData,
            DeviceKind = discoveredDevice.DeviceKind,
            DevicePlatform = discoveredDevice.DevicePlatform,
            DeviceRoles = discoveredDevice.DeviceRoles == null ? new List<string>() : new List<string>(discoveredDevice.DeviceRoles),
            DeviceCapabilities = discoveredDevice.DeviceCapabilities == null ? new List<string>() : new List<string>(discoveredDevice.DeviceCapabilities),
            AvailableActions = CloneAvailableActions(discoveredDevice.AvailableActions),
            DeviceInternalName = discoveredDevice.DeviceInternalName,
            DeviceGivenName = deviceName,
            MeshId = discoveredDevice.MeshId
        };
    }

    private static List<DeviceAvailableAction> CloneAvailableActions(IEnumerable<DeviceAvailableAction> actions)
    {
        List<DeviceAvailableAction> result = [];
        foreach (DeviceAvailableAction action in actions ?? [])
        {
            if (action == null || string.IsNullOrWhiteSpace(action.RawAction))
                continue;

            result.Add(new DeviceAvailableAction()
            {
                ActionKind = action.ActionKind,
                Action = action.Action,
                RawAction = action.RawAction,
                Attributes = action.Attributes == null ? [] : new Dictionary<string, string>(action.Attributes)
            });
        }

        return result;
    }

    public static string GenerateDiscoveryCode(int length)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (length <= 0)
            return string.Empty;

        StringBuilder builder = new StringBuilder(length);
        byte[] buffer = new byte[length];
        RandomNumberGenerator.Fill(buffer);
        for (int index = 0; index < length; index++)
            builder.Append(alphabet[buffer[index] % alphabet.Length]);

        return builder.ToString();
    }
}