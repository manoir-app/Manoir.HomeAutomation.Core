using Home.Common.Model;
using System;

namespace MaNoir.Agents.Sarah;

internal static class MeasurementUnitNormalizer
{
    public static (decimal Value, string Unit) ToCanonical(decimal value, string dataType, string sourceUnit)
    {
        string normalizedUnit = NormalizeUnit(sourceUnit);
        if (string.Equals(dataType, DeviceData.DataTypePowerCurrentConsumption, StringComparison.Ordinal))
        {
            return normalizedUnit switch
            {
                "mw" => (value / 1_000M, "W"),
                "kw" => (value * 1_000M, "W"),
                _ => (value, "W")
            };
        }

        if (string.Equals(dataType, DeviceData.DataTypePowerTotal, StringComparison.Ordinal))
        {
            return normalizedUnit switch
            {
                "w-min" or "wmin" => (value / 60_000M, "kWh"),
                "wh" => (value / 1_000M, "kWh"),
                "mwh" => (value / 1_000_000M, "kWh"),
                "j" => (value / 3_600_000M, "kWh"),
                _ => (value, "kWh")
            };
        }

        return (value, sourceUnit);
    }

    private static string NormalizeUnit(string unit)
    {
        return string.IsNullOrWhiteSpace(unit)
            ? string.Empty
            : unit.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}