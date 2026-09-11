using Home.Common.Model;
using Home.Common.Messages;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Protocols.Zigbee2Mqtt;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;

/// <summary>
/// Metadata reported by Zigbee2MQTT during device discovery.
/// </summary>
public sealed record ZigbeeDeviceMetadata(
    string FriendlyName,
    string IeeeAddress,
    string Manufacturer,
    string ModelId,
    string Model,
    string Vendor,
    string NetworkType,
    string PowerSource,
    int? NetworkAddress,
    string SoftwareBuildId,
    bool? Supported,
    bool? InterviewCompleted);

/// <summary>
/// Runtime representation of a Zigbee2MQTT device and its exposed capabilities.
/// </summary>
public sealed partial class ZigbeeDevice : IDevice, IRuntimeDeviceEvents
{
    private readonly RuntimeDevice _runtimeDevice;
    private readonly ZigbeeSwitchCapability _switchCapability;
    private readonly ZigbeeIntensityCapability _intensityCapability;
    private readonly ZigbeeColorCapability _colorCapability;
    private readonly ZigbeeColorTemperatureCapability _temperatureCapability;
    private readonly ZigbeeSensorCapability _sensorCapability;
    private readonly ZigbeeActionCapability _actionCapability;
    private readonly ZigbeeAvailabilityCapability _availabilityCapability;

    private ZigbeeDevice(
        string id,
        ZigbeeDeviceMetadata metadata,
        RuntimeDevice runtimeDevice,
        ZigbeeSwitchCapability switchCapability,
        ZigbeeIntensityCapability intensityCapability,
        ZigbeeColorCapability colorCapability,
        ZigbeeColorTemperatureCapability temperatureCapability,
        ZigbeeSensorCapability sensorCapability,
        ZigbeeActionCapability actionCapability,
        ZigbeeAvailabilityCapability availabilityCapability)
    {
        Id = id;
        Metadata = metadata;
        _runtimeDevice = runtimeDevice;
        _switchCapability = switchCapability;
        _intensityCapability = intensityCapability;
        _colorCapability = colorCapability;
        _temperatureCapability = temperatureCapability;
        _sensorCapability = sensorCapability;
        _actionCapability = actionCapability;
        _availabilityCapability = availabilityCapability;
    }

    /// <summary>
    /// Gets the Zigbee2MQTT friendly name used to address the device.
    /// </summary>
    public string Id { get; }

    public event EventHandler<RuntimeDeviceStateChangedEventArgs> StateChanged;

    /// <summary>
    /// Gets the discovery metadata associated with the device.
    /// </summary>
    public ZigbeeDeviceMetadata Metadata { get; }

    /// <summary>
    /// Gets the stable runtime identity derived from the IEEE address when available.
    /// </summary>
    public string InternalId => !string.IsNullOrWhiteSpace(Metadata.IeeeAddress)
        ? string.Concat("zigbee:ieee:", Metadata.IeeeAddress.Trim().ToLowerInvariant())
        : string.Concat("zigbee:name:", Id.Trim().ToLowerInvariant());

    /// <summary>
    /// Gets the stable identity used by runtime consumers.
    /// </summary>
    public string StableIdentity => InternalId;

    /// <summary>
    /// Gets all capabilities exposed by the device.
    /// </summary>
    public IReadOnlyList<IDeviceCapability> Capabilities => _runtimeDevice.Capabilities;

    /// <summary>
    /// Gets the runtime elements composing the device.
    /// </summary>
    public IReadOnlyList<IDeviceElement> Elements => _runtimeDevice.Elements;

    /// <summary>
    /// Gets the normalized home-automation roles inferred from the exposed capabilities.
    /// </summary>
    public IReadOnlyList<string> DeviceRoles
    {
        get
        {
            List<string> roles = [];
            if (_switchCapability != null)
                roles.Add(Device.HomeAutomationRoleSwitch);
            if (_intensityCapability != null)
                roles.Add(Device.HomeAutomationRoleDimmer);
            if (_actionCapability != null)
                roles.Add(Device.HomeAutomationRoleActionnable);
            if (_colorCapability != null || _temperatureCapability != null)
                roles.Add(Device.HomeAutomationRoleColorBound);
            if (roles.Count == 0)
                roles.Add(Device.HomeAutomationMainRoleSensors);
            return roles;
        }
    }

    /// <summary>
    /// Gets the normalized capability identifiers exposed by the device.
    /// </summary>
    public IReadOnlyList<string> DeviceCapabilities
    {
        get
        {
            List<string> capabilities = [];
            if (_colorCapability?.SupportedColorModels.Contains(ColorModel.Xy) == true)
                capabilities.Add(Device.CapabilityColorXy);
            if (_colorCapability?.SupportedColorModels.Contains(ColorModel.Hsv) == true)
                capabilities.Add(Device.CapabilityColorHs);
            if (_temperatureCapability != null)
                capabilities.Add(Device.CapabilityColorTemperature);
            return capabilities;
        }
    }

    /// <summary>
    /// Gets the actions advertised by the Zigbee2MQTT exposes definition.
    /// </summary>
    public IReadOnlyList<DeviceAvailableAction> AvailableActions => _actionCapability?.AvailableActions
        .Select(action => new DeviceAvailableAction()
        {
            ActionKind = action.Kind,
            Action = action.Action,
            RawAction = action.RawAction,
            Attributes = new Dictionary<string, string>(action.Attributes)
        })
        .ToArray() ?? [];

    /// <summary>
    /// Creates a runtime device from a Zigbee2MQTT discovery payload.
    /// </summary>
    /// <param name="id">The Zigbee2MQTT friendly name.</param>
    /// <param name="discovery">The raw Zigbee2MQTT discovery document.</param>
    /// <param name="protocol">Protocol used to publish commands to the device.</param>
    /// <returns>A runtime device whose capabilities match the discovery document.</returns>
    public static ZigbeeDevice Create(
        string id,
        JsonElement discovery,
        Zigbee2MqttProtocol protocol)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A Zigbee device identifier is required.", nameof(id));
        if (protocol == null)
            throw new ArgumentNullException(nameof(protocol));

        ZigbeeDeviceMetadata metadata = ReadMetadata(id, discovery);
        HashSet<string> properties = GetExposedProperties(discovery);
        List<IDeviceCapability> capabilities = [];
        ZigbeeSwitchCapability switchCapability = null;
        ZigbeeIntensityCapability intensityCapability = null;
        ZigbeeColorCapability colorCapability = null;
        ZigbeeColorTemperatureCapability temperatureCapability = null;
        ZigbeeSensorCapability sensorCapability = null;
        ZigbeeActionCapability actionCapability = null;
        ZigbeeAvailabilityCapability availabilityCapability = new();
        HashSet<string> sensorProperties = GetSensorProperties(properties);
        if (sensorProperties.Count > 0)
        {
            sensorCapability = new ZigbeeSensorCapability(sensorProperties, GetExposedUnits(discovery));
            capabilities.Add(sensorCapability);
        }
        List<string> actionValues = GetActionValues(discovery);
        if (actionValues.Count > 0)
        {
            actionCapability = new ZigbeeActionCapability(actionValues);
            capabilities.Add(actionCapability);
        }
        if (properties.Contains("state"))
        {
            switchCapability = new ZigbeeSwitchCapability(id, protocol);
            capabilities.Add(switchCapability);
        }
        if (properties.Contains("brightness"))
        {
            intensityCapability = new ZigbeeIntensityCapability(id, protocol);
            capabilities.Add(intensityCapability);
        }
        if (HasColorProperties(properties))
        {
            colorCapability = new ZigbeeColorCapability(id, protocol, GetColorModels(properties));
            capabilities.Add(colorCapability);
        }
        if (properties.Contains("color_temp"))
        {
            temperatureCapability = new ZigbeeColorTemperatureCapability(id, protocol);
            capabilities.Add(temperatureCapability);
        }
        capabilities.Add(availabilityCapability);

        RuntimeDevice runtimeDevice = new(id, [new DeviceElement("Device", capabilities)]);
        return new ZigbeeDevice(id, metadata, runtimeDevice, switchCapability, intensityCapability, colorCapability, temperatureCapability, sensorCapability, actionCapability, availabilityCapability);
    }

    private abstract class ZigbeeCapability
    {
        protected ZigbeeCapability(
            string deviceId,
            Zigbee2MqttProtocol protocol)
        {
            DeviceId = deviceId;
            Protocol = protocol;
        }

        protected string DeviceId { get; }

        protected Zigbee2MqttProtocol Protocol { get; }
    }

    private sealed class ZigbeeSwitchCapability : ZigbeeCapability, IToggleSwitchDevice
    {
        public ZigbeeSwitchCapability(
            string deviceId,
            Zigbee2MqttProtocol protocol)
            : base(deviceId, protocol)
        {
        }

        public bool? IsOn { get; private set; }

        public void ApplyState(JsonElement state)
        {
            if (state.TryGetProperty("state", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && (string.Equals(value.GetString(), "ON", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value.GetString(), "OFF", StringComparison.OrdinalIgnoreCase)))
            {
                IsOn = string.Equals(value.GetString(), "ON", StringComparison.OrdinalIgnoreCase);
            }
        }

        public async Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
        {
            await Protocol.PublishCommandAsync(DeviceId, new Dictionary<string, object>() { ["state"] = isOn ? "ON" : "OFF" }, cancellationToken);
            IsOn = isOn;
        }
    }

    private sealed class ZigbeeIntensityCapability : ZigbeeCapability, IIntensityGradientDevice
    {
        public ZigbeeIntensityCapability(
            string deviceId,
            Zigbee2MqttProtocol protocol)
            : base(deviceId, protocol)
        {
        }

        public decimal? IntensityPercent { get; private set; }

        public void ApplyState(JsonElement state)
        {
            if (state.TryGetProperty("brightness", out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetDecimal(out decimal brightness))
            {
                IntensityPercent = Math.Clamp(brightness, 0M, 254M) * 100M / 254M;
            }
        }

        public async Task SetIntensityAsync(decimal intensityPercent, CancellationToken cancellationToken = default)
        {
            if (intensityPercent is < 0M or > 100M)
                throw new ArgumentOutOfRangeException(nameof(intensityPercent));

            int brightness = Math.Clamp((int)Math.Round(intensityPercent * 254M / 100M, MidpointRounding.AwayFromZero), 0, 254);
            await Protocol.PublishCommandAsync(DeviceId, new Dictionary<string, object>() { ["brightness"] = brightness }, cancellationToken);
            IntensityPercent = intensityPercent;
        }
    }

    private sealed class ZigbeeColorCapability : ZigbeeCapability, IChromaticColorDevice
    {
        public ZigbeeColorCapability(
            string deviceId,
            Zigbee2MqttProtocol protocol,
            IReadOnlyList<ColorModel> supportedColorModels)
            : base(deviceId, protocol)
        {
            SupportedColorModels = supportedColorModels;
        }

        public IReadOnlyList<ColorModel> SupportedColorModels { get; }

        public DeviceColor CurrentColor { get; private set; }

        public void ApplyState(JsonElement state)
        {
            if (!state.TryGetProperty("color", out JsonElement color) || color.ValueKind != JsonValueKind.Object)
                return;

            if (color.TryGetProperty("x", out JsonElement x)
                && color.TryGetProperty("y", out JsonElement y)
                && x.TryGetDouble(out double xCoordinate)
                && y.TryGetDouble(out double yCoordinate))
            {
                CurrentColor = new DeviceColor.Xy(xCoordinate, yCoordinate);
                return;
            }

            if (color.TryGetProperty("hue", out JsonElement hue)
                && color.TryGetProperty("saturation", out JsonElement saturation)
                && hue.TryGetDouble(out double hueDegrees)
                && saturation.TryGetDouble(out double saturationPercent))
            {
                CurrentColor = new DeviceColor.Hsv(hueDegrees, saturationPercent / 100D, 1D);
            }
        }

        public async Task SetColorAsync(DeviceColor color, CancellationToken cancellationToken = default)
        {
            if (color == null)
                throw new ArgumentNullException(nameof(color));

            if (color is DeviceColor.Rgb)
            {
                color = SupportedColorModels.Contains(ColorModel.Xy)
                    ? color.ToXy()
                    : SupportedColorModels.Contains(ColorModel.Hsv)
                        ? color.ToHsv()
                        : throw new ArgumentException("This Zigbee device does not support a convertible color model.", nameof(color));
            }

            Dictionary<string, object> payload = color switch
            {
                DeviceColor.Xy xy => new Dictionary<string, object>() { ["color"] = new Dictionary<string, object>() { ["x"] = xy.X, ["y"] = xy.Y } },
                DeviceColor.Hsv hsv => new Dictionary<string, object>() { ["color"] = new Dictionary<string, object>() { ["hue"] = hsv.HueDegrees, ["saturation"] = hsv.Saturation * 100D } },
                _ => throw new ArgumentException("This Zigbee device does not support the requested color model.", nameof(color))
            };

            await Protocol.PublishCommandAsync(DeviceId, payload, cancellationToken);
            CurrentColor = color;
        }
    }

    private sealed class ZigbeeColorTemperatureCapability : ZigbeeCapability, IColorTemperatureDevice
    {
        public ZigbeeColorTemperatureCapability(
            string deviceId,
            Zigbee2MqttProtocol protocol)
            : base(deviceId, protocol)
        {
        }

        public int? CurrentKelvin { get; private set; }

        public int MinimumKelvin => 1000;

        public int MaximumKelvin => 10000;

        public void ApplyState(JsonElement state)
        {
            if (state.TryGetProperty("color_temp", out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out double mired)
                && mired > 0D)
            {
                CurrentKelvin = (int)Math.Round(1_000_000D / mired, MidpointRounding.AwayFromZero);
            }
        }

        public async Task SetColorTemperatureAsync(int kelvin, CancellationToken cancellationToken = default)
        {
            if (kelvin < MinimumKelvin || kelvin > MaximumKelvin)
                throw new ArgumentOutOfRangeException(nameof(kelvin));

            int mired = (int)Math.Round(1_000_000D / kelvin, MidpointRounding.AwayFromZero);
            await Protocol.PublishCommandAsync(DeviceId, new Dictionary<string, object>() { ["color_temp"] = mired }, cancellationToken);
            CurrentKelvin = kelvin;
        }
    }

    private sealed class ZigbeeSensorCapability : ISensorDevice
    {
        private readonly IReadOnlyCollection<string> _properties;
        private readonly IReadOnlyDictionary<string, string> _sourceUnits;
        private readonly Dictionary<string, RuntimeSensorReading> _readings = new(StringComparer.OrdinalIgnoreCase);

        public ZigbeeSensorCapability(
            IEnumerable<string> properties,
            IReadOnlyDictionary<string, string> sourceUnits)
        {
            _properties = properties.ToArray();
            _sourceUnits = sourceUnits ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, RuntimeSensorReading> Readings => _readings;

        public void ApplyState(JsonElement state)
        {
            foreach (string property in _properties)
            {
                if (!state.TryGetProperty(property, out JsonElement value)
                    || !TryReadValue(value, out object rawValue))
                    continue;

                if (!TryNormalize(property, rawValue, out object normalizedValue, out string unit))
                    continue;

                _readings[property] = new RuntimeSensorReading(GetDefinition(property), normalizedValue);
            }
        }

        private bool TryNormalize(string property, object value, out object normalizedValue, out string unit)
        {
            normalizedValue = value;
            unit = GetCanonicalUnit(property);
            if (value is not decimal numericValue)
                return true;

            string sourceUnit = _sourceUnits.TryGetValue(property, out string exposedUnit)
                ? exposedUnit
                : GetDefaultSourceUnit(property);
            string normalizedSourceUnit = NormalizeUnit(sourceUnit);
            if (string.IsNullOrWhiteSpace(normalizedSourceUnit) || string.IsNullOrWhiteSpace(unit))
                return true;

            string targetUnit = NormalizeUnit(unit);
            if (string.Equals(normalizedSourceUnit, targetUnit, StringComparison.Ordinal))
                return true;

            if (property.Equals("temperature", StringComparison.OrdinalIgnoreCase))
            {
                if (normalizedSourceUnit == "f")
                    normalizedValue = (numericValue - 32M) * 5M / 9M;
                else if (normalizedSourceUnit == "k")
                    normalizedValue = numericValue - 273.15M;
                else
                    return true;
                return true;
            }

            if (property.Equals("pressure", StringComparison.OrdinalIgnoreCase))
            {
                normalizedValue = normalizedSourceUnit switch
                {
                    "hpa" or "mbar" => numericValue * 100M,
                    "kpa" => numericValue * 1_000M,
                    "bar" => numericValue * 100_000M,
                    _ => numericValue
                };
                return true;
            }

            if (property is "power" or "energy")
            {
                normalizedValue = normalizedSourceUnit switch
                {
                    "mw" => numericValue / 1_000M,
                    "kw" => numericValue * 1_000M,
                    "wh" when property == "energy" => numericValue * 3_600M,
                    "kwh" when property == "energy" => numericValue * 3_600_000M,
                    "mwh" when property == "energy" => numericValue * 3_600_000_000M,
                    _ => numericValue
                };
            }

            if (property is "pm25" or "pm10" or "formaldehyde"
                && normalizedSourceUnit == "mg/m3")
            {
                normalizedValue = numericValue * 1_000M;
            }

            return true;
        }

        private static bool TryReadValue(JsonElement value, out object result)
        {
            result = null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal numericValue))
            {
                result = numericValue;
                return true;
            }
            if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            {
                result = value.GetBoolean();
                return true;
            }
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                result = value.GetString();
                return true;
            }
            return false;
        }

        private static RuntimeSensorDefinition GetDefinition(string property)
        {
            return property switch
            {
                "temperature" => new RuntimeSensorDefinition("temperature", "Temperature", "C"),
                "humidity" => new RuntimeSensorDefinition("humidity", "Humidity", "%"),
                "pressure" => new RuntimeSensorDefinition("pressure", "Pressure", "Pa"),
                "illuminance" or "illuminance_lux" => new RuntimeSensorDefinition("illuminance", "Illuminance", "lx"),
                "co2" => new RuntimeSensorDefinition("co2", "CO2", "ppm"),
                "voc" => new RuntimeSensorDefinition("voc", "VOC", "ppb"),
                "pm25" => new RuntimeSensorDefinition("pm25", "PM2.5", "ug/m3"),
                "pm10" => new RuntimeSensorDefinition("pm10", "PM10", "ug/m3"),
                "formaldehyde" => new RuntimeSensorDefinition("formaldehyde", "Formaldehyde", "ug/m3"),
                "soil_moisture" => new RuntimeSensorDefinition("soil_moisture", "SoilMoisture", "%"),
                "noise" => new RuntimeSensorDefinition("noise", "Noise", "dB"),
                "power" => new RuntimeSensorDefinition("power", "Power", "W"),
                "energy" => new RuntimeSensorDefinition("energy", "Energy", "J"),
                "battery" => new RuntimeSensorDefinition("battery", "Battery", "%"),
                "battery_low" => new RuntimeSensorDefinition("battery_low", "BatteryLow", null),
                "linkquality" => new RuntimeSensorDefinition("linkquality", "LinkQuality", null),
                "occupancy" => new RuntimeSensorDefinition("occupancy", "Occupancy", null),
                "contact" => new RuntimeSensorDefinition("contact", "Contact", null),
                "water_leak" => new RuntimeSensorDefinition("water_leak", "WaterLeak", null),
                "smoke" => new RuntimeSensorDefinition("smoke", "Smoke", null),
                "carbon_monoxide" => new RuntimeSensorDefinition("carbon_monoxide", "CarbonMonoxide", null),
                "tamper" => new RuntimeSensorDefinition("tamper", "Tamper", null),
                "vibration" => new RuntimeSensorDefinition("vibration", "Vibration", null),
                _ => new RuntimeSensorDefinition(property, property, null)
            };
        }

        private static string GetCanonicalUnit(string property)
        {
            return property switch
            {
                "temperature" => "C",
                "humidity" or "battery" or "soil_moisture" => "%",
                "pressure" => "Pa",
                "illuminance" or "illuminance_lux" => "lx",
                "co2" => "ppm",
                "voc" or "pm25" or "pm10" or "formaldehyde" => "ug/m3",
                "noise" => "dB",
                "power" => "W",
                "energy" => "J",
                _ => null
            };
        }

        private static string GetDefaultSourceUnit(string property)
        {
            return property switch
            {
                "temperature" => "C",
                "humidity" or "battery" or "soil_moisture" => "%",
                "pressure" => "hPa",
                "illuminance" or "illuminance_lux" => "lx",
                "co2" => "ppm",
                "power" => "W",
                "energy" => "kWh",
                _ => null
            };
        }

        private static string NormalizeUnit(string unit)
        {
            return string.IsNullOrWhiteSpace(unit)
                ? null
                : unit.Trim().ToLowerInvariant()
                    .Replace("µ", "u", StringComparison.Ordinal)
                    .Replace("μ", "u", StringComparison.Ordinal)
                    .Replace("³", "3", StringComparison.Ordinal)
                    .Replace(" ", string.Empty, StringComparison.Ordinal);
        }
    }

}