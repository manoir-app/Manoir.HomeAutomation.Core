using Home.Common.Model;
using MaNoir.HomeAutomation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah;

public sealed class ShellyGen2CommandService
{
    private const string Platform = "shelly-gen2";
    private readonly ILogger<ShellyGen2CommandService> _logger;
    private readonly HttpClient _httpClient;

    public ShellyGen2CommandService(ILogger<ShellyGen2CommandService> logger, HttpClient httpClient = null)
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
            || !TryGetDeviceAddress(device.ConfigurationData, out Uri deviceAddress)
            || !TryCreateRpcCommand(step, device, out string method, out object parameters))
        {
            return false;
        }

        try
        {
            using JsonDocument ignored = await CallRpcAsync(deviceAddress, method, parameters);
            _logger.LogInformation("Sent Shelly Gen2+ {Method} command to device {DeviceId} through its local RPC API.", method, device.Id);
            return true;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogDebug(exception, "Unable to send Shelly Gen2+ switch command to device {DeviceId}.", device.Id);
            return false;
        }
        catch (JsonException exception)
        {
            _logger.LogDebug(exception, "Shelly Gen2+ device {DeviceId} returned an invalid RPC response.", device.Id);
            return false;
        }
    }

    private static bool TryCreateRpcCommand(SceneStep step, Device device, out string method, out object parameters)
    {
        method = null;
        parameters = null;
        if (string.Equals(step.Message, Device.HomeAutomationRoleSwitch, StringComparison.OrdinalIgnoreCase)
            && TryGetCommand(step.MessageBody, out bool on))
        {
            if (TryGetComponentIndex(step.TargetDataName, "RGB", out int rgbIndex)
                && device.DeviceRoles.Contains(Device.HomeAutomationRoleColorBound, StringComparer.OrdinalIgnoreCase))
            {
                method = "RGB.Set";
                parameters = new { id = rgbIndex, on };
                return true;
            }

            if (TryGetComponentIndex(step.TargetDataName, "Light", out int lightIndex)
                && device.DeviceRoles.Contains(Device.HomeAutomationRoleDimmer, StringComparer.OrdinalIgnoreCase))
            {
                method = "Light.Set";
                parameters = new { id = lightIndex, on };
                return true;
            }

            if (TryGetComponentIndex(step.TargetDataName, "Switch", out int switchIndex)
                && device.DeviceRoles.Contains(Device.HomeAutomationRoleSwitch, StringComparer.OrdinalIgnoreCase))
            {
                method = "Switch.Set";
                parameters = new { id = switchIndex, on };
                return true;
            }
        }

        if (string.Equals(step.Message, Device.HomeAutomationRoleDimmer, StringComparison.OrdinalIgnoreCase)
            && device.DeviceRoles.Contains(Device.HomeAutomationRoleDimmer, StringComparer.OrdinalIgnoreCase)
            && decimal.TryParse(step.MessageBody, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal brightness)
            && brightness is >= 0M and <= 100M)
        {
            if (TryGetComponentIndex(step.TargetDataName, "RGB", out int rgbDimmerIndex)
                && device.DeviceRoles.Contains(Device.HomeAutomationRoleColorBound, StringComparer.OrdinalIgnoreCase))
            {
                method = "RGB.Set";
                parameters = new { id = rgbDimmerIndex, on = true, brightness };
                return true;
            }

            if (TryGetComponentIndex(step.TargetDataName, "Light", out int dimmerIndex))
            {
                method = "Light.Set";
                parameters = new { id = dimmerIndex, on = true, brightness };
                return true;
            }
        }

        if (string.Equals(step.Message, Device.HomeAutomationRoleColorBound, StringComparison.OrdinalIgnoreCase)
            && device.DeviceRoles.Contains(Device.HomeAutomationRoleColorBound, StringComparer.OrdinalIgnoreCase)
            && TryGetComponentIndex(step.TargetDataName, "RGB", out int colorIndex)
            && TryGetRgb(step.MessageBody, out byte red, out byte green, out byte blue))
        {
            method = "RGB.Set";
            parameters = new { id = colorIndex, on = true, rgb = new[] { (int)red, (int)green, (int)blue } };
            return true;
        }

        if (string.Equals(step.Message, Device.HomeAutomationMainRoleShutterSwitch, StringComparison.OrdinalIgnoreCase)
            && device.DeviceRoles.Contains(Device.HomeAutomationMainRoleShutterSwitch, StringComparer.OrdinalIgnoreCase)
            && TryGetComponentIndex(step.TargetDataName, "Cover", out int coverIndex)
            && TryGetCoverCommand(step.MessageBody, device, out string coverMethod, out object coverParameters))
        {
            method = coverMethod;
            parameters = coverParameters is decimal position ? new { id = coverIndex, pos = position } : new { id = coverIndex };
            return true;
        }

        return false;
    }

    private async Task<JsonDocument> CallRpcAsync(Uri deviceAddress, string method, object parameters)
    {
        using HttpResponseMessage response = await SendRpcAsync(deviceAddress, method, parameters, null);
        if (response.StatusCode == HttpStatusCode.Unauthorized
            && TryGetDigestAuthentication(response, out DigestAuthentication authentication))
        {
            using HttpResponseMessage authenticatedResponse = await SendRpcAsync(deviceAddress, method, parameters, authentication);
            authenticatedResponse.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await authenticatedResponse.Content.ReadAsStringAsync());
        }

        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> SendRpcAsync(Uri deviceAddress, string method, object parameters, DigestAuthentication authentication)
    {
        string body = authentication == null
            ? JsonSerializer.Serialize(new { id = 1, method, @params = parameters })
            : JsonSerializer.Serialize(new { id = 1, method, @params = parameters, auth = authentication });
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(deviceAddress, "rpc"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return await _httpClient.SendAsync(request);
    }

    private static bool TryGetCommand(string value, out bool on)
    {
        on = string.Equals(value?.Trim(), "on", StringComparison.OrdinalIgnoreCase);
        return on || string.Equals(value?.Trim(), "off", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetComponentIndex(string targetDataName, string componentName, out int componentIndex)
    {
        componentIndex = 0;
        if (string.IsNullOrWhiteSpace(targetDataName) || string.Equals(targetDataName, componentName, StringComparison.OrdinalIgnoreCase))
            return true;

        string[] parts = targetDataName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && string.Equals(parts[0], componentName, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out componentIndex)
            && componentIndex >= 0;
    }

    private static bool TryGetCoverCommand(string value, Device device, out string method, out object parameters)
    {
        method = null;
        parameters = null;
        string command = value?.Trim().ToLowerInvariant();
        method = command switch
        {
            "open" => "Cover.Open",
            "close" => "Cover.Close",
            "stop" => "Cover.Stop",
            _ => null
        };
        if (method != null)
            return true;

        if (device.DeviceCapabilities?.Contains(Device.CapabilityShutterPosition, StringComparer.OrdinalIgnoreCase) == true
            && decimal.TryParse(command, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal position)
            && position is >= 0M and <= 100M)
        {
            method = "Cover.GoToPosition";
            parameters = position;
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
                && byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red)
                && byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green)
                && byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetDeviceAddress(string configurationData, out Uri deviceAddress)
    {
        deviceAddress = null;
        if (string.IsNullOrWhiteSpace(configurationData))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(configurationData);
            return document.RootElement.TryGetProperty("ip", out JsonElement ip)
                && ip.ValueKind == JsonValueKind.String
                && Uri.TryCreate(string.Concat("http://", ip.GetString()?.Trim().TrimEnd('/'), "/"), UriKind.Absolute, out deviceAddress);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetDigestAuthentication(HttpResponseMessage response, out DigestAuthentication authentication)
    {
        authentication = null;
        string password = GetPassword();
        if (string.IsNullOrWhiteSpace(password)
            || !response.Headers.TryGetValues("WWW-Authenticate", out IEnumerable<string> values))
        {
            return false;
        }

        string challenge = values.FirstOrDefault(value => value.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(challenge)
            || !TryGetDigestChallengeValue(challenge, "realm", out string realm)
            || !TryGetDigestChallengeValue(challenge, "nonce", out string nonce))
        {
            return false;
        }

        string username = Environment.GetEnvironmentVariable("SHELLY_GEN2_USERNAME");
        if (string.IsNullOrWhiteSpace(username))
            username = "sarah";

        const string nonceCount = "00000001";
        int clientNonce = RandomNumberGenerator.GetInt32(int.MaxValue);
        string ha1 = ComputeSha256(string.Concat(username, ":", realm, ":", password));
        string ha2 = ComputeSha256("POST:/rpc");
        string digestResponse = ComputeSha256(string.Concat(ha1, ":", nonce, ":", nonceCount, ":", clientNonce.ToString(CultureInfo.InvariantCulture), ":auth:", ha2));
        authentication = new DigestAuthentication(realm, username, nonce, clientNonce, nonceCount, digestResponse, "SHA-256");
        return true;
    }

    private static bool TryGetDigestChallengeValue(string challenge, string name, out string value)
    {
        Match match = Regex.Match(challenge, string.Concat("(?:^|,)\\s*", Regex.Escape(name), "=\\\"(?<value>[^\\\"]+)\\\""), RegexOptions.IgnoreCase);
        value = match.Success ? match.Groups["value"].Value : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string GetPassword()
    {
        string password = Environment.GetEnvironmentVariable("SHELLY_GEN2_PASSWORD");
        return string.IsNullOrWhiteSpace(password) ? Environment.GetEnvironmentVariable("HOMEAUTOMATION_APIKEY") : password;
    }

    private sealed record DigestAuthentication(string realm, string username, string nonce, int cnonce, string nc, string response, string algorithm);
}