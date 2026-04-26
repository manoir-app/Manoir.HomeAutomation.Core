using System.Collections.Generic;
using MaNoir.HomeAutomation.Contracts.Models;

namespace MaNoir.HomeAutomation;

public static class HomeAutomationSupportedProtocols
{
    public static IReadOnlyList<HomeAutomationProtocolKind> All { get; } =
    [
        HomeAutomationProtocolKind.Shelly,
        HomeAutomationProtocolKind.PhilipsHue,
        HomeAutomationProtocolKind.Zigbee2Mqtt,
        HomeAutomationProtocolKind.Wled,
    ];
}
