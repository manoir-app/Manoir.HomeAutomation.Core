using Home.Common.Model;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Api;

[ApiController]
[Route("v1.0/devices")]
public sealed class DevicesController : ControllerBase
{
    public sealed class AppDevice
    {
    }

    [HttpGet("discovered")]
    public async Task<ActionResult<List<DiscoveredDevice>>> GetDiscoveredDevices([FromQuery] string kind = null, [FromQuery] string agent = null, CancellationToken cancellationToken = default)
    {
        return Ok(await new DiscoveredDeviceLogic().GetAllAsync(kind, agent, cancellationToken: cancellationToken));
    }

    [HttpGet("discovered/clearolds")]
    public async Task<ActionResult<bool>> ClearOldDiscoveredDevices(CancellationToken cancellationToken = default)
    {
        await new DiscoveredDeviceLogic().ClearOldAsync(cancellationToken: cancellationToken);
        return Ok(true);
    }

    [HttpGet("discovery/enable")]
    public IActionResult SetDiscoveryModeViaGet()
    {
        DiscoveredDeviceLogic.EnableDiscoveryMode();
        return NoContent();
    }

    [HttpPost("discovery/enabled")]
    public IActionResult SetDiscoveryMode()
    {
        DiscoveredDeviceLogic.EnableDiscoveryMode();
        return NoContent();
    }

    [HttpGet("discovery/enabled")]
    public ActionResult<bool> GetDiscoveryMode()
    {
        return Ok(DiscoveredDeviceLogic.IsDiscoveryEnabled());
    }

    [HttpPut("discovery/appdevice/{deviceId}/validate")]
    public async Task<ActionResult<Device>> CreateDeviceForApp(string deviceId, [FromQuery] string deviceName, CancellationToken cancellationToken = default)
    {
        Device device = await new DiscoveredDeviceLogic().ValidateAppDeviceAsync(deviceId, deviceName, cancellationToken);
        return device == null ? NotFound() : Ok(device);
    }

    [HttpGet("discovery/appdevice/{deviceId}/check")]
    public async Task<ActionResult<bool>> CheckIfAssociated(string deviceId, CancellationToken cancellationToken = default)
    {
        return Ok(await new DiscoveredDeviceLogic().CheckIfAssociatedAsync(deviceId, cancellationToken));
    }

    [HttpPost("discovery/appdevice")]
    public async Task<ActionResult<DiscoveredDevice>> CreateForAppDevice([FromBody] AppDevice device, CancellationToken cancellationToken = default)
    {
        return Ok(await new DiscoveredDeviceLogic().CreateForAppDeviceAsync(cancellationToken));
    }

    [HttpPost("discovery/multiple")]
    public async Task<ActionResult<bool>> UpsertDiscoveredDevices([FromBody] List<DiscoveredDevice> devices, CancellationToken cancellationToken = default)
    {
        return Ok(await new DiscoveredDeviceLogic().UpsertManyAsync(devices, cancellationToken));
    }

    [HttpPost("discovery")]
    public async Task<ActionResult<bool>> UpsertDiscoveredDevice([FromBody] DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        return Ok(await new DiscoveredDeviceLogic().UpsertAsync(device, cancellationToken));
    }

    [HttpGet("discovery/agent/{agentId}/devices")]
    public async Task<ActionResult<List<DiscoveredDevice>>> GetDiscoveredDevicesForAgent(string agentId, CancellationToken cancellationToken = default)
    {
        return Ok(await new DiscoveredDeviceLogic().GetForAgentAsync(agentId, cancellationToken: cancellationToken));
    }
}