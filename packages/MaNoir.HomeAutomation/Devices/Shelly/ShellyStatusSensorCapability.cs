using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellyStatusSensorCapability : ISensorDevice
{
    private readonly IReadOnlyDictionary<string, ShellyStatusReadingDefinition> _definitions;
    private readonly Dictionary<string, RuntimeSensorReading> _readings = [];

    public ShellyStatusSensorCapability(IReadOnlyDictionary<string, ShellyStatusReadingDefinition> definitions)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    }

    public IReadOnlyDictionary<string, RuntimeSensorReading> Readings => _readings;

    public void ApplyStatus(JsonElement status)
    {
        if (status.ValueKind != JsonValueKind.Object)
            return;

        foreach ((string type, ShellyStatusReadingDefinition definition) in _definitions)
        {
            if (!status.TryGetProperty(definition.PropertyName, out JsonElement value))
                continue;

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _readings[type] = new RuntimeSensorReading(definition.Definition, value.GetBoolean());
            }
            else if (value.TryGetDecimal(out decimal decimalValue))
            {
                _readings[type] = new RuntimeSensorReading(definition.Definition, decimalValue);
            }
        }

        if (_definitions.ContainsKey("occupancy")
            && !_readings.ContainsKey("occupancy")
            && status.TryGetProperty("live_track", out JsonElement liveTrack)
            && liveTrack.ValueKind == JsonValueKind.Object)
        {
            ShellyStatusReadingDefinition definition = _definitions["occupancy"];
            _readings["occupancy"] = new RuntimeSensorReading(definition.Definition, true);
        }
    }
}

internal sealed record ShellyStatusReadingDefinition(string PropertyName, RuntimeSensorDefinition Definition);
