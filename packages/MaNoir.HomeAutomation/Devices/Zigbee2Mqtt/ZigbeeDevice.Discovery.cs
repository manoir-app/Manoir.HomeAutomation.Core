using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MaNoir.HomeAutomation.Devices;

namespace MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;

public sealed partial class ZigbeeDevice
{
    /// <summary>
    /// Extracts device metadata from a Zigbee2MQTT discovery payload.
    /// </summary>
    private static ZigbeeDeviceMetadata ReadMetadata(string friendlyName, JsonElement discovery)
    {
        return new ZigbeeDeviceMetadata(
            friendlyName,
            GetString(discovery, "ieee_address"),
            GetString(discovery, "manufacturer"),
            GetString(discovery, "model_id"),
            GetString(discovery, "definition", "model"),
            GetString(discovery, "definition", "vendor"),
            GetString(discovery, "type"),
            GetString(discovery, "power_source"),
            GetInt(discovery, "network_address"),
            GetString(discovery, "software_build_id"),
            GetBool(discovery, "supported"),
            GetBool(discovery, "interview_completed"));
    }

    private static string GetString(JsonElement element, params string[] path)
    {
        foreach (string segment in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                return null;
        }

        return element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString().Trim()
            : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;
    }

    private static List<string> GetActionValues(JsonElement discovery)
    {
        List<string> values = [];
        CollectActionValues(discovery, values);
        return values;
    }

    private static void CollectActionValues(JsonElement element, List<string> values)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                CollectActionValues(item, values);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("property", out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && string.Equals(property.GetString(), "action", StringComparison.OrdinalIgnoreCase)
            && element.TryGetProperty("values", out JsonElement actionValues)
            && actionValues.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement value in actionValues.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    string action = value.GetString().Trim();
                    if (!values.Contains(action, StringComparer.OrdinalIgnoreCase))
                        values.Add(action);
                }
            }
        }

        foreach (string childName in new[] { "definition", "exposes", "features" })
        {
            if (element.TryGetProperty(childName, out JsonElement child))
                CollectActionValues(child, values);
        }
    }

    private static HashSet<string> GetSensorProperties(HashSet<string> properties)
    {
        HashSet<string> sensorProperties = new(StringComparer.OrdinalIgnoreCase);
        foreach (string property in new[]
        {
            "occupancy", "contact", "water_leak", "smoke", "carbon_monoxide", "tamper", "vibration",
            "battery", "battery_low", "linkquality", "temperature", "humidity", "pressure", "illuminance",
            "illuminance_lux", "co2", "voc", "pm25", "pm10", "soil_moisture", "noise", "formaldehyde",
            "power", "energy"
        })
        {
            if (properties.Contains(property))
                sensorProperties.Add(property);
        }

        return sensorProperties;
    }

    private static Dictionary<string, string> GetExposedUnits(JsonElement discovery)
    {
        Dictionary<string, string> units = new(StringComparer.OrdinalIgnoreCase);
        CollectExposedUnits(discovery, units);
        return units;
    }

    private static void CollectExposedUnits(JsonElement element, Dictionary<string, string> units)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                CollectExposedUnits(item, units);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("property", out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && element.TryGetProperty("unit", out JsonElement unit)
            && unit.ValueKind == JsonValueKind.String)
        {
            units[property.GetString()] = unit.GetString();
        }

        foreach (string childName in new[] { "definition", "exposes", "features" })
        {
            if (element.TryGetProperty(childName, out JsonElement child))
                CollectExposedUnits(child, units);
        }
    }

    private static HashSet<string> GetExposedProperties(JsonElement discovery)
    {
        HashSet<string> properties = new(StringComparer.OrdinalIgnoreCase);
        CollectExposedProperties(discovery, properties);
        return properties;
    }

    private static void CollectExposedProperties(JsonElement element, HashSet<string> properties)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                CollectExposedProperties(item, properties);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("property", out JsonElement property)
            && property.ValueKind == JsonValueKind.String)
        {
            string name = property.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                properties.Add(name);
        }

        foreach (string childName in new[] { "definition", "exposes", "features" })
        {
            if (element.TryGetProperty(childName, out JsonElement child))
                CollectExposedProperties(child, properties);
        }
    }

    private static bool HasColorProperties(HashSet<string> properties)
    {
        return properties.Contains("color")
            || properties.Contains("color_xy")
            || properties.Contains("color_hs")
            || (properties.Contains("x") && properties.Contains("y"))
            || (properties.Contains("hue") && properties.Contains("saturation"));
    }

    private static IReadOnlyList<ColorModel> GetColorModels(HashSet<string> properties)
    {
        List<ColorModel> models = [];
        if (properties.Contains("color") || properties.Contains("color_xy") || (properties.Contains("x") && properties.Contains("y")))
            models.Add(ColorModel.Xy);
        if (properties.Contains("color_hs") || (properties.Contains("hue") && properties.Contains("saturation")))
            models.Add(ColorModel.Hsv);
        return models;
    }
}
