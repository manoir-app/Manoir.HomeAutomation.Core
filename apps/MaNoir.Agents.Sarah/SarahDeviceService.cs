using Home.Common.Messages;
using Home.Common.Model;
using Home.Common;
using MaNoir.HomeAutomation;
using MaNoir.HomeAutomation.Devices;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah;

public sealed class SarahDeviceService
{
    private readonly ILogger<SarahDeviceService> _logger;
    private DeviceLogic _deviceLogic;
    private bool _persistenceUnavailable;
    private bool _persistenceWarningLogged;

    public SarahDeviceService(ILogger<SarahDeviceService> logger, RuntimeDeviceRegistry runtimeRegistry = null)
    {
        _logger = logger;
        if (runtimeRegistry != null)
            runtimeRegistry.DeviceAdded += OnRuntimeDeviceAdded;
    }

    public async Task<Device> GetByIdOrInternalNameAsync(string deviceId, string devicePlatform, CancellationToken cancellationToken = default)
    {
        DeviceLogic deviceLogic = TryGetDeviceLogic();
        if (deviceLogic == null)
            return null;

        return await deviceLogic.GetByIdAsync(deviceId, cancellationToken)
            ?? await deviceLogic.GetByInternalNameAndPlatformAsync(deviceId, devicePlatform, cancellationToken);
    }

    public async Task<IReadOnlyList<Device>> RegisterDevicesAsync(string agentId, IEnumerable<Device> devices, CancellationToken cancellationToken = default)
    {
        DeviceLogic deviceLogic = TryGetDeviceLogic();
        if (deviceLogic == null)
            return Array.Empty<Device>();

        return await deviceLogic.RegisterDevicesAsync(agentId, devices, cancellationToken);
    }

    public async Task<bool> ChangeStatusAsync(string devicePlatform, string deviceId, string status, CancellationToken cancellationToken = default)
    {
        DeviceLogic deviceLogic = TryGetDeviceLogic();
        return deviceLogic != null
            && await deviceLogic.ChangeStatusAsync(devicePlatform, deviceId, status, cancellationToken);
    }

    public async Task<bool> OnDeviceStateChangedAsync(
        string devicePlatform,
        string deviceId,
        string role,
        string mainStatus,
        IEnumerable<DeviceStateChangedMessage.DeviceStateValue> changes,
        CancellationToken cancellationToken = default)
    {
        if (changes == null)
            return false;

        List<DeviceStateChangedMessage.DeviceStateValue> effectiveChanges = new();
        foreach (DeviceStateChangedMessage.DeviceStateValue change in changes)
        {
            if (change != null)
                effectiveChanges.Add(change);
        }

        if (effectiveChanges.Count == 0)
            return false;

        DeviceLogic deviceLogic = TryGetDeviceLogic();
        return deviceLogic != null
            && await deviceLogic.PersistDeviceStateChangedAsync(devicePlatform, deviceId, role, mainStatus, effectiveChanges, cancellationToken);
    }

    private void OnRuntimeDeviceAdded(object sender, RuntimeDeviceChangeSet changeSet)
    {
        foreach (IDevice device in changeSet.Added)
        {
            if (device is IRuntimeDeviceEvents runtimeDevice)
                runtimeDevice.StateChanged += OnRuntimeDeviceStateChanged;
        }
    }

    private async void OnRuntimeDeviceStateChanged(object sender, RuntimeDeviceStateChangedEventArgs eventArgs)
    {
        try
        {
            await OnDeviceStateChangedAsync(
                eventArgs.Platform,
                eventArgs.Device.Id,
                eventArgs.Role,
                eventArgs.MainStatus,
                eventArgs.Changes);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to process runtime state change for {DeviceId}.", eventArgs.Device.Id);
        }
    }

    private DeviceLogic TryGetDeviceLogic()
    {
        if (_persistenceUnavailable)
            return null;

        if (_deviceLogic != null)
            return _deviceLogic;

        try
        {
            _deviceLogic = new DeviceLogic();
            return _deviceLogic;
        }
        catch (InvalidOperationException exception)
        {
            _persistenceUnavailable = true;
            if (!_persistenceWarningLogged)
            {
                _persistenceWarningLogged = true;
                _logger.LogWarning(exception, "Sarah device persistence is disabled because MongoDB is not configured.");
            }

            return null;
        }
    }
}
