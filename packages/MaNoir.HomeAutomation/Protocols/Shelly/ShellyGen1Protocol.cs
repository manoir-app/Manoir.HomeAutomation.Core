using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.HomeAutomation.Devices;

namespace MaNoir.HomeAutomation.Protocols.Shelly;

/// <summary>
/// HTTP protocol client for Shelly Gen1 devices.
/// </summary>
public sealed class ShellyGen1Protocol
{
    private readonly HttpClient _httpClient;
    private readonly Uri _deviceAddress;
    private readonly string _username;
    private readonly string _password;

    public ShellyGen1Protocol(string ipAddress, string username = null, string password = null, HttpClient httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new ArgumentException("A Shelly IP address is required.", nameof(ipAddress));

        _deviceAddress = new Uri(string.Concat("http://", ipAddress.Trim().TrimEnd('/'), "/"));
        _username = username;
        _password = password;
        _httpClient = httpClient ?? new HttpClient();
    }

    public string Address => _deviceAddress.Host;

    public Task<JsonDocument> GetInfoAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync(new Uri(_deviceAddress, "shelly"), cancellationToken);

    public Task<JsonDocument> GetStatusAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync(new Uri(_deviceAddress, "status"), cancellationToken);

    public Task<JsonDocument> GetRollerStatusAsync(int index, CancellationToken cancellationToken = default) =>
        GetJsonAsync(new Uri(_deviceAddress, string.Concat("roller/", FormatIndex(index))), cancellationToken);

    public async Task<bool?> GetRollerPositioningAsync(int index, CancellationToken cancellationToken = default)
    {
        using JsonDocument status = await GetRollerStatusAsync(index, cancellationToken);
        return status.RootElement.TryGetProperty("positioning", out JsonElement positioning)
            && positioning.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? positioning.GetBoolean()
            : null;
    }

    public Task<JsonDocument> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync(new Uri(_deviceAddress, "settings"), cancellationToken);

    public static string GetMqttDeviceId(JsonElement settings, string fallbackDeviceId)
    {
        if (settings.TryGetProperty("mqtt", out JsonElement mqtt)
            && mqtt.TryGetProperty("id", out JsonElement id)
            && id.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(id.GetString()))
            return id.GetString();

        return fallbackDeviceId;
    }

    public static string GetMqttTopic(JsonElement settings, string fallbackDeviceId)
    {
        return string.Concat("shellies/", GetMqttDeviceId(settings, fallbackDeviceId));
    }

    public Task SetRelayAsync(int index, bool isOn, CancellationToken cancellationToken = default) =>
        SendAsync(new Uri(_deviceAddress, string.Concat("relay/", FormatIndex(index), "?turn=", isOn ? "on" : "off")), cancellationToken);

    public Task SetRollerAsync(int index, string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("A Shelly Gen1 roller command is required.", nameof(command));

        string query = decimal.TryParse(command, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal position)
            ? string.Concat("go=to_pos&roller_pos=", position.ToString("0.############################", CultureInfo.InvariantCulture))
            : string.Concat("go=", Uri.EscapeDataString(command));
        return SendAsync(new Uri(_deviceAddress, string.Concat("roller/", FormatIndex(index), "?", query)), cancellationToken);
    }

    public Task SetLightAsync(int index, bool? isOn, decimal? brightness, CancellationToken cancellationToken = default)
    {
        List<string> parameters = new List<string>();
        if (isOn.HasValue)
            parameters.Add(string.Concat("turn=", isOn.Value ? "on" : "off"));
        if (brightness.HasValue)
            parameters.Add(string.Concat("brightness=", brightness.Value.ToString("0.############################", CultureInfo.InvariantCulture)));
        return SendAsync(new Uri(_deviceAddress, string.Concat("light/", FormatIndex(index), "?", string.Join("&", parameters))), cancellationToken);
    }

    public Task SetColorAsync(int index, bool? isOn, decimal? brightness, DeviceColor color, CancellationToken cancellationToken = default)
    {
        if (color is not DeviceColor.Rgb rgb)
            throw new ArgumentException("Shelly Gen1 color commands require an RGB color.", nameof(color));

        List<string> parameters = new List<string>
        {
            string.Concat("turn=", isOn.GetValueOrDefault(true) ? "on" : "off"),
            string.Concat("red=", rgb.Red.ToString(CultureInfo.InvariantCulture)),
            string.Concat("green=", rgb.Green.ToString(CultureInfo.InvariantCulture)),
            string.Concat("blue=", rgb.Blue.ToString(CultureInfo.InvariantCulture))
        };
        if (brightness.HasValue)
            parameters.Add(string.Concat("brightness=", brightness.Value.ToString("0.############################", CultureInfo.InvariantCulture)));
        return SendAsync(new Uri(_deviceAddress, string.Concat("color/", FormatIndex(index), "?", string.Join("&", parameters))), cancellationToken);
    }

    private async Task<JsonDocument> GetJsonAsync(Uri address, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendRequestAsync(HttpMethod.Get, address, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task SendAsync(Uri address, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendRequestAsync(HttpMethod.Get, address, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri address, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new HttpRequestMessage(method, address);
        if (!string.IsNullOrWhiteSpace(_username) && _password != null)
        {
            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(_username, ":", _password)));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static string FormatIndex(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return index.ToString(CultureInfo.InvariantCulture);
    }
}
