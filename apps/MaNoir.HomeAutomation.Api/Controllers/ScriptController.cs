using Home.Common.Model;
using MaNoir.HomeAutomation.Scripting;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Api;

[ApiController]
[Route("v1.0/homeautomation/scripts")]
public sealed class ScriptController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ScriptDefinition>>> GetScripts(CancellationToken cancellationToken = default)
    {
        return Ok(await new ScriptLogic().GetAllAsync(cancellationToken));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ScriptDefinition>> GetScript(string id, CancellationToken cancellationToken = default)
    {
        ScriptDefinition script = await new ScriptLogic().GetByIdAsync(id, cancellationToken);
        return script == null ? NotFound() : Ok(script);
    }

    [HttpPost]
    public async Task<ActionResult<ScriptDefinition>> UpsertScript([FromBody] ScriptDefinition script, CancellationToken cancellationToken = default)
    {
        ScriptDefinition storedScript = await new ScriptLogic().UpsertAsync(script, cancellationToken);
        return storedScript == null ? BadRequest() : Ok(storedScript);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<bool>> DeleteScript(string id, CancellationToken cancellationToken = default)
    {
        await new ScriptLogic().DeleteAsync(id, cancellationToken);
        return Ok(true);
    }
}