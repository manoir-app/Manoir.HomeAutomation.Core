using MaNoir.HomeAutomation.Devices;
using System.Collections.Generic;
using System.Text.Json;

namespace MaNoir.HomeAutomation.Devices.Shelly;

internal sealed class ShellyElectricalCapability : ISensorDevice
{
    private readonly Dictionary<string, RuntimeSensorReading> _readings = [];

    public IReadOnlyDictionary<string, RuntimeSensorReading> Readings => _readings;

    public void ApplyStatus(string componentType, JsonElement status)
    {
        if (status.ValueKind != JsonValueKind.Object)
            return;

        if (string.Equals(componentType, "em", System.StringComparison.OrdinalIgnoreCase))
        {
            AddDecimalReading(status, "total_act_power", "power", "Power", "W");
            AddDecimalReading(status, "a_act_power", "phase_a_power", "Phase A Power", "W");
            AddDecimalReading(status, "b_act_power", "phase_b_power", "Phase B Power", "W");
            AddDecimalReading(status, "c_act_power", "phase_c_power", "Phase C Power", "W");
        }
        else if (string.Equals(componentType, "switch", System.StringComparison.OrdinalIgnoreCase))
        {
            AddDecimalReading(status, "apower", "power", "Power", "W");
            AddDecimalReading(status, "voltage", "voltage", "Voltage", "V");
            AddDecimalReading(status, "current", "current", "Current", "A");
            AddDecimalReading(status, "pf", "power_factor", "Power factor", "");
            AddEnergyReading(status, "aenergy", "energy", "Energy", "Wh");
            AddEnergyReading(status, "ret_aenergy", "returned_energy", "Returned energy", "Wh");
        }
        else if (string.Equals(componentType, "em1", System.StringComparison.OrdinalIgnoreCase))
        {
            AddDecimalReading(status, "act_power", "power", "Power", "W");
        }
        else if (string.Equals(componentType, "em1data", System.StringComparison.OrdinalIgnoreCase))
        {
            AddDecimalReading(status, "total_act_energy", "energy", "Energy", "Wh");
        }
    }

    private void AddDecimalReading(JsonElement status, string propertyName, string type, string label, string unit)
    {
        if (status.TryGetProperty(propertyName, out JsonElement value)
            && value.TryGetDecimal(out decimal decimalValue))
        {
            RuntimeSensorDefinition definition = new RuntimeSensorDefinition(type, label, unit);
            _readings[type] = new RuntimeSensorReading(definition, decimalValue);
        }
    }

    private void AddEnergyReading(JsonElement status, string propertyName, string type, string label, string unit)
    {
        if (status.TryGetProperty(propertyName, out JsonElement energy)
            && energy.ValueKind == JsonValueKind.Object
            && energy.TryGetProperty("total", out JsonElement total)
            && total.TryGetDecimal(out decimal decimalValue))
        {
            RuntimeSensorDefinition definition = new RuntimeSensorDefinition(type, label, unit);
            _readings[type] = new RuntimeSensorReading(definition, decimalValue);
        }
    }
}
