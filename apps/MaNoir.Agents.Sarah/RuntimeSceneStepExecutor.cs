using Home.Common.Model;
using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah;

public sealed class RuntimeSceneStepExecutor
{
    public async Task<bool> ExecuteAsync(
        IDevice device,
        SceneStep step,
        CancellationToken cancellationToken = default)
    {
        if (device == null || step == null || step.TargetKind != SceneStepTargetKind.Device)
            return false;

        IEnumerable<IDeviceCapability> capabilities = GetCapabilities(device, step.TargetDataName);
        if (string.Equals(step.Message, Device.HomeAutomationRoleSwitch, StringComparison.OrdinalIgnoreCase)
            && TryParseSwitch(step.MessageBody, out bool isOn))
        {
            IToggleSwitchDevice toggle = capabilities.OfType<IToggleSwitchDevice>().FirstOrDefault();
            if (toggle == null)
                return false;

            await toggle.SetSwitchStateAsync(isOn, cancellationToken);
            return true;
        }

        if (string.Equals(step.Message, Device.HomeAutomationRoleDimmer, StringComparison.OrdinalIgnoreCase)
            && decimal.TryParse(step.MessageBody, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal intensity)
            && intensity is >= 0M and <= 100M)
        {
            IIntensityGradientDevice dimmer = capabilities.OfType<IIntensityGradientDevice>().FirstOrDefault();
            if (dimmer == null)
                return false;

            await dimmer.SetIntensityAsync(intensity, cancellationToken);
            return true;
        }

        if (string.Equals(step.Message, Device.HomeAutomationMainRoleShutterSwitch, StringComparison.OrdinalIgnoreCase)
            && capabilities.OfType<ICoverDevice>().FirstOrDefault() is ICoverDevice cover)
        {
            if (string.Equals(step.MessageBody?.Trim(), "open", StringComparison.OrdinalIgnoreCase))
            {
                await cover.OpenAsync(cancellationToken);
                return true;
            }

            if (string.Equals(step.MessageBody?.Trim(), "close", StringComparison.OrdinalIgnoreCase))
            {
                await cover.CloseAsync(cancellationToken);
                return true;
            }

            if (string.Equals(step.MessageBody?.Trim(), "stop", StringComparison.OrdinalIgnoreCase))
            {
                await cover.StopAsync(cancellationToken);
                return true;
            }

            if (decimal.TryParse(step.MessageBody, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal position)
                && position is >= 0M and <= 100M)
            {
                if (capabilities.OfType<IPositionableCoverDevice>().FirstOrDefault() is not IPositionableCoverDevice positionableCover)
                    return false;

                await positionableCover.SetPositionAsync(position, cancellationToken);
                return true;
            }
        }

        if (string.Equals(step.Message, Device.HomeAutomationRoleColorBound, StringComparison.OrdinalIgnoreCase)
            && TryParseColor(step.MessageBody, out DeviceColor color))
        {
            IChromaticColorDevice chromaticColor = capabilities.OfType<IChromaticColorDevice>().FirstOrDefault();
            if (chromaticColor == null)
                return false;

            await chromaticColor.SetColorAsync(color, cancellationToken);
            return true;
        }

        if (IsTemperatureMessage(step.Message)
            && TryParseTemperature(step.MessageBody, out int kelvin))
        {
            IColorTemperatureDevice temperature = capabilities.OfType<IColorTemperatureDevice>().FirstOrDefault();
            if (temperature == null)
                return false;

            await temperature.SetColorTemperatureAsync(kelvin, cancellationToken);
            return true;
        }

        return false;
    }

    private static IEnumerable<IDeviceCapability> GetCapabilities(IDevice device, string elementName)
    {
        IEnumerable<IDeviceCapability> deviceCapabilities = device.Capabilities ?? [];
        IEnumerable<IDeviceElement> elements = device.Elements ?? [];
        if (!string.IsNullOrWhiteSpace(elementName))
            elements = elements.Where(element => string.Equals(element.Name, elementName, StringComparison.OrdinalIgnoreCase));

        return deviceCapabilities.Concat(elements.SelectMany(element => element.Capabilities ?? []));
    }

    private static bool TryParseSwitch(string value, out bool isOn)
    {
        isOn = false;
        if (string.Equals(value?.Trim(), "on", StringComparison.OrdinalIgnoreCase))
        {
            isOn = true;
            return true;
        }

        return string.Equals(value?.Trim(), "off", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseColor(string value, out DeviceColor color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("rgb", out JsonElement rgb)
                && rgb.ValueKind == JsonValueKind.String
                && TryParseHexColor(rgb.GetString(), out byte red, out byte green, out byte blue))
            {
                color = new DeviceColor.Rgb(red, green, blue);
                return true;
            }

            if (root.TryGetProperty("x", out JsonElement x)
                && root.TryGetProperty("y", out JsonElement y)
                && x.TryGetDouble(out double xCoordinate)
                && y.TryGetDouble(out double yCoordinate))
            {
                color = new DeviceColor.Xy(xCoordinate, yCoordinate);
                return true;
            }

            if (root.TryGetProperty("hue", out JsonElement hue)
                && root.TryGetProperty("saturation", out JsonElement saturation)
                && hue.TryGetDouble(out double hueDegrees)
                && saturation.TryGetDouble(out double saturationPercent))
            {
                color = new DeviceColor.Hsv(hueDegrees, saturationPercent / 100D, 1D);
                return true;
            }
        }
        catch (JsonException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return false;
    }

    private static bool TryParseHexColor(string value, out byte red, out byte green, out byte blue)
    {
        red = 0;
        green = 0;
        blue = 0;
        string hex = value?.Trim().TrimStart('#');
        return hex?.Length == 6
            && byte.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red)
            && byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green)
            && byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue);
    }

    private static bool IsTemperatureMessage(string message)
    {
        return string.Equals(message, "color-temperature", StringComparison.OrdinalIgnoreCase)
            || string.Equals(message, "temperature", StringComparison.OrdinalIgnoreCase)
            || string.Equals(message, "color.temperature", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseTemperature(string value, out int kelvin)
    {
        kelvin = 0;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out kelvin))
            return kelvin > 0;

        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return document.RootElement.TryGetProperty("kelvin", out JsonElement property)
                && property.TryGetInt32(out kelvin)
                && kelvin > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
