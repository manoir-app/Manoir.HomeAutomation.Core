using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MaNoir.HomeAutomation.Api;

[ApiController]
[AllowAnonymous]
[Route("")]
public sealed class SystemController : ControllerBase
{
    [HttpGet("api/system")]
    public ActionResult GetRoot()
    {
        return Ok(new
        {
            service = "MaNoir.HomeAutomation.Api",
            pluginId = HomeAutomationPluginDescriptorProvider.PluginId,
        });
    }

    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return NoContent();
    }
}