using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Protocols.Shelly;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Shelly;

public sealed class ShellyGen1Device : IDevice
{
    private readonly RuntimeDevice _runtimeDevice;
    private readonly Dictionary<int, ShellyGen1SwitchCapability> _switches;
    private readonly Dictionary<int, ShellyGen1CoverCapability> _covers;
    private readonly Dictionary<int, ShellyGen1LightCapability> _lights;
    private readonly Dictionary<int, ShellyGen1RgbCapability> _rgbs;
    private readonly Dictionary<int, ShellyGen1MeterCapability> _meters;
    private readonly Dictionary<int, ShellyGen1InputCapability> _inputs;

    private ShellyGen1Device(
        string id,
        RuntimeDevice runtimeDevice,
        Dictionary<int, ShellyGen1SwitchCapability> switches,
        Dictionary<int, ShellyGen1CoverCapability> covers,
        Dictionary<int, ShellyGen1LightCapability> lights,
        Dictionary<int, ShellyGen1RgbCapability> rgbs,
        Dictionary<int, ShellyGen1MeterCapability> meters,
        Dictionary<int, ShellyGen1InputCapability> inputs)
    {
        Id = id;
        _runtimeDevice = runtimeDevice;
        _switches = switches;
        _covers = covers;
        _lights = lights;
        _rgbs = rgbs;
        _meters = meters;
        _inputs = inputs;
    }

    public string Id { get; }

    public string InternalId => string.Concat("shelly-gen1:", Id.Trim().ToLowerInvariant());

    public IReadOnlyList<IDeviceCapability> Capabilities => _runtimeDevice.Capabilities;

    public IReadOnlyList<IDeviceElement> Elements => _runtimeDevice.Elements;

    public static ShellyGen1Device Create(
        string id,
        string model,
        string mode,
        ShellyGen1Protocol protocol)
    {
        if (protocol == null)
            throw new ArgumentNullException(nameof(protocol));

        return Create(
            id,
            model,
            mode,
            protocol.SetRelayAsync,
            protocol.SetRollerAsync,
            (outputKind, outputIndex, isOn, brightness, cancellationToken) =>
                protocol.SetLightAsync(outputIndex, isOn, brightness, cancellationToken),
            (outputIndex, isOn, brightness, color, cancellationToken) =>
                protocol.SetColorAsync(outputIndex, isOn, brightness, color, cancellationToken),
            protocol.GetRollerPositioningAsync);
    }

    public static ShellyGen1Device Create(
        string id,
        string model,
        string mode,
        Func<int, bool, CancellationToken, Task> setRelayState,
        Func<int, string, CancellationToken, Task> sendRollerCommand,
        Func<string, int, bool?, decimal?, CancellationToken, Task> setLightState,
        Func<int, bool?, decimal?, DeviceColor, CancellationToken, Task> setRgbState,
        Func<int, CancellationToken, Task<bool?>> getRollerPositioning = null,
        bool? supportsPositioning = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A Shelly device identifier is required.", nameof(id));
        if (setRelayState == null)
            throw new ArgumentNullException(nameof(setRelayState));
        if (sendRollerCommand == null)
            throw new ArgumentNullException(nameof(sendRollerCommand));
        if (setLightState == null)
            throw new ArgumentNullException(nameof(setLightState));
        if (setRgbState == null)
            throw new ArgumentNullException(nameof(setRgbState));

        Dictionary<int, ShellyGen1SwitchCapability> switches = [];
        Dictionary<int, ShellyGen1CoverCapability> covers = [];
        Dictionary<int, ShellyGen1LightCapability> lights = [];
        Dictionary<int, ShellyGen1RgbCapability> rgbs = [];
        Dictionary<int, ShellyGen1MeterCapability> meters = [];
        Dictionary<int, ShellyGen1InputCapability> inputs = [];
        List<DeviceElement> elements = [];
        if (SupportsInputActions(model))
        {
            for (int index = 0; index < GetInputCount(model); index++)
            {
                ShellyGen1InputCapability capability = new();
                inputs[index] = capability;
                elements.Add(new DeviceElement(string.Concat("Input ", index.ToString(CultureInfo.InvariantCulture)), [capability]));
            }
        }
        if (IsRollerModel(model, mode))
        {
            Func<string, CancellationToken, Task> sendCommand =
                (command, cancellationToken) => sendRollerCommand(0, command, cancellationToken);
            ShellyGen1CoverCapability capability = supportsPositioning == true
                ? new ShellyGen1PositionableCoverCapability(
                    sendCommand,
                    getRollerPositioning == null
                        ? null
                        : cancellationToken => getRollerPositioning(0, cancellationToken))
                : new ShellyGen1CoverCapability(sendCommand);
            ShellyGen1MeterCapability meter = new();
            covers[0] = capability;
            meters[0] = meter;
            elements.Add(new DeviceElement("Cover 0", [capability, meter]));
        }
        else if (IsRelayModel(model))
        {
            int relayCount = GetRelayCount(model);
            for (int index = 0; index < relayCount; index++)
            {
                int relayIndex = index;
                ShellyGen1SwitchCapability capability = new((isOn, cancellationToken) => setRelayState(relayIndex, isOn, cancellationToken));
                ShellyGen1MeterCapability meter = new();
                switches[index] = capability;
                meters[index] = meter;
                elements.Add(new DeviceElement(GetRelayName(index), [capability, meter]));
            }
        }
        if (IsColorModel(model))
        {
            ShellyGen1RgbCapability capability = new((isOn, brightness, color, cancellationToken) => setRgbState(0, isOn, brightness, color, cancellationToken));
            ShellyGen1MeterCapability meter = new();
            rgbs[0] = capability;
            meters[0] = meter;
            elements.Add(new DeviceElement("Color 0", [capability, meter]));
        }
        else if (IsDimmableModel(model))
        {
            ShellyGen1LightCapability capability = new((isOn, brightness, cancellationToken) => setLightState("light", 0, isOn, brightness, cancellationToken));
            ShellyGen1MeterCapability meter = new();
            lights[0] = capability;
            meters[0] = meter;
            elements.Add(new DeviceElement("Light 0", [capability, meter]));
        }

        return new ShellyGen1Device(id, new RuntimeDevice(id, elements), switches, covers, lights, rgbs, meters, inputs);
    }

    public void ApplyStatus(string componentName, string payload)
    {
        if (TryParseComponent(componentName, "emeter", out int meterIndex)
            && _meters.TryGetValue(meterIndex, out ShellyGen1MeterCapability meterCapability))
        {
            meterCapability.ApplyStatus(GetProperty(componentName), payload);
            return;
        }

        if ((TryParseComponent(componentName, "relay", out meterIndex)
                || TryParseComponent(componentName, "roller", out meterIndex)
                || TryParseComponent(componentName, "light", out meterIndex)
                || TryParseComponent(componentName, "color", out meterIndex))
            && _meters.TryGetValue(meterIndex, out meterCapability)
            && IsMeterProperty(GetProperty(componentName)))
        {
            meterCapability.ApplyStatus(GetProperty(componentName), payload);
        }

        if (!TryParseRelay(componentName, out int relayIndex)
            || !_switches.TryGetValue(relayIndex, out ShellyGen1SwitchCapability capability))
        {
            if (TryParseComponent(componentName, "roller", out int rollerIndex)
                && _covers.TryGetValue(rollerIndex, out ShellyGen1CoverCapability coverCapability))
                coverCapability.ApplyStatus(GetProperty(componentName), payload);
            else if (TryParseComponent(componentName, "light", out int lightIndex)
                && _lights.TryGetValue(lightIndex, out ShellyGen1LightCapability lightCapability))
                lightCapability.ApplyStatus(GetProperty(componentName), payload);
            else if (TryParseComponent(componentName, "color", out int rgbIndex)
                && _rgbs.TryGetValue(rgbIndex, out ShellyGen1RgbCapability rgbCapability))
                rgbCapability.ApplyStatus(GetProperty(componentName), payload);
            return;
        }

        capability.ApplyStatus(payload);
    }

    public void ApplyInputEvent(int inputIndex, string rawAction, IReadOnlyDictionary<string, string> attributes)
    {
        if (_inputs.TryGetValue(inputIndex, out ShellyGen1InputCapability input))
            input.ApplyAction(rawAction, attributes);
    }

    private static string GetProperty(string componentName)
    {
        string[] parts = componentName?.Split(':', 3);
        return parts?.Length == 3 ? parts[2] : string.Empty;
    }

    private static bool IsMeterProperty(string property)
    {
        return string.Equals(property, "power", StringComparison.OrdinalIgnoreCase)
            || string.Equals(property, "energy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseComponent(string componentName, string expectedType, out int index)
    {
        index = 0;
        string[] parts = componentName?.Split(':', 3);
        return parts?.Length >= 2
            && string.Equals(parts[0], expectedType, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && index >= 0;
    }

    private static bool TryParseRelay(string componentName, out int index)
    {
        index = 0;
        string[] parts = componentName?.Split(':');
        return parts?.Length == 2
            && string.Equals(parts[0], "relay", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && index >= 0;
    }

    private static string GetRelayName(int index)
    {
        return index == 0 ? "Switch" : string.Concat("Relay ", index.ToString(CultureInfo.InvariantCulture));
    }

    private static int GetRelayCount(string model)
    {
        if (model.StartsWith("SHSW-4", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (model.StartsWith("SHSW-2", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    private static bool IsRollerModel(string model, string mode)
    {
        return string.Equals(mode, "roller", StringComparison.OrdinalIgnoreCase)
            && (model.StartsWith("SHSW-21", StringComparison.OrdinalIgnoreCase)
                || model.StartsWith("SHSW-25", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRelayModel(string model)
    {
        return model.StartsWith("SHSW", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHPLG", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHEM", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHUNI", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDimmableModel(string model)
    {
        return model.StartsWith("SHDM", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHBLB", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHBDUO", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHVIN", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHCB", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHRGBW", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsInputActions(string model)
    {
        return model.StartsWith("SHBTN", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHIX", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHSW", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHUNI", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHRGBW", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHDM", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetInputCount(string model)
    {
        if (model.StartsWith("SHSW-4", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (model.StartsWith("SHSW-2", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHUNI", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    private static bool IsColorModel(string model)
    {
        return model.StartsWith("SHBLB", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHCB", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("SHRGBW", StringComparison.OrdinalIgnoreCase);
    }
}
