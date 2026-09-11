using MaNoir.HomeAutomation.Devices;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah.Awtrix;

public sealed class AwtrixHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AwtrixHttpClient(string host, HttpClient httpClient = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public string Host { get; }

    public Task SetDisplayTextAsync(string text, DeviceColor color, string icon, CancellationToken cancellationToken = default)
    {
        return PostJsonAsync(
            "/api/custom?name=manoir_display",
            new { text, color = ToHexColor(color), icon },
            cancellationToken);
    }

    public Task ClearDisplayAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Delete, "/api/custom?name=manoir_display", null, cancellationToken);
    }

    public Task SendNotificationAsync(RuntimeDeviceNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification == null)
            throw new ArgumentNullException(nameof(notification));

        return PostJsonAsync(
            "/api/notify",
            new
            {
                text = notification.Text,
                color = ToHexColor(notification.Color),
                icon = notification.Icon,
                duration = notification.DurationSeconds,
                repeat = notification.Repeat,
                hold = notification.Hold
            },
            cancellationToken);
    }

    private async Task PostJsonAsync(string path, object body, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Post, path, body, cancellationToken);
    }

    private async Task SendAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, new Uri(CreateBaseAddress(Host), path));
        if (body != null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, _jsonOptions), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static Uri CreateBaseAddress(string host)
    {
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new Uri(host.TrimEnd('/') + "/");
        return new Uri(string.Concat("http://", host.TrimEnd('/'), "/"));
    }

    private static string ToHexColor(DeviceColor color)
    {
        return color is DeviceColor.Rgb rgb
            ? string.Concat("#", rgb.Red.ToString("X2"), rgb.Green.ToString("X2"), rgb.Blue.ToString("X2"))
            : null;
    }
}
