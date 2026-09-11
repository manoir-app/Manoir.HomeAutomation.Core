using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Devices.Hue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah.Hue;

public sealed class HueDiscoverySource : IDeviceDiscoverySource
{
    private readonly HueRuntimeService _runtimeService;
    private readonly string _bridgeAddress;
    private readonly string _apiKey;

    public HueDiscoverySource(
        HueRuntimeService runtimeService,
        string bridgeAddress,
        string apiKey)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _bridgeAddress = bridgeAddress;
        _apiKey = apiKey;
    }

    public string SourceId => "hue-bridge";

    public async Task<IReadOnlyList<IDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, HueLight> lights = await _runtimeService.FetchLightsAsync(
            _bridgeAddress,
            _apiKey,
            cancellationToken);
        List<HueLightDevice> runtimeLights = HueRuntimeService.CreateRuntimeLights(
            _bridgeAddress,
            _apiKey,
            lights,
            _runtimeService);
        HueBridgeDevice bridge = HueBridgeDevice.Create(_bridgeAddress, runtimeLights);
        return [bridge, .. runtimeLights];
    }
}