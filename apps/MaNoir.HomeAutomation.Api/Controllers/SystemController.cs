using Microsoft.AspNetCore.Mvc;

namespace MaNoir.HomeAutomation.Api;

[ApiController]
[Route("")]
public sealed class SystemController : ControllerBase
{
    [HttpGet]
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