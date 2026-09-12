using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Devices.Wled;
using MaNoir.HomeAutomation;
using Home.Common.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah.Wled;

public sealed class WledRuntimeService : BackgroundService
{
    private const string Platform = "wled";
    private readonly ILogger<WledRuntimeService> _logger;
    private readonly HttpClient _httpClient;
    private readonly RuntimeDeviceRegistry _runtimeRegistry;
    private readonly Dictionary<string, WledDevice> _devices = new(StringComparer.OrdinalIgnoreCase);

    public WledRuntimeService(
        ILogger<WledRuntimeService> logger,
        HttpClient httpClient = null,
        RuntimeDeviceRegistry runtimeRegistry = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _runtimeRegistry = runtimeRegistry ?? new RuntimeDeviceRegistry();
    }

    public RuntimeDeviceRegistry RuntimeRegistry => _runtimeRegistry;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int nextPollSeconds = 30;
            List<Device> configuredDevices = await new DeviceLogic()
                .FindAsync(agentId: "sarah", cancellationToken: stoppingToken);
            HashSet<string> activeDeviceIds = new(StringComparer.OrdinalIgnoreCase);

            foreach (Device configuredDevice in configuredDevices.Where(device =>
                string.Equals(device.DevicePlatform, Platform, StringComparison.OrdinalIgnoreCase)))
            {
                string host = configuredDevice.DeviceAddresses?.FirstOrDefault(address => !string.IsNullOrWhiteSpace(address));
                if (string.IsNullOrWhiteSpace(configuredDevice.DeviceInternalName) || string.IsNullOrWhiteSpace(host))
                {
                    _logger.LogWarning("Ignoring WLED device {DeviceId} without a persisted internal name or address.", configuredDevice.Id);
                    continue;
                }

                try
                {
                    activeDeviceIds.Add(configuredDevice.DeviceInternalName);
                    WledDevice device = await GetOrCreateDeviceAsync(configuredDevice.DeviceInternalName, host, stoppingToken);
                    using JsonDocument state = await new WledHttpClient(host, _httpClient)
                        .GetStateAsync(stoppingToken);
                    device.ApplyState(state.RootElement);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Could not poll WLED device {DeviceId} at {Host}.", configuredDevice.DeviceInternalName, host);
                }
            }

            foreach (string deviceId in _devices.Keys.Where(deviceId => !activeDeviceIds.Contains(deviceId)).ToList())
                _devices.Remove(deviceId);
            _runtimeRegistry.ApplySnapshot(Platform, _devices.Values.ToArray());
            await Task.Delay(TimeSpan.FromSeconds(nextPollSeconds), stoppingToken);
        }
    }

    private async Task<WledDevice> GetOrCreateDeviceAsync(string deviceId, string host, CancellationToken cancellationToken)
    {
        if (_devices.TryGetValue(deviceId, out WledDevice existing))
            return existing;

        WledHttpClient client = new(host, _httpClient);
        IReadOnlyList<string> effects;
        try
        {
            effects = await client.GetEffectsAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Could not load WLED effects for {DeviceId} at {Host}; continuing without the animation catalogue.", deviceId, host);
            effects = [];
        }
        List<LightAnimationDefinition> animations = effects
            .Select((label, index) => new LightAnimationDefinition(
                string.Concat("wled.effect.", index),
                label,
                "effect",
                true,
                label,
                ["wled", "native"],
                [
                    new LightAnimationParameter("palette", "Palette", "integer", 0, 255),
                    new LightAnimationParameter("speed", "Speed", "integer", 0, 255),
                    new LightAnimationParameter("intensity", "Intensity", "integer", 0, 255)
                ]))
            .ToList();
        WledDevice device = WledDevice.Create(
            deviceId,
            (segmentId, isOn, intensity, color, token) => client.SetStateAsync(segmentId, isOn, intensity, color, token),
            (segmentId, request, token) => client.SetAnimationAsync(segmentId, request, token),
            animations,
            (isOn, token) => client.SetStateAsync(isOn, null, null, token));
        _devices[deviceId] = device;
        _logger.LogInformation("Loaded WLED device {DeviceId} at {Host} with {EffectCount} native effects from persistent device data.", deviceId, host, effects.Count);
        return device;
    }
}