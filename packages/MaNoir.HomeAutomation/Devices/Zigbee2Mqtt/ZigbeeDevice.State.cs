using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;

public sealed partial class ZigbeeDevice
{
    /// <summary>
    /// Applies a Zigbee2MQTT state payload to the device capabilities.
    /// </summary>
    /// <param name="state">The raw state payload received from Zigbee2MQTT.</param>
    public void ApplyState(JsonElement state)
    {
        _availabilityCapability.ApplyState();
        _switchCapability?.ApplyState(state);
        _intensityCapability?.ApplyState(state);
        _colorCapability?.ApplyState(state);
        _temperatureCapability?.ApplyState(state);
        _sensorCapability?.ApplyState(state);
    }

    /// <summary>
    /// Applies an availability update received from Zigbee2MQTT.
    /// </summary>
    /// <param name="availability">The reported availability value, such as online or offline.</param>
    public void ApplyAvailability(string availability)
    {
        _availabilityCapability.ApplyAvailability(availability);
    }

    /// <summary>
    /// Builds legacy state-change values from the current runtime capability state.
    /// </summary>
    /// <returns>The state changes currently available for publication.</returns>
    public List<DeviceStateChangedMessage.DeviceStateValue> GetStateChanges()
    {
        List<DeviceStateChangedMessage.DeviceStateValue> changes = [];
        if (_switchCapability?.IsOn is bool isOn)
        {
            changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
            {
                Name = "Switch",
                Value = isOn ? "on" : "off",
                Category = DeviceDataCategory.DeviceState,
                StandardDataType = DeviceData.DataTypeSwitch,
                IsMainData = true
            });
        }

        if (_intensityCapability?.IntensityPercent is decimal intensity)
        {
            changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
            {
                Name = "Brightness",
                Value = intensity.ToString("0.############################", CultureInfo.InvariantCulture),
                Category = DeviceDataCategory.DeviceState,
                StandardDataType = DeviceData.DataTypeGradient,
                ValueUnit = "%",
                IsMainData = true
            });
        }

        if (_colorCapability?.CurrentColor is DeviceColor color)
        {
            changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
            {
                Name = "Color",
                Value = FormatColor(color),
                Category = DeviceDataCategory.DeviceState,
                StandardDataType = DeviceData.DataTypeColor,
                IsMainData = true
            });
        }

        if (_temperatureCapability?.CurrentKelvin is int kelvin)
        {
            changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
            {
                Name = "Color temperature",
                Value = kelvin.ToString(CultureInfo.InvariantCulture),
                Category = DeviceDataCategory.DeviceState,
                StandardDataType = DeviceData.DataTypeColor
            });
        }

        if (_sensorCapability != null)
        {
            foreach (RuntimeSensorReading reading in _sensorCapability.Readings.Values)
            {
                string dataType = GetLegacyDataType(reading.Definition.Type);
                string value = FormatLegacyValue(reading.Definition, reading.Value, out string unit);
                if (dataType == null || value == null)
                    continue;

                changes.Add(new DeviceStateChangedMessage.DeviceStateValue()
                {
                    Name = reading.Definition.Label,
                    Value = value,
                    Category = GetLegacyCategory(reading.Definition.Type),
                    StandardDataType = dataType,
                    ValueUnit = unit,
                    IsMainData = IsMainSensorData(reading.Definition.Type)
                });
            }
        }

        return changes;
    }

    private static string FormatColor(DeviceColor color)
    {
        return color switch
        {
            DeviceColor.Xy xy => JsonSerializer.Serialize(new { x = xy.X, y = xy.Y }),
            DeviceColor.Hsv hsv => JsonSerializer.Serialize(new { hue = hsv.HueDegrees, saturation = hsv.Saturation * 100D }),
            _ => null
        };
    }

    private static string GetLegacyDataType(string type)
    {
        return type switch
        {
            "occupancy" => DeviceData.DataTypeOccupancy,
            "contact" => DeviceData.DataTypeContact,
            "water_leak" => DeviceData.DataTypeWaterLeak,
            "smoke" => DeviceData.DataTypeSmoke,
            "carbon_monoxide" => DeviceData.DataTypeCarbonMonoxide,
            "tamper" => DeviceData.DataTypeTamper,
            "vibration" => DeviceData.DataTypeVibration,
            "battery" => DeviceData.DataTypeBatteryPercentage,
            "battery_low" => DeviceData.DataTypeBatteryLow,
            "linkquality" => DeviceData.DataTypeLinkSignalStrength,
            "temperature" => DeviceData.DataTypeSensorTemperature,
            "humidity" => DeviceData.DataTypeSensorHumidity,
            "pressure" => DeviceData.DataTypeSensorPressure,
            "illuminance" => DeviceData.DataTypeSensorIlluminance,
            "co2" => DeviceData.DataTypeSensorCo2,
            "voc" => DeviceData.DataTypeSensorVoc,
            "pm25" => DeviceData.DataTypeSensorPm25,
            "pm10" => DeviceData.DataTypeSensorPm10,
            "soil_moisture" => DeviceData.DataTypeSensorSoilMoisture,
            "noise" => DeviceData.DataTypeSensorNoise,
            "formaldehyde" => DeviceData.DataTypeSensorFormaldehyde,
            "power" => DeviceData.DataTypePowerCurrentConsumption,
            "energy" => DeviceData.DataTypePowerTotal,
            _ => null
        };
    }

    private static DeviceDataCategory GetLegacyCategory(string type)
    {
        return type is "occupancy" or "contact" or "water_leak" or "smoke" or "carbon_monoxide" or "tamper" or "vibration"
            ? DeviceDataCategory.DeviceState
            : type is "battery" or "battery_low" or "linkquality"
                ? DeviceDataCategory.DeviceHealth
                : DeviceDataCategory.SensorReading;
    }

    private static bool IsMainSensorData(string type)
    {
        return type is "occupancy" or "contact" or "water_leak" or "smoke" or "carbon_monoxide" or "tamper" or "vibration"
            or "battery" or "battery_low" or "linkquality";
    }

    private static string FormatLegacyValue(RuntimeSensorDefinition definition, object value, out string unit)
    {
        unit = null;
        if (value is decimal numericValue)
        {
            if (definition.Type == "energy")
            {
                numericValue /= 3_600_000M;
                unit = "kWh";
            }
            else
            {
                unit = definition.CanonicalUnit;
            }

            return numericValue.ToString("0.############################", CultureInfo.InvariantCulture);
        }

        return value switch
        {
            bool booleanValue => booleanValue ? "true" : "false",
            string stringValue when !string.IsNullOrWhiteSpace(stringValue) => stringValue,
            _ => null
        };
    }

    /// <summary>
    /// Converts an action-bearing Zigbee2MQTT payload into a runtime action.
    /// </summary>
    /// <param name="state">The raw state payload containing an action property.</param>
    /// <param name="action">The normalized runtime action when one is present.</param>
    /// <returns><see langword="true"/> when the payload contained an action.</returns>
    public bool TryApplyAction(JsonElement state, out RuntimeDeviceAction action)
    {
        action = null;
        return _actionCapability?.TryApplyAction(state, out action) == true;
    }
}
