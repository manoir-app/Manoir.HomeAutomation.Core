using Home.Common.Model;
using MaNoir.HomeAutomation;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah;

public sealed class ShellyGen1CommandService
{
    private const string Platform = "shelly-gen1";
    private readonly ILogger<ShellyGen1CommandService> _logger;
    private readonly HttpClient _httpClient;

    public ShellyGen1CommandService(ILogger<ShellyGen1CommandService> logger, HttpClient httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<bool> ExecuteAsync(SceneStep step)
    {
        if (step == null)
            return false;

        Device device = await new DeviceLogic().GetByIdAsync(step.TargetId);
        if (device == null
            || !string.Equals(device.DevicePlatform, Platform, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(device.DeviceInternalName))
        {
            return false;
        }

        if (!TryGetDeviceAddress(device.ConfigurationData, out Uri deviceAddress))
            return false;

        if (!TryCreateRequestUri(deviceAddress, device, step, out Uri requestUri))
            return false;

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        AddAuthentication(request);
        using HttpResponseMessage response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return false;

        _logger.LogInformation("Sent Shelly Gen1 {Role} command to device {DeviceId} through its local API.", step.Message, device.Id);
        return true;
    }

    private static bool TryCreateRequestUri(Uri deviceAddress, Device device, SceneStep step, out Uri requestUri)
    {
        requestUri = null;
        string role = step.Message?.Trim();
        if (string.Equals(role, Device.HomeAutomationRoleSwitch, StringComparison.OrdinalIgnoreCase))
        {
            string state = step.MessageBody?.Trim().ToLowerInvariant();
            if (state is not "on" and not "off" || !TryResolveOutput(step.TargetDataName, device, out string output, out int outputIndex))
                return false;

            requestUri = new Uri(deviceAddress, string.Concat(output, "/", outputIndex.ToString(), "?turn=", state));
            return true;
        }

        if (string.Equals(role, Device.HomeAutomationRoleDimmer, StringComparison.OrdinalIgnoreCase)
            && decimal.TryParse(step.MessageBody, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal brightness)
            && brightness is >= 0M and <= 100M
            && TryResolveOutput(step.TargetDataName, device, out string dimmerOutput, out int dimmerIndex))
        {
            if (string.IsNullOrWhiteSpace(step.TargetDataName))
                dimmerOutput = "light";
            requestUri = new Uri(deviceAddress, string.Concat(dimmerOutput, "/", dimmerIndex.ToString(), "?turn=on&brightness=", brightness.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture)));
            return true;
        }

        if (string.Equals(role, Device.HomeAutomationRoleColorBound, StringComparison.OrdinalIgnoreCase)
            && TryGetRgb(step.MessageBody, out byte red, out byte green, out byte blue))
        {
            string colorOutput = "color";
            int colorIndex = 0;
            if (!string.IsNullOrWhiteSpace(step.TargetDataName)
                && !TryResolveOutput(step.TargetDataName, device, out colorOutput, out colorIndex))
            {
                return false;
            }
            requestUri = new Uri(deviceAddress, string.Concat(colorOutput, "/", colorIndex.ToString(), "?red=", red.ToString(), "&green=", green.ToString(), "&blue=", blue.ToString(), "&turn=on"));
            return true;
        }

        if (string.Equals(role, Device.HomeAutomationMainRoleShutterSwitch, StringComparison.OrdinalIgnoreCase)
            && TryResolveRollerOutput(step.TargetDataName, out int rollerIndex)
            && TryGetRollerCommand(step.MessageBody, out string rollerCommand, out bool requiresPosition)
            && (!requiresPosition || device.DeviceCapabilities?.Any(capability => string.Equals(capability, Device.CapabilityShutterPosition, StringComparison.OrdinalIgnoreCase)) == true))
        {
            requestUri = new Uri(deviceAddress, string.Concat("roller/", rollerIndex.ToString(), "?", rollerCommand));
            return true;
        }

        return false;
    }

    private static bool TryResolveRollerOutput(string targetDataName, out int rollerIndex)
    {
        rollerIndex = 0;
        if (string.IsNullOrWhiteSpace(targetDataName))
            return true;

        string[] parts = targetDataName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && string.Equals(parts[0], "Cover", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], out rollerIndex)
            && rollerIndex >= 0;
    }

    private static bool TryGetRollerCommand(string messageBody, out string command, out bool requiresPosition)
    {
        command = null;
        requiresPosition = false;
        string value = messageBody?.Trim().ToLowerInvariant();
        if (value is "open" or "close" or "stop")
        {
            command = string.Concat("go=", value);
            return true;
        }

        if (decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal position)
            && position is >= 0M and <= 100M)
        {
            requiresPosition = true;
            command = string.Concat("go=to_pos&roller_pos=", position.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }

        return false;
    }

    private static bool TryGetRgb(string messageBody, out byte red, out byte green, out byte blue)
    {
        red = 0;
        green = 0;
        blue = 0;
        try
        {
            using JsonDocument document = JsonDocument.Parse(messageBody);
            if (!document.RootElement.TryGetProperty("rgb", out JsonElement rgb) || rgb.ValueKind != JsonValueKind.String)
                return false;

            string hex = rgb.GetString()?.Trim().TrimStart('#');
            return hex?.Length == 6
                && byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out red)
                && byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out green)
                && byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out blue);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryResolveOutput(string targetDataName, Device device, out string output, out int outputIndex)
    {
        output = null;
        outputIndex = 0;
        string name = targetDataName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "Switch", StringComparison.OrdinalIgnoreCase))
        {
            output = device.DeviceRoles.Any(role => string.Equals(role, Device.HomeAutomationRoleColorBound, StringComparison.OrdinalIgnoreCase))
                ? "color"
                : device.DeviceRoles.Any(role => string.Equals(role, Device.HomeAutomationRoleDimmer, StringComparison.OrdinalIgnoreCase)) ? "light" : "relay";
            return true;
        }

        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out outputIndex) || outputIndex < 0)
            return false;

        output = parts[0].ToLowerInvariant() switch
        {
            "relay" => "relay",
            "light" => "light",
            "white" => "white",
            "color" => "color",
            _ => null
        };
        return output != null;
    }

    private static bool TryGetDeviceAddress(string configurationData, out Uri deviceAddress)
    {
        deviceAddress = null;
        if (string.IsNullOrWhiteSpace(configurationData))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(configurationData);
            if (!document.RootElement.TryGetProperty("ip", out JsonElement ip)
                || ip.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(ip.GetString()))
            {
                return false;
            }

            return Uri.TryCreate(string.Concat("http://", ip.GetString().Trim().TrimEnd('/'), "/"), UriKind.Absolute, out deviceAddress);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AddAuthentication(HttpRequestMessage request)
    {
        string password = Environment.GetEnvironmentVariable("SHELLY_GEN1_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            password = Environment.GetEnvironmentVariable("HOMEAUTOMATION_APIKEY");
        if (string.IsNullOrWhiteSpace(password))
            return;

        string username = Environment.GetEnvironmentVariable("SHELLY_GEN1_USERNAME");
        if (string.IsNullOrWhiteSpace(username))
            username = "sarah";

        string credential = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(username, ":", password)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
    }
}