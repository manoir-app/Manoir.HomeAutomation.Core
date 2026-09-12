using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Linq;
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

    public async Task<IReadOnlyList<string>> GetEffectsAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await GetJsonAsync("/json/eff", cancellationToken);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
                .Where(effect => effect.ValueKind == JsonValueKind.String)
                .Select(effect => effect.GetString())
                .Where(effect => !string.IsNullOrWhiteSpace(effect))
                .ToArray()
            : [];
    }

    public Task SetAnimationAsync(LightAnimationRequest request, CancellationToken cancellationToken = default)
    {
        return SetAnimationAsync(0, request, cancellationToken);
    }

    public Task SetAnimationAsync(int segmentId, LightAnimationRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("An animation code is required.", nameof(request));

        Dictionary<string, object> segment = new() { ["id"] = segmentId };
        Dictionary<string, object> state = new() { ["seg"] = new[] { segment } };
        if (request.Code.Equals("wled.none", StringComparison.OrdinalIgnoreCase))
            segment["fx"] = 0;
        else if (request.Code.StartsWith("wled.effect.", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(request.Code.Substring("wled.effect.".Length), out int effectIndex)
            && effectIndex >= 0)
            segment["fx"] = effectIndex;
        else
            throw new NotSupportedException($"Animation WLED inconnue : {request.Code}.");

        if (request.Parameters != null)
        {
            if (request.Parameters.TryGetValue("palette", out object palette))
                segment["pal"] = palette;
            if (request.Parameters.TryGetValue("speed", out object speed))
                segment["sx"] = speed;
            if (request.Parameters.TryGetValue("intensity", out object intensity))
                segment["ix"] = intensity;
        }

        return SendStateAsync(state, cancellationToken);
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

    public Task SetStateAsync(int segmentId, bool? isOn, decimal? intensityPercent, DeviceColor color, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object> state = new();
        Dictionary<string, object> segment = new() { ["id"] = segmentId };
        if (isOn.HasValue)
            segment["on"] = isOn.Value;
        if (intensityPercent.HasValue)
            segment["bri"] = (int)Math.Round(intensityPercent.Value * 255M / 100M);
        if (color != null)
        {
            if (color is not DeviceColor.Rgb rgb)
                throw new NotSupportedException("WLED accepte actuellement les couleurs RGB uniquement.");
            segment["col"] = new[] { new[] { rgb.Red, rgb.Green, rgb.Blue } };

        if (segment.Count > 1)
            state["seg"] = new[] { segment };
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
