using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellyGen1MeterCapability : ISensorDevice
{
    private readonly Dictionary<string, RuntimeSensorReading> _readings = [];

    public IReadOnlyDictionary<string, RuntimeSensorReading> Readings => _readings;

    public void ApplyStatus(string property, string payload)
    {
        if (!decimal.TryParse(payload, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
            return;

        if (string.Equals(property, "power", StringComparison.OrdinalIgnoreCase))
            SetReading("power", "Power", "W", value);
        else if (string.Equals(property, "energy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(property, "total", StringComparison.OrdinalIgnoreCase))
            SetReading("energy", "Energy", "Wh", value);
    }

    private void SetReading(string type, string label, string unit, decimal value)
    {
        RuntimeSensorDefinition definition = new RuntimeSensorDefinition(type, label, unit);
        _readings[type] = new RuntimeSensorReading(definition, value);
    }
}
