using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaNoir.HomeAutomation.Devices.Hue;

public sealed class HueLight
{
    public string Name { get; set; }
    public HueLightState State { get; set; }
    public HueLightCapabilities Capabilities { get; set; }
}

public sealed class HueLightState
{
    [JsonPropertyName("on")]
    public bool On { get; set; }

    [JsonPropertyName("bri")]
    public int? Brightness { get; set; }

    [JsonPropertyName("xy")]
    public HueColor Color { get; set; }

    [JsonPropertyName("reachable")]
    public bool Reachable { get; set; } = true;

    [JsonPropertyName("hue")]
    public int? Hue { get; set; }

    [JsonPropertyName("sat")]
    public int? Saturation { get; set; }

    [JsonPropertyName("ct")]
    public int? ColorTemperature { get; set; }

    [JsonPropertyName("effect")]
    public string Effect { get; set; }
}

public sealed class HueLightCapabilities
{
    [JsonPropertyName("control")]
    public HueLightControl Control { get; set; }
}

public sealed class HueLightControl
{
    [JsonPropertyName("colorgamut")]
    public double[][] ColorGamut { get; set; }

    [JsonPropertyName("ct")]
    public HueColorTemperatureRange ColorTemperature { get; set; }
}

public sealed class HueColorTemperatureRange
{
    [JsonPropertyName("min")]
    public int Minimum { get; set; }

    [JsonPropertyName("max")]
    public int Maximum { get; set; }
}

[JsonConverter(typeof(HueColorJsonConverter))]
public sealed class HueColor
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class HueColorJsonConverter : JsonConverter<HueColor>
{
    public override HueColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Hue XY color must be an array.");

        reader.Read();
        double x = reader.GetDouble();
        reader.Read();
        double y = reader.GetDouble();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("Hue XY color must contain exactly two values.");

        return new HueColor() { X = x, Y = y };
    }

    public override void Write(Utf8JsonWriter writer, HueColor value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }
}
