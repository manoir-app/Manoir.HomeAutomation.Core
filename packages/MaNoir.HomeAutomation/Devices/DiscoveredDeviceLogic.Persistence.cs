using Home.Common;
using Home.Common.Messages;
using Home.Common.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed partial class DiscoveredDeviceLogic
{
    public Task<List<DiscoveredDevice>> GetAllAsync(string kind = null, string agentId = null, string meshId = "local", CancellationToken cancellationToken = default)
    {
        return _mongoOperations.FindAsync(
            NormalizeDiscoveredDeviceKind(kind),
            NormalizeDiscoveredDeviceAgentId(agentId),
            string.IsNullOrWhiteSpace(meshId) ? "local" : meshId.Trim().ToLowerInvariant(),
            cancellationToken);
    }

    public async Task<List<DiscoveredDevice>> GetForAgentAsync(string agentId, string meshId = "local", CancellationToken cancellationToken = default)
    {
        if (!IsDiscoveryEnabled())
            return new List<DiscoveredDevice>();

        return await GetAllAsync(agentId: agentId, meshId: meshId, cancellationToken: cancellationToken);
    }

    public async Task ClearOldAsync(string meshId = "local", CancellationToken cancellationToken = default)
    {
        string normalizedMeshId = string.IsNullOrWhiteSpace(meshId) ? "local" : meshId.Trim().ToLowerInvariant();
        await _mongoOperations.DeleteOlderThanAsync(normalizedMeshId, DateTimeOffset.Now.AddHours(-1), cancellationToken);
    }

    public async Task<bool> CheckIfAssociatedAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        string normalizedDeviceId = NormalizeDiscoveredDeviceId(deviceId);
        if (normalizedDeviceId == null)
            return false;

        return await _deviceMongoOperations.GetByIdAsync(normalizedDeviceId, cancellationToken) != null;
    }

    public async Task<DiscoveredDevice> CreateForAppDeviceAsync(CancellationToken cancellationToken = default)
    {
        DiscoveredDevice discoveredDevice = new DiscoveredDevice()
        {
            DiscoveryDate = DateTimeOffset.Now,
            DefaultConfigurationData = null,
            DeviceAgentId = null,
            DeviceCode = GenerateDiscoveryCode(6),
            DeviceInternalName = string.Empty,
            DevicePlatform = "WebApp",
            DeviceKind = Device.DeviceKindMobileDevice,
            MeshId = "local",
            Id = Guid.NewGuid().ToString("D").ToLowerInvariant()
        };

        PrepareForDiscovery(discoveredDevice);
        await _mongoOperations.InsertAsync(discoveredDevice, cancellationToken);
        return discoveredDevice;
    }

    public async Task<Device> ValidateAppDeviceAsync(string deviceId, string deviceName, CancellationToken cancellationToken = default)
    {
        string normalizedDeviceId = NormalizeDiscoveredDeviceId(deviceId);
        if (normalizedDeviceId == null)
            return null;

        DiscoveredDevice discoveredDevice = await _mongoOperations.GetByIdAsync(normalizedDeviceId, cancellationToken);
        if (discoveredDevice == null)
            return null;

        Device existingDevice = await _deviceMongoOperations.GetByIdAsync(normalizedDeviceId, cancellationToken);
        if (existingDevice != null)
            return existingDevice;

        Device managedDevice = CreateManagedDeviceFromDiscovery(discoveredDevice, deviceName);
        if (managedDevice == null)
            return null;

        await _deviceMongoOperations.InsertAsync(managedDevice, cancellationToken);
        return managedDevice;
    }

    public async Task<bool> UpsertAsync(DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        if (!IsDiscoveryEnabled())
            return false;

        return await UpsertCoreAsync(device, cancellationToken) > 0;
    }

    public async Task<bool> UpsertManyAsync(IEnumerable<DiscoveredDevice> devices, CancellationToken cancellationToken = default)
    {
        if (!IsDiscoveryEnabled() || devices == null)
            return false;

        int changed = 0;
        int total = 0;
        foreach (DiscoveredDevice device in devices)
        {
            total++;
            changed += await UpsertCoreAsync(device, cancellationToken);
        }

        return total > 0 && changed == total;
    }

    private async Task<int> UpsertCoreAsync(DiscoveredDevice device, CancellationToken cancellationToken)
    {
        DiscoveredDevice preparedDevice = PrepareForDiscovery(device);
        if (preparedDevice == null || preparedDevice.DeviceKind == null || preparedDevice.DevicePlatform == null)
            return 0;

        if (!string.IsNullOrWhiteSpace(preparedDevice.DeviceInternalName) && !string.IsNullOrWhiteSpace(preparedDevice.DeviceAgentId))
        {
            Device existingManagedDevice = await _deviceMongoOperations.GetByInternalNameAsync(preparedDevice.DeviceInternalName, cancellationToken);
            if (existingManagedDevice != null && string.Equals(existingManagedDevice.DeviceAgentId, preparedDevice.DeviceAgentId, StringComparison.Ordinal))
                return 0;

            if (preparedDevice.Id == null)
            {
                DiscoveredDevice existingDiscoveredDevice = await _mongoOperations.GetByInternalNameAndAgentAsync(preparedDevice.DeviceInternalName, preparedDevice.DeviceAgentId, cancellationToken);
                preparedDevice.Id = existingDiscoveredDevice?.Id ?? Guid.NewGuid().ToString("N");
            }
        }

        if (preparedDevice.Id == null)
            preparedDevice.Id = Guid.NewGuid().ToString("N");

        await _mongoOperations.SaveAsync(preparedDevice, cancellationToken);
        PublishDiscoveredDeviceBestEffort(preparedDevice);
        return 1;
    }

    private static void PublishDiscoveredDeviceBestEffort(DiscoveredDevice device)
    {
        if (device == null)
            return;

        try
        {
            string topic = string.Concat(device.DeviceKind, ".discovery.", device.DevicePlatform);
            NatsInterprocess.Push(new DeviceDiscoveredMessage(topic)
            {
                Device = device,
                DiscoveryTime = DateTimeOffset.Now
            });

            if (device.DeviceRoles == null)
                return;

            foreach (string role in device.DeviceRoles)
            {
                NatsInterprocess.Push(new DeviceDiscoveredMessage(string.Concat(device.DeviceKind, ".discovery.", device.DevicePlatform, ".", role))
                {
                    Device = device,
                    DiscoveryTime = DateTimeOffset.Now
                });
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to publish discovered device {device.Id}: {exception.Message}");
        }
    }
}