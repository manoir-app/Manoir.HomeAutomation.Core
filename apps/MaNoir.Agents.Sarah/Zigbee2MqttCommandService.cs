using Home.Common.Model;
using MaNoir.HomeAutomation;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah;

public sealed class Zigbee2MqttCommandService
{
    private readonly ILogger<Zigbee2MqttCommandService> _logger;

    public Zigbee2MqttCommandService(ILogger<Zigbee2MqttCommandService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(SceneStep step)
    {
        if (step == null || string.IsNullOrWhiteSpace(step.TargetId) || string.IsNullOrWhiteSpace(step.Message))
            return false;

        Device device = await new DeviceLogic().GetByIdAsync(step.TargetId);
        if (device == null
            || !string.Equals(device.DevicePlatform, "zigbee2mqtt", StringComparison.OrdinalIgnoreCase)
            || !device.DeviceRoles.Contains(step.Message, StringComparer.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(device.DeviceInternalName))
        {
            return false;
        }

        Dictionary<string, object> command = CreateCommand(step, device);
        if (command == null)
            return false;

        string host = Environment.GetEnvironmentVariable("MQTT_SERVICE_HOST");
        if (string.IsNullOrWhiteSpace(host))
            host = "localhost";

        string portValue = Environment.GetEnvironmentVariable("MQTT_SERVICE_PORT");
        int port = int.TryParse(portValue, out int configuredPort) ? configuredPort : 1883;
        string topicRoot = Environment.GetEnvironmentVariable("ZIGBEE2MQTT_TOPIC");
        if (string.IsNullOrWhiteSpace(topicRoot))
            topicRoot = "zigbee2mqtt";

        MqttFactory factory = new MqttFactory();
        using IMqttClient client = factory.CreateMqttClient();
        try
        {
            await client.ConnectAsync(new MqttClientOptionsBuilder()
                .WithClientId(string.Concat("manoir-sarah-command-", Guid.NewGuid().ToString("N")))
                .WithTcpServer(host, port)
                .Build());
            await client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(string.Concat(topicRoot.Trim().Trim('/'), "/", device.DeviceInternalName, "/set"))
                .WithPayload(JsonSerializer.Serialize(command))
                .Build());

            _logger.LogInformation("Sent Zigbee2MQTT {Role} command to device {DeviceId}.", step.Message, device.Id);
            return true;
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(new MqttClientDisconnectOptions());
        }
    }

    private static Dictionary<string, object> CreateCommand(SceneStep step, Device device)
    {
        if (string.Equals(step.Message, Device.HomeAutomationRoleSwitch, StringComparison.OrdinalIgnoreCase))
        {
            string state = step.MessageBody?.Trim();
            if (!string.Equals(state, "on", StringComparison.OrdinalIgnoreCase) && !string.Equals(state, "off", StringComparison.OrdinalIgnoreCase))
                return null;

            return new Dictionary<string, object>() { ["state"] = state.ToUpperInvariant() };
        }

        if (string.Equals(step.Message, Device.HomeAutomationRoleDimmer, StringComparison.OrdinalIgnoreCase)
            && decimal.TryParse(step.MessageBody, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal brightnessPercent)
            && brightnessPercent >= 0M
            && brightnessPercent <= 100M)
        {
            int zigbeeBrightness = (int)Math.Round(brightnessPercent * 254M / 100M, MidpointRounding.AwayFromZero);
            return new Dictionary<string, object>() { ["brightness"] = zigbeeBrightness };
        }

        if (string.Equals(step.Message, Device.HomeAutomationRoleColorBound, StringComparison.OrdinalIgnoreCase))
            return CreateColorCommand(step.MessageBody, device);

        return null;
    }

    private static Dictionary<string, object> CreateColorCommand(string messageBody, Device device)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(messageBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            List<string> capabilities = device.DeviceCapabilities ?? [];

            bool hasRgb = TryGetRgb(document.RootElement, out double red, out double green, out double blue);
            bool hasTemperature = TryGetColorTemperatureKelvin(document.RootElement, out int temperatureKelvin);
            if (hasRgb && hasTemperature)
                return null;

            if (hasRgb)
            {
                if (capabilities.Contains(Device.CapabilityColorXy, StringComparer.OrdinalIgnoreCase)
                    && TryConvertRgbToXy(red, green, blue, out double convertedX, out double convertedY))
                {
                    return new Dictionary<string, object>() { ["color"] = new Dictionary<string, double>() { ["x"] = convertedX, ["y"] = convertedY } };
                }

                if (capabilities.Contains(Device.CapabilityColorHs, StringComparer.OrdinalIgnoreCase))
                {
                    ConvertRgbToHs(red, green, blue, out double convertedHue, out double convertedSaturation);
                    return new Dictionary<string, object>() { ["color"] = new Dictionary<string, double>() { ["hue"] = convertedHue, ["saturation"] = convertedSaturation } };
                }

                return null;
            }

            if (capabilities.Contains(Device.CapabilityColorXy, StringComparer.OrdinalIgnoreCase)
                && document.RootElement.TryGetProperty("x", out JsonElement x)
                && document.RootElement.TryGetProperty("y", out JsonElement y)
                && x.TryGetDecimal(out decimal xValue)
                && y.TryGetDecimal(out decimal yValue)
                && xValue is >= 0M and <= 1M
                && yValue is >= 0M and <= 1M)
            {
                return new Dictionary<string, object>() { ["color"] = new Dictionary<string, decimal>() { ["x"] = xValue, ["y"] = yValue } };
            }

            if (capabilities.Contains(Device.CapabilityColorHs, StringComparer.OrdinalIgnoreCase)
                && document.RootElement.TryGetProperty("hue", out JsonElement hue)
                && document.RootElement.TryGetProperty("saturation", out JsonElement saturation)
                && hue.TryGetDecimal(out decimal hueValue)
                && saturation.TryGetDecimal(out decimal saturationValue)
                && hueValue is >= 0M and <= 360M
                && saturationValue is >= 0M and <= 100M)
            {
                return new Dictionary<string, object>() { ["color"] = new Dictionary<string, decimal>() { ["hue"] = hueValue, ["saturation"] = saturationValue } };
            }

            if (capabilities.Contains(Device.CapabilityColorTemperature, StringComparer.OrdinalIgnoreCase)
                && hasTemperature
                && temperatureKelvin is >= 1000 and <= 10000)
            {
                int temperatureMired = (int)Math.Round(1_000_000M / temperatureKelvin, MidpointRounding.AwayFromZero);
                (int minimumMired, int maximumMired) = GetColorTemperatureRange(device.ConfigurationData);
                return new Dictionary<string, object>() { ["color_temp"] = Math.Clamp(temperatureMired, minimumMired, maximumMired) };
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static bool TryGetRgb(JsonElement command, out double red, out double green, out double blue)
    {
        red = 0;
        green = 0;
        blue = 0;
        if (!command.TryGetProperty("rgb", out JsonElement rgb)
            || rgb.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string hex = rgb.GetString()?.Trim();
        if (string.IsNullOrEmpty(hex))
            return false;
        if (hex.StartsWith("#", StringComparison.Ordinal))
            hex = hex.Substring(1);
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            return false;

        red = ((value >> 16) & 0xFF) / 255D;
        green = ((value >> 8) & 0xFF) / 255D;
        blue = (value & 0xFF) / 255D;
        return true;
    }

    private static bool TryGetColorTemperatureKelvin(JsonElement command, out int temperatureKelvin)
    {
        temperatureKelvin = 0;
        return (command.TryGetProperty("temperatureKelvin", out JsonElement temperatureKelvinValue)
                || command.TryGetProperty("temperature", out temperatureKelvinValue))
            && temperatureKelvinValue.TryGetInt32(out temperatureKelvin);
    }

    private static bool TryConvertRgbToXy(double red, double green, double blue, out double x, out double y)
    {
        double linearRed = ToLinearSrgb(red);
        double linearGreen = ToLinearSrgb(green);
        double linearBlue = ToLinearSrgb(blue);
        double tristimulusX = linearRed * 0.4124D + linearGreen * 0.3576D + linearBlue * 0.1805D;
        double tristimulusY = linearRed * 0.2126D + linearGreen * 0.7152D + linearBlue * 0.0722D;
        double tristimulusZ = linearRed * 0.0193D + linearGreen * 0.1192D + linearBlue * 0.9505D;
        double total = tristimulusX + tristimulusY + tristimulusZ;
        if (total <= double.Epsilon)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = tristimulusX / total;
        y = tristimulusY / total;
        return true;
    }

    private static void ConvertRgbToHs(double red, double green, double blue, out double hue, out double saturation)
    {
        double maximum = Math.Max(red, Math.Max(green, blue));
        double minimum = Math.Min(red, Math.Min(green, blue));
        double difference = maximum - minimum;
        saturation = maximum <= double.Epsilon ? 0D : difference / maximum * 100D;

        if (difference <= double.Epsilon)
        {
            hue = 0D;
            return;
        }

        if (maximum == red)
            hue = 60D * ((green - blue) / difference % 6D);
        else if (maximum == green)
            hue = 60D * ((blue - red) / difference + 2D);
        else
            hue = 60D * ((red - green) / difference + 4D);

        if (hue < 0D)
            hue += 360D;
    }

    private static double ToLinearSrgb(double component)
    {
        return component <= 0.04045D
            ? component / 12.92D
            : Math.Pow((component + 0.055D) / 1.055D, 2.4D);
    }

    private static (int minimumMired, int maximumMired) GetColorTemperatureRange(string configurationData)
    {
        const int defaultMinimumMired = 153;
        const int defaultMaximumMired = 500;
        if (string.IsNullOrWhiteSpace(configurationData))
            return (defaultMinimumMired, defaultMaximumMired);

        try
        {
            using JsonDocument document = JsonDocument.Parse(configurationData);
            if (!TryGetColorTemperatureRange(document.RootElement, out int minimumMired, out int maximumMired))
                return (defaultMinimumMired, defaultMaximumMired);

            return (minimumMired, maximumMired);
        }
        catch (JsonException)
        {
            return (defaultMinimumMired, defaultMaximumMired);
        }
    }

    private static bool TryGetColorTemperatureRange(JsonElement element, out int minimumMired, out int maximumMired)
    {
        minimumMired = 0;
        maximumMired = 0;
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("property", out JsonElement property)
            && string.Equals(property.GetString(), "color_temp", StringComparison.OrdinalIgnoreCase)
            && element.TryGetProperty("value_min", out JsonElement minimum)
            && element.TryGetProperty("value_max", out JsonElement maximum)
            && minimum.TryGetInt32(out minimumMired)
            && maximum.TryGetInt32(out maximumMired)
            && minimumMired > 0
            && maximumMired >= minimumMired)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("features", out JsonElement features)
            && TryGetColorTemperatureRange(features, out minimumMired, out maximumMired))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("definition", out JsonElement definition)
            && TryGetColorTemperatureRange(definition, out minimumMired, out maximumMired))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("exposes", out JsonElement exposes)
            && TryGetColorTemperatureRange(exposes, out minimumMired, out maximumMired))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
            return false;

        foreach (JsonElement item in element.EnumerateArray())
        {
            if (TryGetColorTemperatureRange(item, out minimumMired, out maximumMired))
                return true;
        }

        return false;
    }
}