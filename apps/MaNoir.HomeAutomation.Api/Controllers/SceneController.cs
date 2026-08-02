using Home.Common.Model;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Api;

[ApiController]
[Route("v1.0/homeautomation/scenes")]
public sealed class SceneController : ControllerBase
{
    [HttpGet("groups")]
    public async Task<ActionResult<List<SceneGroup>>> GetGroups([FromQuery] bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        return Ok(await new SceneLogic().GetGroupsAsync(onlyRemote, cancellationToken));
    }

    [HttpGet("groups/{id}")]
    public async Task<ActionResult<SceneGroup>> GetGroup(string id, [FromQuery] bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        SceneGroup group = await new SceneLogic().GetGroupAsync(id, onlyRemote, cancellationToken);
        return group == null ? NotFound() : Ok(group);
    }

    [HttpPost("groups/{id}/activeScenes")]
    public async Task<ActionResult<SceneGroup>> UpdateActiveSceneForGroup(string id, [FromBody] List<string> scenes, [FromQuery] bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        SceneGroup group = await new SceneLogic().UpdateActiveScenesForGroupAsync(id, scenes, onlyRemote, cancellationToken);
        return group == null ? NotFound() : Ok(group);
    }

    [HttpDelete("groups/{id}/activeScenes")]
    public async Task<ActionResult<SceneGroup>> DeleteActiveSceneForGroup(string id, [FromQuery] string scene, [FromQuery] bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        SceneGroup group = await new SceneLogic().DeleteActiveSceneForGroupAsync(id, scene, onlyRemote, cancellationToken);
        return group == null ? NotFound() : Ok(group);
    }

    [HttpDelete("groups/{groupId}")]
    public async Task<ActionResult<bool>> DeleteGroup(string groupId, CancellationToken cancellationToken = default)
    {
        await new SceneLogic().DeleteGroupAsync(groupId, cancellationToken);
        return Ok(true);
    }

    [HttpPost("groups")]
    public async Task<ActionResult<SceneGroup>> UpsertGroup([FromBody] SceneGroup group, CancellationToken cancellationToken = default)
    {
        SceneGroup storedGroup = await new SceneLogic().UpsertGroupAsync(group, cancellationToken);
        return storedGroup == null ? BadRequest() : Ok(storedGroup);
    }

    [HttpGet("groups/{groupId}/scenes")]
    public async Task<ActionResult<List<Scene>>> GetScenesByGroup(string groupId, [FromQuery] bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        return Ok(await new SceneLogic().GetScenesAsync(groupId, onlyRemote, cancellationToken));
    }

    [HttpGet("scenes")]
    public async Task<ActionResult<List<Scene>>> GetScenes([FromQuery] bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        return Ok(await new SceneLogic().GetAllScenesAsync(onlyRemote, cancellationToken));
    }

    [HttpGet("execute/{id}")]
    public async Task<ActionResult<bool>> ExecuteScene(string id, CancellationToken cancellationToken = default)
    {
        bool executed = await new SceneLogic().ExecuteSceneAsync(id, cancellationToken);
        return executed ? Ok(true) : NotFound();
    }

    [HttpGet("scenes/{id}")]
    public async Task<ActionResult<Scene>> GetScene(string id, CancellationToken cancellationToken = default)
    {
        Scene scene = await new SceneLogic().GetSceneByIdAsync(id, cancellationToken);
        return scene == null ? NotFound() : Ok(scene);
    }

    [HttpPost("scenes")]
    public async Task<ActionResult<Scene>> UpsertScene([FromBody] Scene scene, CancellationToken cancellationToken = default)
    {
        Scene storedScene = await new SceneLogic().UpsertSceneAsync(scene, cancellationToken);
        return storedScene == null ? BadRequest() : Ok(storedScene);
    }

    [HttpDelete("scene/{id}")]
    [HttpDelete("scenes/{id}")]
    public async Task<ActionResult<bool>> DeleteScene(string id, CancellationToken cancellationToken = default)
    {
        await new SceneLogic().DeleteSceneAsync(id, cancellationToken);
        return Ok(true);
    }

    [HttpDelete("scene/{id}/images/{imagecode}")]
    [HttpDelete("scenes/{id}/images/{imagecode}")]
    public async Task<ActionResult<Scene>> DeleteImage(string id, string imagecode, CancellationToken cancellationToken = default)
    {
        Scene scene = await new SceneLogic().DeleteImageAsync(id, imagecode, cancellationToken);
        return scene == null ? NotFound() : Ok(scene);
    }

    [HttpPost("scene/{id}/images/{imagecode}")]
    [HttpPost("scenes/{id}/images/{imagecode}")]
    public async Task<ActionResult<Scene>> UpsertImage(string id, string imagecode, CancellationToken cancellationToken = default)
    {
        try
        {
            Scene scene = await new SceneLogic().UpsertImageAsync(id, imagecode, Request.Body, cancellationToken);
            return scene == null ? NotFound() : Ok(scene);
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}