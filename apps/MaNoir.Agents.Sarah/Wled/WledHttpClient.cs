using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah.Wled;

public sealed class WledHttpClient
{
    private readonly HttpClient _httpClient;

    public WledHttpClient(string host, HttpClient httpClient = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public string Host { get; }

    public async Task<JsonDocument> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        return await GetJsonAsync("/json/info", cancellationToken);
    }

    public async Task<JsonDocument> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return await GetJsonAsync("/json/state", cancellationToken);
    }

    public Task SetStateAsync(bool? isOn, decimal? intensityPercent, DeviceColor color, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object> state = new();
        if (isOn.HasValue)
            state["on"] = isOn.Value;
        if (intensityPercent.HasValue)
            state["bri"] = (int)Math.Round(intensityPercent.Value * 255M / 100M);
        if (color != null)
        {
            if (color is not DeviceColor.Rgb rgb)
                throw new NotSupportedException("WLED accepte actuellement les couleurs RGB uniquement.");
            state["seg"] = new[] { new { col = new[] { new[] { rgb.Red, rgb.Green, rgb.Blue } } } };
        }

        return SendStateAsync(state, cancellationToken);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(CreateUri(path), cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task SendStateAsync(Dictionary<string, object> state, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, CreateUri("/json/state"));
        request.Content = new StringContent(JsonSerializer.Serialize(state), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private Uri CreateUri(string path)
    {
        string baseAddress = Host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? Host.TrimEnd('/')
            : string.Concat("http://", Host.TrimEnd('/'));
        return new Uri(string.Concat(baseAddress, path));
    }
}
