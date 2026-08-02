using Home.Common;
using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.HomeAutomation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah;

public sealed class SceneExecutionService
{
    private readonly ILogger<SceneExecutionService> _logger;
    private readonly Zigbee2MqttCommandService _zigbee2MqttCommandService;
    private readonly ShellyGen1CommandService _shellyGen1CommandService;
    private readonly ShellyGen2CommandService _shellyGen2CommandService;

    public SceneExecutionService(ILogger<SceneExecutionService> logger, Zigbee2MqttCommandService zigbee2MqttCommandService = null, ShellyGen1CommandService shellyGen1CommandService = null, ShellyGen2CommandService shellyGen2CommandService = null)
    {
        _logger = logger;
        _zigbee2MqttCommandService = zigbee2MqttCommandService;
        _shellyGen1CommandService = shellyGen1CommandService;
        _shellyGen2CommandService = shellyGen2CommandService;
    }

    public MessageResponse Execute(string messageBody, bool deactivate)
    {
        ExecuteScenarioHomeAutomationMessage message;
        try
        {
            message = BaseMessage.ReadAs<ExecuteScenarioHomeAutomationMessage>(messageBody);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to read scene execution message.");
            return MessageResponse.GenericFail;
        }

        if (string.IsNullOrWhiteSpace(message?.SceneId))
            return MessageResponse.GenericFail;

        SceneLogic sceneLogic = new SceneLogic();
        Scene scene = sceneLogic.GetSceneByIdAsync(message.SceneId).GetAwaiter().GetResult();
        if (scene == null)
        {
            _logger.LogWarning("Cannot {Action} unknown scene {SceneId}.", deactivate ? "deactivate" : "execute", message.SceneId);
            return MessageResponse.GenericFail;
        }

        SceneGroup group = sceneLogic.GetGroupAsync(scene.GroupId).GetAwaiter().GetResult();
        if (group == null)
        {
            _logger.LogWarning("Cannot {Action} scene {SceneId} because group {GroupId} does not exist.", deactivate ? "deactivate" : "execute", scene.Id, scene.GroupId);
            return MessageResponse.GenericFail;
        }

        if (deactivate)
            DeactivateScene(sceneLogic, scene, group);
        else
            ActivateScene(sceneLogic, scene, group);

        _logger.LogInformation("{Action} scene {SceneId}.", deactivate ? "Deactivated" : "Executed", scene.Id);
        return MessageResponse.OK;
    }

    public MessageResponse TriggerForDeviceAction(string messageBody)
    {
        DeviceActionTriggeredMessage action;
        try
        {
            action = BaseMessage.ReadAs<DeviceActionTriggeredMessage>(messageBody);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to read device action trigger message.");
            return MessageResponse.GenericFail;
        }

        if (string.IsNullOrWhiteSpace(action?.DeviceId) || string.IsNullOrWhiteSpace(action.Action))
            return MessageResponse.GenericFail;

        List<Scene> matchingScenes = new SceneLogic().GetAllScenesAsync().GetAwaiter().GetResult()
            .Where(scene => (scene.DetectionCriteria ?? []).Any(criteria => MatchesDeviceAction(criteria?.DeviceActionTrigger, action)))
            .ToList();

        foreach (Scene scene in matchingScenes)
            Execute(JsonSerializer.Serialize(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage(scene.Id)), deactivate: false);

        _logger.LogInformation("Triggered {SceneCount} scene(s) from device action {Action} on device {DeviceId}.", matchingScenes.Count, action.Action, action.DeviceId);
        return MessageResponse.OK;
    }

    private static bool MatchesDeviceAction(SceneDetectionDeviceActionTrigger trigger, DeviceActionTriggeredMessage action)
    {
        if (trigger == null
            || !EqualsIgnoreCase(trigger.DeviceId, action.DeviceId)
            || !MatchesOptional(trigger.ActionKind, action.ActionKind)
            || !MatchesOptional(trigger.Action, action.Action)
            || !MatchesOptional(trigger.RawAction, action.RawAction))
        {
            return false;
        }

        foreach ((string name, string value) in trigger.RequiredAttributes ?? [])
        {
            KeyValuePair<string, string> actualAttribute = (action.Attributes ?? [])
                .FirstOrDefault(attribute => EqualsIgnoreCase(attribute.Key, name));
            if (string.IsNullOrWhiteSpace(actualAttribute.Key)
                || !EqualsIgnoreCase(value, actualAttribute.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesOptional(string expected, string actual)
    {
        return string.IsNullOrWhiteSpace(expected) || EqualsIgnoreCase(expected, actual);
    }

    private static bool EqualsIgnoreCase(string expected, string actual)
    {
        return string.Equals(expected?.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void ActivateScene(SceneLogic sceneLogic, Scene scene, SceneGroup group)
    {
        if (group.SceneIsExclusive)
        {
            foreach (string activeSceneId in group.CurrentActiveScenes ?? [])
            {
                if (string.Equals(activeSceneId, scene.Id, StringComparison.OrdinalIgnoreCase))
                    continue;

                Scene activeScene = sceneLogic.GetSceneAsync(group.Id, activeSceneId).GetAwaiter().GetResult();
                if (activeScene != null)
                    ExecuteSteps(activeScene, activeScene.DeactivationSteps);
            }
        }

        ExecuteSteps(scene, scene.ActivationSteps);

        List<string> activeScenes = group.SceneIsExclusive
            ? new List<string>()
            : new List<string>(group.CurrentActiveScenes ?? []);

        activeScenes.RemoveAll(activeSceneId => string.Equals(activeSceneId, scene.Id, StringComparison.OrdinalIgnoreCase));
        activeScenes.Add(scene.Id);
        sceneLogic.UpdateActiveScenesForGroupAsync(group.Id, activeScenes).GetAwaiter().GetResult();
    }

    private void DeactivateScene(SceneLogic sceneLogic, Scene scene, SceneGroup group)
    {
        ExecuteSteps(scene, scene.DeactivationSteps);
        sceneLogic.DeleteActiveSceneForGroupAsync(group.Id, scene.Id).GetAwaiter().GetResult();
    }

    private void ExecuteSteps(Scene scene, IEnumerable<SceneStep> steps)
    {
        foreach (SceneStep step in steps ?? [])
        {
            ExecuteStep(scene, step);
        }
    }

    private void ExecuteStep(Scene scene, SceneStep step)
    {
        if (step == null)
            return;

        if (step.Delay is TimeSpan delay && delay > TimeSpan.Zero)
            Task.Delay(delay).GetAwaiter().GetResult();

        if (step.TargetKind == SceneStepTargetKind.Agent && !string.IsNullOrWhiteSpace(step.Message))
        {
            NatsInterprocess.Push(step.Message, step.MessageBody ?? string.Empty);
            _logger.LogInformation("Executed agent step {Topic} for scene {SceneId}.", step.Message, scene.Id);
        }
        else if (step.TargetKind == SceneStepTargetKind.Device)
        {
            bool executed = _zigbee2MqttCommandService?.ExecuteAsync(step).GetAwaiter().GetResult() ?? false;
            if (!executed)
                executed = _shellyGen1CommandService?.ExecuteAsync(step).GetAwaiter().GetResult() ?? false;
            if (!executed)
                executed = _shellyGen2CommandService?.ExecuteAsync(step).GetAwaiter().GetResult() ?? false;
            if (!executed)
                _logger.LogWarning("Unable to execute device step for scene {SceneId} and device {DeviceId}.", scene.Id, step.TargetId);
        }
    }
}