using Home.Common.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah.Hue;

public sealed partial class HueRuntimeService
{
    public async Task<HttpResponseMessage> SendCommandAsync(
        string bridgeAddress,
        string apiKey,
        string lightId,
        IReadOnlyDictionary<string, object> command,
        CancellationToken cancellationToken = default)
    {
        return await _protocol.SendLightCommandAsync(bridgeAddress, apiKey, lightId, command, cancellationToken);
    }

    public static bool TryCreateCommand(SceneStep step, out Dictionary<string, object> command)
    {
        return TryCreateCommand(step, null, out command);
    }

    public static bool TryCreateCommand(SceneStep step, Device device, out Dictionary<string, object> command)
    {
        command = null;
        if (string.Equals(step.Message, Device.HomeAutomationRoleSwitch, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(step.MessageBody, "on", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(step.MessageBody, "off", StringComparison.OrdinalIgnoreCase))
                return false;

            command = new Dictionary<string, object>()
            {
                ["on"] = string.Equals(step.MessageBody, "on", StringComparison.OrdinalIgnoreCase)
            };
            return true;
        }

        if (string.Equals(step.Message, Device.HomeAutomationRoleDimmer, StringComparison.OrdinalIgnoreCase)
            && decimal.TryParse(step.MessageBody, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal brightness)
            && brightness is >= 0M and <= 100M)
        {
            command = new Dictionary<string, object>()
            {
                ["bri"] = Math.Clamp((int)Math.Round(brightness * 254M / 100M, MidpointRounding.AwayFromZero), 1, 254)
            };
            return true;
        }

        if (string.Equals(step.Message, Device.HomeAutomationRoleColorBound, StringComparison.OrdinalIgnoreCase))
            return TryCreateColorCommand(step.MessageBody, device, out command);

        return false;
    }

    private static bool TryCreateColorCommand(string messageBody, Device device, out Dictionary<string, object> command)
    {
        command = null;
        if (string.IsNullOrWhiteSpace(messageBody) || device == null)
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(messageBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            List<string> capabilities = device.DeviceCapabilities ?? [];
            JsonElement root = document.RootElement;
            bool hasRgb = TryGetRgb(root, out double red, out double green, out double blue);
            bool hasTemperature = TryGetColorTemperatureKelvin(root, out int temperatureKelvin);
            if (hasRgb && hasTemperature)
                return false;

            if (hasRgb)
            {
                if (capabilities.Contains(Device.CapabilityColorXy, StringComparer.OrdinalIgnoreCase)
                    && TryConvertRgbToXy(red, green, blue, out double x, out double y))
                {
                    (x, y) = ClipToColorGamut(x, y, GetColorGamut(device.ConfigurationData));
                    command = new Dictionary<string, object>() { ["xy"] = new[] { x, y } };
                    return true;
                }

                if (capabilities.Contains(Device.CapabilityColorHs, StringComparer.OrdinalIgnoreCase))
                {
                    ConvertRgbToHueSaturation(red, green, blue, out int hue, out int saturation);
                    command = new Dictionary<string, object>() { ["hue"] = hue, ["sat"] = saturation };
                    return true;
                }

                return false;
            }

            if (capabilities.Contains(Device.CapabilityColorXy, StringComparer.OrdinalIgnoreCase)
                && root.TryGetProperty("x", out JsonElement xValue)
                && root.TryGetProperty("y", out JsonElement yValue)
                && xValue.TryGetDouble(out double xCoordinate)
                && yValue.TryGetDouble(out double yCoordinate)
                && xCoordinate is >= 0D and <= 1D
                && yCoordinate is >= 0D and <= 1D)
            {
                (xCoordinate, yCoordinate) = ClipToColorGamut(xCoordinate, yCoordinate, GetColorGamut(device.ConfigurationData));
                command = new Dictionary<string, object>() { ["xy"] = new[] { xCoordinate, yCoordinate } };
                return true;
            }

            if (capabilities.Contains(Device.CapabilityColorHs, StringComparer.OrdinalIgnoreCase)
                && root.TryGetProperty("hue", out JsonElement hueValue)
                && root.TryGetProperty("saturation", out JsonElement saturationValue)
                && hueValue.TryGetDouble(out double hueDegrees)
                && saturationValue.TryGetDouble(out double saturationPercent)
                && hueDegrees is >= 0D and <= 360D
                && saturationPercent is >= 0D and <= 100D)
            {
                command = new Dictionary<string, object>()
                {
                    ["hue"] = (int)Math.Round(hueDegrees * 65535D / 360D, MidpointRounding.AwayFromZero),
                    ["sat"] = (int)Math.Round(saturationPercent * 254D / 100D, MidpointRounding.AwayFromZero)
                };
                return true;
            }

            if (capabilities.Contains(Device.CapabilityColorTemperature, StringComparer.OrdinalIgnoreCase)
                && hasTemperature
                && temperatureKelvin is >= 1000 and <= 10000)
            {
                int mired = (int)Math.Round(1_000_000D / temperatureKelvin, MidpointRounding.AwayFromZero);
                (int minimum, int maximum) = GetColorTemperatureRange(device.ConfigurationData);
                command = new Dictionary<string, object>() { ["ct"] = Math.Clamp(mired, minimum, maximum) };
                return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static bool TryGetRgb(JsonElement command, out double red, out double green, out double blue)
    {
        red = 0;
        green = 0;
        blue = 0;
        if (!command.TryGetProperty("rgb", out JsonElement rgb) || rgb.ValueKind != JsonValueKind.String)
            return false;

        string hex = rgb.GetString()?.Trim();
        if (hex?.StartsWith("#", StringComparison.Ordinal) == true)
            hex = hex[1..];
        if (hex?.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            return false;

        red = ((value >> 16) & 0xFF) / 255D;
        green = ((value >> 8) & 0xFF) / 255D;
        blue = (value & 0xFF) / 255D;
        return true;
    }

    private static bool TryGetColorTemperatureKelvin(JsonElement command, out int temperatureKelvin)
    {
        temperatureKelvin = 0;
        return (command.TryGetProperty("temperatureKelvin", out JsonElement value)
                || command.TryGetProperty("temperature", out value))
            && value.TryGetInt32(out temperatureKelvin);
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

    private static void ConvertRgbToHueSaturation(double red, double green, double blue, out int hue, out int saturation)
    {
        double maximum = Math.Max(red, Math.Max(green, blue));
        double minimum = Math.Min(red, Math.Min(green, blue));
        double difference = maximum - minimum;
        double hueDegrees = 0D;
        if (difference > double.Epsilon)
        {
            if (maximum == red)
                hueDegrees = 60D * ((green - blue) / difference % 6D);
            else if (maximum == green)
                hueDegrees = 60D * ((blue - red) / difference + 2D);
            else
                hueDegrees = 60D * ((red - green) / difference + 4D);
            if (hueDegrees < 0D)
                hueDegrees += 360D;
        }

        double saturationPercent = maximum <= double.Epsilon ? 0D : difference / maximum * 100D;
        hue = (int)Math.Round(hueDegrees * 65535D / 360D, MidpointRounding.AwayFromZero);
        saturation = (int)Math.Round(saturationPercent * 254D / 100D, MidpointRounding.AwayFromZero);
    }

    private static double ToLinearSrgb(double component)
    {
        return component <= 0.04045D
            ? component / 12.92D
            : Math.Pow((component + 0.055D) / 1.055D, 2.4D);
    }

    public static (double x, double y) ClipToColorGamut(double x, double y, double[][] gamut)
    {
        if (gamut == null || gamut.Length < 3 || gamut.Any(point => point == null || point.Length < 2))
            return (x, y);

        double[] red = gamut[0];
        double[] green = gamut[1];
        double[] blue = gamut[2];
        if (IsInsideTriangle(x, y, red, green, blue))
            return (x, y);

        (double x, double y, double distance) closest = ClosestPointOnSegment(x, y, red, green);
        (double x, double y, double distance) greenBlue = ClosestPointOnSegment(x, y, green, blue);
        if (greenBlue.distance < closest.distance)
            closest = greenBlue;

        (double x, double y, double distance) blueRed = ClosestPointOnSegment(x, y, blue, red);
        if (blueRed.distance < closest.distance)
            closest = blueRed;

        return (closest.x, closest.y);
    }

    private static bool IsInsideTriangle(double x, double y, double[] first, double[] second, double[] third)
    {
        double firstCross = Cross(second[0] - first[0], second[1] - first[1], x - first[0], y - first[1]);
        double secondCross = Cross(third[0] - second[0], third[1] - second[1], x - second[0], y - second[1]);
        double thirdCross = Cross(first[0] - third[0], first[1] - third[1], x - third[0], y - third[1]);
        return (firstCross >= 0D && secondCross >= 0D && thirdCross >= 0D)
            || (firstCross <= 0D && secondCross <= 0D && thirdCross <= 0D);
    }

    private static double Cross(double firstX, double firstY, double secondX, double secondY)
    {
        return firstX * secondY - firstY * secondX;
    }

    private static (double x, double y, double distance) ClosestPointOnSegment(double x, double y, double[] start, double[] end)
    {
        double deltaX = end[0] - start[0];
        double deltaY = end[1] - start[1];
        double lengthSquared = deltaX * deltaX + deltaY * deltaY;
        double factor = lengthSquared <= double.Epsilon
            ? 0D
            : Math.Clamp(((x - start[0]) * deltaX + (y - start[1]) * deltaY) / lengthSquared, 0D, 1D);
        double closestX = start[0] + factor * deltaX;
        double closestY = start[1] + factor * deltaY;
        double distanceX = x - closestX;
        double distanceY = y - closestY;
        return (closestX, closestY, distanceX * distanceX + distanceY * distanceY);
    }

    private static double[][] GetColorGamut(string configurationData)
    {
        if (string.IsNullOrWhiteSpace(configurationData))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(configurationData);
            if (!document.RootElement.TryGetProperty("capabilities", out JsonElement capabilities)
                || !capabilities.TryGetProperty("control", out JsonElement control)
                || !control.TryGetProperty("colorgamut", out JsonElement gamut)
                || gamut.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            List<double[]> points = [];
            foreach (JsonElement point in gamut.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2)
                    return null;

                points.Add([point[0].GetDouble(), point[1].GetDouble()]);
            }

            return points.Count >= 3 ? [points[0], points[1], points[2]] : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static (int minimum, int maximum) GetColorTemperatureRange(string configurationData)
    {
        const int defaultMinimum = 153;
        const int defaultMaximum = 500;
        if (string.IsNullOrWhiteSpace(configurationData))
            return (defaultMinimum, defaultMaximum);

        try
        {
            using JsonDocument document = JsonDocument.Parse(configurationData);
            JsonElement ct = document.RootElement
                .GetProperty("capabilities")
                .GetProperty("control")
                .GetProperty("ct");
            int minimum = ct.GetProperty("min").GetInt32();
            int maximum = ct.GetProperty("max").GetInt32();
            return minimum > 0 && maximum >= minimum ? (minimum, maximum) : (defaultMinimum, defaultMaximum);
        }
        catch (JsonException)
        {
            return (defaultMinimum, defaultMaximum);
        }
        catch (KeyNotFoundException)
        {
            return (defaultMinimum, defaultMaximum);
        }
    }
}