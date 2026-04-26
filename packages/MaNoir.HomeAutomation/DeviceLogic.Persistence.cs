using Home.Common;
using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.Core.Contracts.Models.Entities;
using MaNoir.Core.DataPublication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed partial class DeviceLogic
{
    public async Task<List<Device>> RegisterDevicesAsync(string agentId, IEnumerable<Device> devices, CancellationToken cancellationToken = default)
    {
        List<Device> insertedDevices = [];
        if (devices == null)
            return insertedDevices;

        foreach (Device device in devices)
        {
            Device preparedDevice = PrepareForRegistration(device);
            if (preparedDevice == null)
                continue;

            Device existingDevice = null;
            if (!string.IsNullOrWhiteSpace(preparedDevice.Id))
            {
                existingDevice = await _mongoOperations.GetByIdAsync(preparedDevice.Id, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(preparedDevice.DeviceInternalName))
            {
                existingDevice = await _mongoOperations.GetByInternalNameAsync(preparedDevice.DeviceInternalName, cancellationToken);
            }

            if (existingDevice != null)
            {
                ApplyRegistrationUpdate(existingDevice, preparedDevice);
                await _mongoOperations.SaveAsync(existingDevice, cancellationToken);

                if (!string.IsNullOrWhiteSpace(preparedDevice.Id))
                    Console.WriteLine($"Device {preparedDevice.DeviceInternalName}/{preparedDevice.Id} already registered with this ID");
                else
                    Console.WriteLine($"Device {preparedDevice.DeviceInternalName} already registered as id : {existingDevice.Id}");

                continue;
            }

            if (string.IsNullOrWhiteSpace(preparedDevice.Id))
            {
                preparedDevice.Id = preparedDevice.DeviceInternalName ?? NormalizeDeviceId(Guid.NewGuid().ToString("D"));
            }

            await _mongoOperations.InsertAsync(preparedDevice, cancellationToken);
            insertedDevices.Add(preparedDevice);
        }

        return insertedDevices;
    }

    public Task<List<Device>> FindAsync(string agentId = null, string kind = null, string role = null, string id = null, bool returnIgnored = false, string meshId = "local", CancellationToken cancellationToken = default)
    {
        return _mongoOperations.FindAsync(agentId, NormalizeDeviceKind(kind), role, NormalizeDeviceId(id), returnIgnored, NormalizeMeshId(meshId), cancellationToken);
    }

    public Task<List<Device>> GetAllAsync(string meshId = "local", bool returnIgnored = false, CancellationToken cancellationToken = default)
    {
        return FindAsync(returnIgnored: returnIgnored, meshId: meshId, cancellationToken: cancellationToken);
    }

    public async Task<Device> GetByIdAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        string normalizedDeviceId = NormalizeDeviceId(deviceId);
        if (normalizedDeviceId == null)
            return null;

        return await _mongoOperations.GetByIdAsync(normalizedDeviceId, cancellationToken);
    }

    public async Task<bool> ReplaceDataAndStatusAsync(string deviceId, string status, List<DeviceData> datas, CancellationToken cancellationToken = default)
    {
        Device device = await GetByIdAsync(deviceId, cancellationToken);
        if (device == null)
            return false;

        ApplyDataAndStatus(device, status, datas);
        await _mongoOperations.SaveAsync(device, cancellationToken);
        PublishDeviceEntityBestEffort(device);
        return true;
    }

    public async Task<bool> ChangeDataAsync(string deviceId, DeviceData data, string mainStatus = null, CancellationToken cancellationToken = default)
    {
        Device device = await GetByIdAsync(deviceId, cancellationToken);
        if (device == null)
            return false;

        Console.WriteLine($"Devices - Setting {data?.Name} on {deviceId} = {data?.Value}");

        bool changed = ChangeData(device, data, mainStatus);
        if (!changed)
            return false;

        await _mongoOperations.SaveAsync(device, cancellationToken);
        PublishDeviceEntityBestEffort(device);
        return true;
    }

    public async Task<List<DeviceData>> GetDataAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        Device device = await GetByIdAsync(deviceId, cancellationToken);
        return device?.Datas;
    }

    public Task<bool> OnDeviceStateChangedAsync(string devicePlatform, string deviceId, string role, params DeviceStateChangedMessage.DeviceStateValue[] changedValues)
    {
        return OnDeviceStateChangedAsync(devicePlatform, deviceId, role, null, changedValues, CancellationToken.None);
    }

    public Task<bool> OnDeviceStateChangedAsync(string devicePlatform, string deviceId, string role, string mainStatus, params DeviceStateChangedMessage.DeviceStateValue[] changedValues)
    {
        return OnDeviceStateChangedAsync(devicePlatform, deviceId, role, mainStatus, changedValues, CancellationToken.None);
    }

    public async Task<bool> OnDeviceStateChangedAsync(string devicePlatform, string deviceId, string role, string mainStatus, IEnumerable<DeviceStateChangedMessage.DeviceStateValue> changedValues, CancellationToken cancellationToken = default)
    {
        if (changedValues == null)
            return false;

        List<DeviceStateChangedMessage.DeviceStateValue> effectiveChanges = changedValues.Where(value => value != null).ToList();
        if (effectiveChanges.Count == 0)
            return false;

        DeviceStateChangedMessage message = new DeviceStateChangedMessage(devicePlatform, deviceId, role, effectiveChanges.ToArray());
        Console.WriteLine($"Raising DeviceStateChanged for : {deviceId}/{role} : {effectiveChanges.Count} changed value(s)");

        bool anySucceeded = false;
        foreach (DeviceStateChangedMessage.DeviceStateValue change in effectiveChanges)
        {
            if (await ChangeDataAsync(deviceId, change, mainStatus, cancellationToken))
                anySucceeded = true;
        }

        PublishDeviceStateChangedBestEffort(message);
        return anySucceeded;
    }

    private static void PublishDeviceEntityBestEffort(Device device)
    {
        Entity entity = DeviceProjectedEntityRepository.CreateProjectedEntity(device);
        if (entity == null)
            return;

        try
        {
            MqttDataPublisher.PublishEntity(entity);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to publish device entity for {device.Id}: {exception.Message}");
        }
    }

    private static void PublishDeviceStateChangedBestEffort(DeviceStateChangedMessage message)
    {
        if (message == null)
            return;

        try
        {
            NatsInterprocess.Push(message);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to publish device state change for {message.DeviceId}: {exception.Message}");
        }
    }
}