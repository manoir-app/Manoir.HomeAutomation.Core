using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Protocols.Hue;

/// <summary>
/// Sends state commands to a Philips Hue bridge.
/// </summary>
public sealed class HueProtocol
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _httpTimeout;

    public HueProtocol(HttpClient httpClient, TimeSpan httpTimeout)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (httpTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(httpTimeout));

        _httpTimeout = httpTimeout;
    }

    public async Task<HttpResponseMessage> SendLightCommandAsync(
        string bridgeAddress,
        string apiKey,
        string lightId,
        IReadOnlyDictionary<string, object> command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bridgeAddress))
            throw new ArgumentException("A Hue bridge address is required.", nameof(bridgeAddress));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("A Hue API key is required.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(lightId))
            throw new ArgumentException("A Hue light identifier is required.", nameof(lightId));
        if (command == null)
            throw new ArgumentNullException(nameof(command));

        using HttpRequestMessage request = new(
            HttpMethod.Put,
            new Uri($"{NormalizeBridgeAddress(bridgeAddress)}/api/{Uri.EscapeDataString(apiKey)}/lights/{Uri.EscapeDataString(lightId)}/state"));
        request.Content = new StringContent(JsonSerializer.Serialize(command), Encoding.UTF8, "application/json");
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_httpTimeout);
        return await _httpClient.SendAsync(request, timeoutSource.Token);
    }

    public static string NormalizeBridgeAddress(string bridgeAddress)
    {
        string value = bridgeAddress?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A Hue bridge address is required.", nameof(bridgeAddress));

        return value.Contains("://", StringComparison.Ordinal) ? value : $"http://{value}";
    }
}
