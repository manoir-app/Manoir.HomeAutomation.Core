using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Protocols.Shelly;

/// <summary>
/// HTTP JSON-RPC protocol client for Shelly Gen2 and newer devices.
/// </summary>
public sealed class ShellyGen2Protocol
{
    private readonly HttpClient _httpClient;
    private readonly Uri _deviceAddress;
    private readonly string _password;

    public ShellyGen2Protocol(string ipAddress, string password = null, HttpClient httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new ArgumentException("A Shelly IP address is required.", nameof(ipAddress));

        _deviceAddress = new Uri(string.Concat("http://", ipAddress.Trim().TrimEnd('/'), "/"));
        _password = password;
        _httpClient = httpClient ?? new HttpClient();
    }

    public string Address => _deviceAddress.Host;

    public Task<JsonDocument> GetInfoAsync(CancellationToken cancellationToken = default) =>
        CallAsync("Shelly.GetDeviceInfo", null, cancellationToken);

    public Task<JsonDocument> GetStatusAsync(CancellationToken cancellationToken = default) =>
        CallAsync("Shelly.GetStatus", null, cancellationToken);

    public Task<JsonDocument> GetMqttConfigAsync(CancellationToken cancellationToken = default) =>
        CallAsync("Mqtt.GetConfig", null, cancellationToken);

    public Task<JsonDocument> SetComponentStateAsync(
        string componentType,
        int componentIndex,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentType))
            throw new ArgumentException("A Shelly component type is required.", nameof(componentType));
        if (componentIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(componentIndex));

        string method = string.Concat(ToComponentName(componentType), ".Set");
        Dictionary<string, object> requestParameters = new Dictionary<string, object>(parameters ?? new Dictionary<string, object>())
        {
            ["id"] = componentIndex
        };
        return CallAsync(method, requestParameters, cancellationToken);
    }

    public Task<JsonDocument> CallAsync(string method, object parameters, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("A Shelly RPC method is required.", nameof(method));
        return CallCoreAsync(method, parameters, cancellationToken);
    }

    private async Task<JsonDocument> CallCoreAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(new { id = 1, method, @params = parameters });
        using HttpRequestMessage request = CreateRequest(body, null);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            DigestAuthentication authentication = CreateDigestAuthentication(response, method);
            using HttpRequestMessage authenticatedRequest = CreateRequest(body, authentication);
            using HttpResponseMessage authenticatedResponse = await _httpClient.SendAsync(authenticatedRequest, cancellationToken);
            authenticatedResponse.EnsureSuccessStatusCode();
            return await ParseResponseAsync(authenticatedResponse, cancellationToken);
        }

        response.EnsureSuccessStatusCode();
        return await ParseResponseAsync(response, cancellationToken);
    }

    private static async Task<JsonDocument> ParseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (document.RootElement.TryGetProperty("error", out JsonElement error))
        {
            string message = error.TryGetProperty("message", out JsonElement errorMessage) ? errorMessage.GetString() : error.GetRawText();
            document.Dispose();
            throw new HttpRequestException(string.Concat("Shelly RPC error: ", message));
        }

        if (!document.RootElement.TryGetProperty("result", out JsonElement result))
            return document;

        JsonDocument resultDocument = JsonDocument.Parse(result.GetRawText());
        document.Dispose();
        return resultDocument;
    }

    private HttpRequestMessage CreateRequest(string body, DigestAuthentication authentication)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(_deviceAddress, "rpc"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (authentication != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Digest",
                string.Concat(
                    "username=\"", authentication.username, "\", ",
                    "realm=\"", authentication.realm, "\", ",
                    "nonce=\"", authentication.nonce, "\", ",
                    "uri=\"/rpc\", ",
                    "algorithm=", authentication.algorithm, ", ",
                    "response=\"", authentication.response, "\", ",
                    "qop=auth, ",
                    "nc=", authentication.nc, ", ",
                    "cnonce=\"", authentication.cnonce, "\""));
        }
        return request;
    }

    private DigestAuthentication CreateDigestAuthentication(HttpResponseMessage response, string method)
    {
        if (string.IsNullOrWhiteSpace(_password)
            || !response.Headers.TryGetValues("WWW-Authenticate", out IEnumerable<string> values))
            throw new HttpRequestException("Shelly Gen2 demande Digest, mais aucun mot de passe n'est disponible.");

        string challenge = null;
        foreach (string value in values)
        {
            if (value.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase))
            {
                challenge = value;
                break;
            }
        }

        if (!TryGetChallengeValue(challenge, "realm", out string realm)
            || !TryGetChallengeValue(challenge, "nonce", out string nonce))
            throw new HttpRequestException("Challenge Digest Shelly invalide.");

        const string nonceCount = "00000001";
        int clientNonce = RandomNumberGenerator.GetInt32(int.MaxValue);
        string ha1 = Sha256(string.Concat("admin:", realm, ":", _password));
        string ha2 = Sha256("POST:/rpc");
        string digest = Sha256(string.Concat(
            ha1, ":", nonce, ":", nonceCount, ":", clientNonce.ToString(CultureInfo.InvariantCulture), ":auth:", ha2));
        return new DigestAuthentication(realm, "admin", nonce, clientNonce, nonceCount, digest, "SHA-256");
    }

    private static string ToComponentName(string componentType)
    {
        return componentType switch
        {
            "switch" => "Switch",
            "light" => "Light",
            "rgb" => "RGB",
            "cover" => "Cover",
            _ => throw new ArgumentException(string.Concat("Unsupported Shelly component type: ", componentType), nameof(componentType))
        };
    }

    private static bool TryGetChallengeValue(string challenge, string name, out string value)
    {
        Match match = Regex.Match(
            challenge ?? string.Empty,
            string.Concat("(?:^|,)\\s*", Regex.Escape(name), "=\\\"(?<value>[^\\\"]+)\\\""),
            RegexOptions.IgnoreCase);
        value = match.Success ? match.Groups["value"].Value : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record DigestAuthentication(
        string realm,
        string username,
        string nonce,
        int cnonce,
        string nc,
        string response,
        string algorithm);
}
