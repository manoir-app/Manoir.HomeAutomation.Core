using Home.Common.Messages;
using Home.Common.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Awtrix;

public sealed class AwtrixDevice : IDevice, IRuntimeDeviceEvents
{
    private readonly RuntimeDevice _runtimeDevice;
    private readonly AwtrixDisplayCapability _display;
    private readonly AwtrixNotificationCapability _notification;

    private AwtrixDevice(
        string id,
        string host,
        RuntimeDevice runtimeDevice,
        AwtrixDisplayCapability display,
        AwtrixNotificationCapability notification)
    {
        Id = id;
        Host = host;
        _runtimeDevice = runtimeDevice;
        _display = display;
        _notification = notification;
    }

    public string Id { get; }

    public string Host { get; }

    public string InternalId => string.Concat("awtrix:", Id.Trim().ToLowerInvariant());

    public IReadOnlyList<IDeviceCapability> Capabilities => _runtimeDevice.Capabilities;

    public IReadOnlyList<IDeviceElement> Elements => _runtimeDevice.Elements;

    public event EventHandler<RuntimeDeviceStateChangedEventArgs> StateChanged;

    public static AwtrixDevice Create(
        string id,
        string host,
        Func<string, DeviceColor, string, CancellationToken, Task> setDisplayTextAsync,
        Func<CancellationToken, Task> clearDisplayAsync,
        Func<RuntimeDeviceNotification, CancellationToken, Task> sendNotificationAsync)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An AWTRIX device identifier is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("An AWTRIX device host is required.", nameof(host));

        _ = setDisplayTextAsync ?? throw new ArgumentNullException(nameof(setDisplayTextAsync));
        _ = clearDisplayAsync ?? throw new ArgumentNullException(nameof(clearDisplayAsync));
        _ = sendNotificationAsync ?? throw new ArgumentNullException(nameof(sendNotificationAsync));

        AwtrixDisplayCapability display = new(setDisplayTextAsync, clearDisplayAsync);
        AwtrixNotificationCapability notification = new(sendNotificationAsync);
        RuntimeDevice runtimeDevice = new(
            id,
            [new DeviceElement("Display", [display, notification])],
            [display, notification]);
        return new AwtrixDevice(id, host, runtimeDevice, display, notification);
    }

    public void ApplyMqttState(JsonElement state)
    {
        List<DeviceStateChangedMessage.DeviceStateValue> changes = [];
        AddNumberState(state, changes, "battery", "Battery", DeviceData.DataTypeBatteryPercentage, "%");
        AddNumberState(state, changes, "wifi_signal", "WiFi signal", DeviceData.DataTypeLinkSignalStrength, "dBm");
        if (state.TryGetProperty("power", out JsonElement power)
            && power.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            changes.Add(new DeviceStateChangedMessage.DeviceStateValue
            {
                Name = "Power",
                Value = power.GetBoolean() ? "on" : "off",
                Category = DeviceDataCategory.DeviceState,
                StandardDataType = DeviceData.DataTypeSwitch,
                IsMainData = true
            });
        }

        if (changes.Count > 0)
        {
            StateChanged?.Invoke(this, new RuntimeDeviceStateChangedEventArgs(
                this,
                "awtrix",
                Device.HomeAutomationMainRoleSensors,
                "online",
                changes));
        }
    }

    private static void AddNumberState(
        JsonElement state,
        List<DeviceStateChangedMessage.DeviceStateValue> changes,
        string propertyName,
        string name,
        string dataType,
        string unit)
    {
        if (!state.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDecimal(out decimal number))
            return;

        changes.Add(new DeviceStateChangedMessage.DeviceStateValue
        {
            Name = name,
            Value = number.ToString("0.############################", CultureInfo.InvariantCulture),
            Category = DeviceDataCategory.DeviceState,
            StandardDataType = dataType,
            ValueUnit = unit
        });
    }

    private sealed class AwtrixDisplayCapability : IDisplayDevice
    {
        private readonly Func<string, DeviceColor, string, CancellationToken, Task> _setDisplayTextAsync;
        private readonly Func<CancellationToken, Task> _clearDisplayAsync;

        public AwtrixDisplayCapability(
            Func<string, DeviceColor, string, CancellationToken, Task> setDisplayTextAsync,
            Func<CancellationToken, Task> clearDisplayAsync)
        {
            _setDisplayTextAsync = setDisplayTextAsync;
            _clearDisplayAsync = clearDisplayAsync;
        }

        public Task SetDisplayTextAsync(string text, DeviceColor color = null, string icon = null, CancellationToken cancellationToken = default)
        {
            return _setDisplayTextAsync(text, color, icon, cancellationToken);
        }

        public Task ClearDisplayAsync(CancellationToken cancellationToken = default)
        {
            return _clearDisplayAsync(cancellationToken);
        }
    }

    private sealed class AwtrixNotificationCapability : INotificationDevice
    {
        private readonly Func<RuntimeDeviceNotification, CancellationToken, Task> _sendNotificationAsync;

        public AwtrixNotificationCapability(Func<RuntimeDeviceNotification, CancellationToken, Task> sendNotificationAsync)
        {
            _sendNotificationAsync = sendNotificationAsync;
        }

        public Task SendNotificationAsync(RuntimeDeviceNotification notification, CancellationToken cancellationToken = default)
        {
            return _sendNotificationAsync(notification ?? throw new ArgumentNullException(nameof(notification)), cancellationToken);
        }
    }
}
