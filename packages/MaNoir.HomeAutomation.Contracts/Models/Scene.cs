using MaNoir.Core.Contracts.Models.Mesh;
using System;
using System.Collections.Generic;

namespace Home.Common.Model;

public class Scene
{
    public Scene()
    {
        RoomIds = new List<string>();
        LocationZoneIds = new List<string>();
        ActivationSteps = new List<SceneStep>();
        DeactivationSteps = new List<SceneStep>();
        DetectionCriteria = new List<SceneDetectionCriteria>();
        Images = new Dictionary<string, string>();
        InvocationStrings = new List<string>();
    }

    public string Id { get; set; }

    public string GroupId { get; set; }

    public List<string> RoomIds { get; set; }
    public List<string> LocationZoneIds { get; set; }
    public List<SceneStep> ActivationSteps { get; set; }
    public List<SceneStep> DeactivationSteps { get; set; }

    public AutomationMeshPrivacyMode? PrivacyLevel { get; set; }

    public string Label { get; set; }
    public string IconUrl { get; set; }
    public string BannerUrl { get; set; }
    public string MessageInStatus { get; set; }

    public bool VisibleInRemote { get; set; }

    public int? OrderInGroup { get; set; }

    public List<SceneDetectionCriteria> DetectionCriteria { get; set; }

    public Dictionary<string, string> Images { get; set; }

    public List<string> InvocationStrings { get; set; }
}

public class SceneGroup
{
    public SceneGroup()
    {
        CurrentActiveScenes = new List<string>();
    }

    public string Id { get; set; }
    public string Label { get; set; }

    public AutomationMeshPrivacyMode? PrivacyLevel { get; set; }

    public bool VisibleInRemote { get; set; }

    public int? Order { get; set; }

    public List<string> CurrentActiveScenes { get; set; }

    public bool SceneIsExclusive { get; set; }

    public string ClearGroupSceneId { get; set; }
}

public enum SceneStepTargetKind
{
    Agent = 0,
    Device = 1,
}

public class SceneStep
{
    public SceneStepTargetKind TargetKind { get; set; }
    public string TargetId { get; set; }
    public string Message { get; set; }
    public string MessageBody { get; set; }
    public TimeSpan? Delay { get; set; }
}

public class SceneDetectionCriteria
{
    public SceneDetectionAgentCheck AgentCheck { get; set; }
    public SceneDetectionDeviceCheck DeviceCheck { get; set; }
}

public class SceneDetectionAgentCheck
{
    public string MessageTopic { get; set; }
    public string MessageBody { get; set; }
    public string RegexToMatchInResponse { get; set; }
}

public class SceneDetectionDeviceCheck
{
    public string DeviceId { get; set; }
    public string DeviceRole { get; set; }
    public string ElementName { get; set; }
    public string Operator { get; set; }
    public string Value { get; set; }
}