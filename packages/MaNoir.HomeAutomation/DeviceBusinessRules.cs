using Home.Common.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MaNoir.HomeAutomation;

public sealed partial class DeviceLogic
{
    public static string NormalizeDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        return deviceId.Trim().ToLowerInvariant();
    }

    public static string NormalizeDeviceInternalName(string deviceInternalName)
    {
        if (string.IsNullOrWhiteSpace(deviceInternalName))
            return null;

        return deviceInternalName.Trim().ToLowerInvariant();
    }

    public static string NormalizeDeviceKind(string deviceKind)
    {
        if (string.IsNullOrWhiteSpace(deviceKind))
            return null;

        return deviceKind.Trim().ToLowerInvariant();
    }

    public static string NormalizeMeshId(string meshId)
    {
        if (string.IsNullOrWhiteSpace(meshId))
            return "local";

        return meshId.Trim().ToLowerInvariant();
    }

    public static string GetEntityKind(Device device)
    {
        return GetEntityKind(device?.DeviceKind);
    }

    public static string GetEntityKind(string deviceKind)
    {
        string normalizedDeviceKind = NormalizeDeviceKind(deviceKind);
        if (normalizedDeviceKind == null)
            return null;

        return string.Concat("manoirapp:device/", normalizedDeviceKind);
    }

    public static Device PrepareForRegistration(Device device)
    {
        if (device == null)
            return null;

        device.Id = NormalizeDeviceId(device.Id);
        device.DeviceInternalName = NormalizeDeviceInternalName(device.DeviceInternalName);
        device.DeviceKind = NormalizeDeviceKind(device.DeviceKind);
        device.MeshId = NormalizeMeshId(device.MeshId);

        device.DeviceRoles ??= [];
        for (int index = 0; index < device.DeviceRoles.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(device.DeviceRoles[index]))
                device.DeviceRoles[index] = device.DeviceRoles[index].Trim().ToLowerInvariant();
        }

            device.DeviceCapabilities ??= [];
            for (int index = 0; index < device.DeviceCapabilities.Count; index++)
            {
                if (!string.IsNullOrWhiteSpace(device.DeviceCapabilities[index]))
                device.DeviceCapabilities[index] = device.DeviceCapabilities[index].Trim().ToLowerInvariant();
            }

        device.DeviceAddresses ??= [];
        device.AvailableActions ??= [];
        device.Datas ??= [];
        device.SecondaryDatas ??= [];
        device.Images ??= [];

        return device;
    }

    public static void ApplyRegistrationUpdate(Device existing, Device incoming)
    {
        if (existing == null || incoming == null)
            return;

        existing.DevicePlatform = incoming.DevicePlatform;
        existing.DeviceAddresses = incoming.DeviceAddresses == null ? [] : [.. incoming.DeviceAddresses];
        existing.SupportPrivacyMode = incoming.SupportPrivacyMode;
        existing.DeviceRoles = incoming.DeviceRoles == null ? [] : [.. incoming.DeviceRoles];
        existing.DeviceCapabilities = incoming.DeviceCapabilities == null ? [] : [.. incoming.DeviceCapabilities];
        existing.AvailableActions = CloneAvailableActions(incoming.AvailableActions);

        if (string.IsNullOrWhiteSpace(existing.MeshId))
            existing.MeshId = incoming.MeshId;

        if (incoming.DeviceGivenName != null
            && (string.IsNullOrWhiteSpace(existing.DeviceGivenName)
                || string.Equals(incoming.DeviceInternalName, incoming.DeviceGivenName, StringComparison.Ordinal)))
        {
            existing.DeviceGivenName = incoming.DeviceGivenName;
        }
    }

    public static bool ApplyDataAndStatus(Device device, string status, IEnumerable<DeviceData> datas)
    {
        if (device == null)
            return false;

        DateTimeOffset now = DateTimeOffset.Now;
        device.Datas = CloneDeviceDatas(datas, now);
        device.MainStatusInfo = status;
        return true;
    }

    public static bool ChangeData(Device device, DeviceData data, string mainStatus = null)
    {
        if (device == null || data == null)
            return false;

        device.Datas ??= [];

        DeviceData existingData = device.Datas.FirstOrDefault(current => string.Equals(current.Name, data.Name, StringComparison.Ordinal));
        if (existingData != null)
        {
            existingData.LastUpdated = DateTimeOffset.Now;
            existingData.Value = data.Value;
            existingData.StandardDataType = data.StandardDataType;
            existingData.IsMainData = data.IsMainData;
            existingData.Category = data.Category;

            if (!string.IsNullOrWhiteSpace(data.ValueUnit))
                existingData.ValueUnit = data.ValueUnit;

            if (!string.IsNullOrWhiteSpace(mainStatus))
                device.MainStatusInfo = mainStatus;

            return true;
        }

        device.Datas.Add(CloneDeviceData(data, data.LastUpdated));
        return ApplyDataAndStatus(device, device.MainStatusInfo, device.Datas);
    }

    public static List<DeviceData> CloneDeviceDatas(IEnumerable<DeviceData> datas, DateTimeOffset lastUpdated)
    {
        List<DeviceData> result = [];
        if (datas == null)
            return result;

        foreach (DeviceData data in datas)
        {
            result.Add(CloneDeviceData(data, lastUpdated));
        }

        return result;
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

    public static DeviceData CloneDeviceData(DeviceData data, DateTimeOffset lastUpdated)
    {
        if (data == null)
            return null;

        return new DeviceData()
        {
            IsMainData = data.IsMainData,
            Category = data.Category,
            Name = data.Name,
            StandardDataType = data.StandardDataType,
            Value = data.Value,
            ValueUnit = data.ValueUnit,
            MinValue = data.MinValue,
            MaxValue = data.MaxValue,
            ValidValues = data.ValidValues == null ? null : (string[])data.ValidValues.Clone(),
            LastUpdated = lastUpdated
        };
    }
}