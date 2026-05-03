using Home.Common.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Api;

[ApiController]
[Route("v1.0/system/mesh")]
public sealed class AutomationMeshController : ControllerBase
{
    [HttpGet("local/triggers")]
    public async Task<ActionResult<List<Trigger>>> GetTriggers(CancellationToken cancellationToken = default)
    {
        return Ok(await new TriggerLogic().GetAllAsync(cancellationToken));
    }

    [HttpPost("local/triggers")]
    public async Task<ActionResult<Trigger>> UpsertTrigger([FromBody] Trigger trigger, CancellationToken cancellationToken = default)
    {
        Trigger storedTrigger = await new TriggerLogic().UpsertAsync(trigger, cancellationToken);
        return storedTrigger == null ? BadRequest() : Ok(storedTrigger);
    }

    [HttpGet("local/triggers/{triggerId}")]
    public async Task<ActionResult<Trigger>> GetTrigger(string triggerId, CancellationToken cancellationToken = default)
    {
        Trigger trigger = await new TriggerLogic().GetByIdAsync(triggerId, cancellationToken);
        return trigger == null ? NotFound() : Ok(trigger);
    }

    [HttpGet("local/triggers/{triggerId}/settings")]
    public async Task<ActionResult<bool>> SetSettings(string triggerId, [FromQuery] DateTimeOffset? probableNextOccurrence = null, CancellationToken cancellationToken = default)
    {
        bool changed = await new TriggerLogic().SetSettingsAsync(triggerId, probableNextOccurrence, cancellationToken);
        return changed ? Ok(true) : NotFound();
    }

    [AllowAnonymous]
    [HttpGet("local/triggers/{triggerId}/raise")]
    [HttpPost("local/triggers/{triggerId}/raise")]
    public async Task<ActionResult<bool>> RaiseEvent(string triggerId, [FromQuery] string data = null, CancellationToken cancellationToken = default)
    {
        if (data == null && Request.ContentLength.GetValueOrDefault() > 0)
        {
            using StreamReader reader = new StreamReader(Request.Body);
            data = await reader.ReadToEndAsync(cancellationToken);
        }

        bool raised = await new TriggerLogic().RaiseAsync(triggerId, User?.Identity?.Name, data, cancellationToken);
        return raised ? Ok(true) : NotFound();
    }

    [HttpDelete("local/triggers/{triggerId}")]
    public async Task<ActionResult<bool>> DeleteTrigger(string triggerId, CancellationToken cancellationToken = default)
    {
        bool deleted = await new TriggerLogic().DeleteAsync(triggerId, cancellationToken);
        return deleted ? Ok(true) : NotFound();
    }
}