using MaNoir.HomeAutomation.Devices;
using System.Collections.Generic;
using System.Text.Json;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellySensorCapability : ISensorDevice
{
    private readonly string _propertyName;
    private readonly RuntimeSensorDefinition _definition;
    private readonly Dictionary<string, RuntimeSensorReading> _readings = [];

    public ShellySensorCapability(string propertyName, RuntimeSensorDefinition definition)
    {
        _propertyName = propertyName;
        _definition = definition;
    }

    public IReadOnlyDictionary<string, RuntimeSensorReading> Readings => _readings;

    public void ApplyStatus(JsonElement status)
    {
        if (status.ValueKind == JsonValueKind.Object
            && status.TryGetProperty(_propertyName, out JsonElement value)
            && value.TryGetDecimal(out decimal decimalValue))
        {
            _readings[_definition.Type] = new RuntimeSensorReading(_definition, decimalValue);
        }
    }
}
