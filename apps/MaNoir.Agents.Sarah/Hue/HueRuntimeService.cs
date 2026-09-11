using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.Agents.Sarah;
using MaNoir.HomeAutomation;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Devices.Hue;
using MaNoir.HomeAutomation.Protocols.Hue;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah.Hue;

public sealed partial class HueRuntimeService : BackgroundService
{
    private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(200);
    private const string Platform = "hue";
    private readonly ILogger<HueRuntimeService> _logger;
    private readonly SarahDeviceService _deviceService;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _httpTimeout;
    private readonly RuntimeDeviceRegistry _runtimeRegistry;
    private readonly HueProtocol _protocol;

    public HueRuntimeService(
        ILogger<HueRuntimeService> logger,
        HttpClient httpClient = null,
        TimeSpan? httpTimeout = null,
        SarahDeviceService deviceService = null,
        RuntimeDeviceRegistry runtimeRegistry = null)
    {
        _logger = logger;
        _runtimeRegistry = runtimeRegistry ?? new RuntimeDeviceRegistry();
        _deviceService = deviceService ?? new SarahDeviceService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SarahDeviceService>.Instance,
            _runtimeRegistry);
        _httpClient = httpClient ?? new HttpClient();
        _httpTimeout = httpTimeout ?? DefaultHttpTimeout;
        _protocol = new HueProtocol(_httpClient, _httpTimeout);
    }

    public RuntimeDeviceRegistry RuntimeRegistry => _runtimeRegistry;

    public HueProtocol Protocol => _protocol;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!TryGetConfiguration(out _, out _))
        {
            _logger.LogInformation("Hue runtime is disabled because HUE_BRIDGE_ADDRESS or HUE_API_KEY is not configured.");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(GetPollingIntervalSeconds()));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Unable to refresh Hue devices.");
            }
        }
    }

    public async Task<List<Device>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetConfiguration(out string bridgeAddress, out string apiKey))
            return [];

        Dictionary<string, HueLight> lights = await FetchLightsAsync(bridgeAddress, apiKey, cancellationToken);

        List<HueLightDevice> runtimeLights = CreateRuntimeLights(bridgeAddress, apiKey, lights, this);
        HueBridgeDevice runtimeBridge = HueBridgeDevice.Create(bridgeAddress, runtimeLights);
        _runtimeRegistry.ApplySnapshot("hue-bridge", [runtimeBridge, .. runtimeLights]);

        List<Device> devices = CreateDevices(bridgeAddress, lights);
        await _deviceService.RegisterDevicesAsync("sarah", devices, cancellationToken);

        foreach (Device device in devices.Where(device => !string.Equals(device.Id, "hue-bridge", StringComparison.OrdinalIgnoreCase)))
        {
            string lightId = device.DeviceInternalName[5..];
            if (!lights.TryGetValue(lightId, out HueLight light))
                continue;

            await _deviceService.OnDeviceStateChangedAsync(
                Platform,
                device.Id,
                Device.HomeAutomationRoleSwitch,
                light.State?.Reachable == false ? "offline" : "online",
                CreateStateValues(light),
                cancellationToken);
        }

        _logger.LogInformation("Refreshed {LightCount} Hue light(s) from bridge {BridgeAddress}.", lights.Count, bridgeAddress);
        return devices;
    }

    public async Task<Dictionary<string, HueLight>> FetchLightsAsync(
        string bridgeAddress,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_httpTimeout);
            using HttpResponseMessage response = await _httpClient.GetAsync(
                BuildLightsUri(bridgeAddress, apiKey),
                timeoutSource.Token);
            if (response.IsSuccessStatusCode)
            {
                await using System.IO.Stream content = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
                return await JsonSerializer.DeserializeAsync<Dictionary<string, HueLight>>(
                    content,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    timeoutSource.Token) ?? [];
            }

            if (attempt < 2 && IsTransientReadStatus(response.StatusCode))
            {
                await Task.Delay(ReadRetryDelay, cancellationToken);
                continue;
            }

            string errorBody = await response.Content.ReadAsStringAsync(timeoutSource.Token);
            throw new HttpRequestException(
                $"Hue lights request returned HTTP {(int)response.StatusCode} ({response.StatusCode}): {errorBody}",
                null,
                response.StatusCode);
        }

        throw new InvalidOperationException("Hue lights request did not complete.");
    }

    private static bool IsTransientReadStatus(System.Net.HttpStatusCode statusCode)
    {
        return statusCode == System.Net.HttpStatusCode.RequestTimeout
            || (int)statusCode == 429
            || (int)statusCode >= 500;
    }

    public static Uri BuildLightsUri(string bridgeAddress, string apiKey)
    {
        return new Uri($"{NormalizeBridgeAddress(bridgeAddress)}/api/{Uri.EscapeDataString(apiKey)}/lights");
    }

    public static string NormalizeBridgeAddress(string bridgeAddress)
    {
        string value = bridgeAddress?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A Hue bridge address is required.", nameof(bridgeAddress));

        return value.Contains("://", StringComparison.Ordinal) ? value : $"http://{value}";
    }

    public static List<Device> CreateDevices(string bridgeAddress, IReadOnlyDictionary<string, HueLight> lights)
    {
        List<Device> devices =
        [
            new Device()
            {
                Id = "hue-bridge",
                DeviceInternalName = "hue-bridge",
                DeviceGivenName = "Hue Bridge",
                DeviceAgentId = "sarah",
                DevicePlatform = Platform,
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = ["hue-bridge", Device.HomeAutomationMainRoleBridge],
                DeviceAddresses = [bridgeAddress]
            }
        ];

        foreach ((string lightId, HueLight light) in lights)
        {
            if (light == null || string.IsNullOrWhiteSpace(lightId))
                continue;

            string internalName = string.Concat("hue-", lightId);
            List<string> roles = [Device.HomeAutomationRoleSwitch, Device.HomeAutomationMainRoleLight];
            List<string> capabilities = [];
            List<DeviceData> data =
            [
                new DeviceData()
                {
                    Name = "Switch",
                    Value = light.State?.On == true ? "on" : "off",
                    StandardDataType = DeviceData.DataTypeSwitch,
                    IsMainData = true
                }
            ];

            if (light.State?.Brightness is int brightness)
            {
                roles.Add(Device.HomeAutomationRoleDimmer);
                data.Add(new DeviceData()
                {
                    Name = "Brightness",
                    Value = Math.Round(brightness * 100M / 254M).ToString(CultureInfo.InvariantCulture),
                    StandardDataType = DeviceData.DataTypeGradient,
                    ValueUnit = "%"
                });
            }

            if (light.State?.Color != null)
            {
                roles.Add(Device.HomeAutomationRoleColorBound);
                capabilities.Add(Device.CapabilityColorXy);
                data.Add(new DeviceData()
                {
                    Name = "Color",
                    Value = CreateXyValue(light.State.Color),
                    StandardDataType = DeviceData.DataTypeColor
                });
            }

            if (light.State?.Hue is int hue && light.State.Saturation is int saturation)
            {
                if (!roles.Contains(Device.HomeAutomationRoleColorBound))
                    roles.Add(Device.HomeAutomationRoleColorBound);
                if (!capabilities.Contains(Device.CapabilityColorHs))
                    capabilities.Add(Device.CapabilityColorHs);
                data.Add(new DeviceData()
                {
                    Name = "Hue/Saturation",
                    Value = JsonSerializer.Serialize(new { hue, saturation }),
                    StandardDataType = DeviceData.DataTypeColor
                });
            }

            if (light.State?.ColorTemperature is int colorTemperature)
            {
                if (!roles.Contains(Device.HomeAutomationRoleColorBound))
                    roles.Add(Device.HomeAutomationRoleColorBound);
                if (!capabilities.Contains(Device.CapabilityColorTemperature))
                    capabilities.Add(Device.CapabilityColorTemperature);
                data.Add(new DeviceData()
                {
                    Name = "Color temperature",
                    Value = colorTemperature.ToString(CultureInfo.InvariantCulture),
                    StandardDataType = DeviceData.DataTypeColor
                });
            }

            if (light.Capabilities?.Control?.ColorGamut?.Length >= 3)
            {
                if (!roles.Contains(Device.HomeAutomationRoleColorBound))
                    roles.Add(Device.HomeAutomationRoleColorBound);
                if (!capabilities.Contains(Device.CapabilityColorXy))
                    capabilities.Add(Device.CapabilityColorXy);
            }

            if (light.Capabilities?.Control?.ColorTemperature is HueColorTemperatureRange range
                && range.Minimum > 0
                && range.Maximum >= range.Minimum)
            {
                if (!roles.Contains(Device.HomeAutomationRoleColorBound))
                    roles.Add(Device.HomeAutomationRoleColorBound);
                if (!capabilities.Contains(Device.CapabilityColorTemperature))
                    capabilities.Add(Device.CapabilityColorTemperature);
            }

            devices.Add(new Device()
            {
                Id = internalName,
                DeviceInternalName = internalName,
                DeviceGivenName = string.IsNullOrWhiteSpace(light.Name) ? internalName : light.Name,
                DeviceAgentId = "sarah",
                DevicePlatform = Platform,
                DeviceKind = Device.DeviceKindHomeAutomation,
                DeviceRoles = roles,
                DeviceCapabilities = capabilities,
                Datas = data,
                ConfigurationData = JsonSerializer.Serialize(light)
            });
        }

        return devices;
    }

    public static List<HueLightDevice> CreateRuntimeLights(
        string bridgeAddress,
        string apiKey,
        IReadOnlyDictionary<string, HueLight> lights,
        HueRuntimeService runtimeService)
    {
        if (lights == null)
            throw new ArgumentNullException(nameof(lights));
        if (runtimeService == null)
            throw new ArgumentNullException(nameof(runtimeService));

        return lights
            .Where(pair => pair.Value != null && !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => HueLightDevice.Create(
                string.Concat("hue-", pair.Key),
                bridgeAddress,
                apiKey,
                pair.Value,
                runtimeService.Protocol,
                CreateCommand))
            .ToList();
    }

    public static Dictionary<string, object> CreateCommand(SceneStep step, Device device)
    {
        return TryCreateCommand(step, device, out Dictionary<string, object> command) ? command : null;
    }

    public static HueBridgeDevice CreateRuntimeBridge(
        string bridgeAddress,
        string apiKey,
        IReadOnlyDictionary<string, HueLight> lights,
        HueRuntimeService runtimeService)
    {
        return (HueBridgeDevice)CreateRuntimeDevices(bridgeAddress, apiKey, lights, runtimeService)[0];
    }

    public static List<IDevice> CreateRuntimeDevices(
        string bridgeAddress,
        string apiKey,
        IReadOnlyDictionary<string, HueLight> lights,
        HueRuntimeService runtimeService)
    {
        List<HueLightDevice> runtimeLights = CreateRuntimeLights(bridgeAddress, apiKey, lights, runtimeService);
        HueBridgeDevice bridge = HueBridgeDevice.Create(bridgeAddress, runtimeLights);
        return [bridge, .. runtimeLights];
    }

    private static List<DeviceStateChangedMessage.DeviceStateValue> CreateStateValues(HueLight light)
    {
        List<DeviceStateChangedMessage.DeviceStateValue> values =
        [
            new DeviceStateChangedMessage.DeviceStateValue()
            {
                Name = "Switch",
                Value = light.State?.On == true ? "on" : "off",
                StandardDataType = DeviceData.DataTypeSwitch,
                IsMainData = true
            }
        ];

        if (light.State?.Brightness is int brightness)
        {
            values.Add(new DeviceStateChangedMessage.DeviceStateValue()
            {
                Name = "Brightness",
                Value = Math.Round(brightness * 100M / 254M).ToString(CultureInfo.InvariantCulture),
                StandardDataType = DeviceData.DataTypeGradient,
                ValueUnit = "%"
            });
        }

        if (light.State?.Color != null)
        {
            values.Add(new DeviceStateChangedMessage.DeviceStateValue()
            {
                Name = "Color",
                Value = CreateXyValue(light.State.Color),
                StandardDataType = DeviceData.DataTypeColor
            });
        }

        if (light.State?.Hue is int hue && light.State.Saturation is int saturation)
        {
            values.Add(new DeviceStateChangedMessage.DeviceStateValue()
            {
                Name = "Hue/Saturation",
                Value = JsonSerializer.Serialize(new { hue, saturation }),
                StandardDataType = DeviceData.DataTypeColor
            });
        }

        if (light.State?.ColorTemperature is int colorTemperature)
        {
            values.Add(new DeviceStateChangedMessage.DeviceStateValue()
            {
                Name = "Color temperature",
                Value = colorTemperature.ToString(CultureInfo.InvariantCulture),
                StandardDataType = DeviceData.DataTypeColor
            });
        }

        return values;
    }

    private static string CreateXyValue(HueColor color)
    {
        return JsonSerializer.Serialize(new { x = color.X, y = color.Y });
    }

    private static bool TryGetConfiguration(out string bridgeAddress, out string apiKey)
    {
        bridgeAddress = Environment.GetEnvironmentVariable("HUE_BRIDGE_ADDRESS")?.Trim();
        apiKey = Environment.GetEnvironmentVariable("HUE_API_KEY")?.Trim();
        return !string.IsNullOrWhiteSpace(bridgeAddress) && !string.IsNullOrWhiteSpace(apiKey);
    }

    private static int GetPollingIntervalSeconds()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("HUE_POLL_INTERVAL_SECONDS"), out int seconds)
            ? Math.Clamp(seconds, 5, 3600)
            : 30;
    }

}