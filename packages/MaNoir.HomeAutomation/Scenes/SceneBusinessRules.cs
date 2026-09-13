using Home.Common.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace MaNoir.HomeAutomation;

public sealed partial class SceneLogic
{
    public static string NormalizeSceneId(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            return null;

        return sceneId.Trim().ToLowerInvariant();
    }

    public static string NormalizeSceneGroupId(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return null;

        return groupId.Trim().ToLowerInvariant();
    }

    public static string NormalizeSceneImageCode(string imageCode)
    {
        if (string.IsNullOrWhiteSpace(imageCode))
            return null;

        string normalizedImageCode = imageCode.Trim().ToLowerInvariant();
        if (normalizedImageCode.Contains('/') || normalizedImageCode.Contains('\\'))
            return null;

        if (normalizedImageCode.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        return normalizedImageCode;
    }

    public static string GetSceneImageRelativePath(string sceneId, string imageCode)
    {
        string normalizedSceneId = NormalizeSceneId(sceneId);
        string normalizedImageCode = NormalizeSceneImageCode(imageCode);
        if (normalizedSceneId == null || normalizedImageCode == null)
            return null;

        return string.Concat("images/scenes/", normalizedSceneId, "/", normalizedImageCode, ".png");
    }

    public static string GetSceneImagePublicUrl(string sceneId, string imageCode)
    {
        string relativePath = GetSceneImageRelativePath(sceneId, imageCode);
        return relativePath == null ? null : string.Concat("/api/core/files/public/home-automation/", relativePath);
    }

    public static List<string> NormalizeSceneIds(IEnumerable<string> sceneIds)
    {
        List<string> normalizedSceneIds = new List<string>();
        if (sceneIds == null)
            return normalizedSceneIds;

        foreach (string sceneId in sceneIds)
        {
            string normalizedSceneId = NormalizeSceneId(sceneId);
            if (normalizedSceneId == null)
                continue;

            if (!normalizedSceneIds.Exists(current => string.Equals(current, normalizedSceneId, StringComparison.Ordinal)))
                normalizedSceneIds.Add(normalizedSceneId);
        }

        return normalizedSceneIds;
    }

    public static Scene PrepareScene(Scene scene)
    {
        if (scene == null)
            return null;

        scene.Id = NormalizeSceneId(scene.Id);
        scene.GroupId = NormalizeSceneGroupId(scene.GroupId);
        scene.RoomIds ??= new List<string>();
        scene.LocationZoneIds ??= new List<string>();
        scene.ActivationSteps ??= new List<SceneStep>();
        scene.DeactivationSteps ??= new List<SceneStep>();
        scene.DetectionCriteria ??= new List<SceneDetectionCriteria>();
        scene.Images ??= new Dictionary<string, string>();
        scene.InvocationStrings ??= new List<string>();

        if (scene.ActivationSteps.Any(step => !IsValidScriptStep(step))
            || scene.DeactivationSteps.Any(step => !IsValidScriptStep(step)))
        {
            return null;
        }

        return scene;
    }

    private static bool IsValidScriptStep(SceneStep step)
    {
        if (step == null || step.TargetKind != SceneStepTargetKind.Script)
            return true;

        return string.IsNullOrWhiteSpace(step.ScriptId) != string.IsNullOrWhiteSpace(step.ScriptContent);
    }

    public static SceneGroup PrepareSceneGroup(SceneGroup group)
    {
        if (group == null)
            return null;

        group.Id = NormalizeSceneGroupId(group.Id);
        group.ClearGroupSceneId = NormalizeSceneId(group.ClearGroupSceneId);
        group.CurrentActiveScenes = NormalizeSceneIds(group.CurrentActiveScenes);

        return group;
    }

    public static void ApplySceneUpdate(Scene existing, Scene incoming)
    {
        if (existing == null || incoming == null)
            return;

        existing.GroupId = incoming.GroupId;
        existing.RoomIds = incoming.RoomIds == null ? new List<string>() : new List<string>(incoming.RoomIds);
        existing.LocationZoneIds = incoming.LocationZoneIds == null ? new List<string>() : new List<string>(incoming.LocationZoneIds);
        existing.ActivationSteps = incoming.ActivationSteps == null ? new List<SceneStep>() : new List<SceneStep>(incoming.ActivationSteps);
        existing.DeactivationSteps = incoming.DeactivationSteps == null ? new List<SceneStep>() : new List<SceneStep>(incoming.DeactivationSteps);
        existing.PrivacyLevel = incoming.PrivacyLevel;
        existing.Label = incoming.Label;
        existing.IconUrl = string.IsNullOrWhiteSpace(incoming.IconUrl) ? existing.IconUrl : incoming.IconUrl;
        existing.BannerUrl = string.IsNullOrWhiteSpace(incoming.BannerUrl) ? existing.BannerUrl : incoming.BannerUrl;
        existing.MessageInStatus = incoming.MessageInStatus;
        existing.VisibleInRemote = incoming.VisibleInRemote;
        existing.OrderInGroup = incoming.OrderInGroup;
        existing.DetectionCriteria = incoming.DetectionCriteria == null ? new List<SceneDetectionCriteria>() : new List<SceneDetectionCriteria>(incoming.DetectionCriteria);
        if (incoming.Images != null && incoming.Images.Count > 0)
            existing.Images = new Dictionary<string, string>(incoming.Images);
        else
            existing.Images ??= new Dictionary<string, string>();
        existing.InvocationStrings = incoming.InvocationStrings == null ? new List<string>() : new List<string>(incoming.InvocationStrings);
    }

    public static void ApplySceneGroupUpdate(SceneGroup existing, SceneGroup incoming)
    {
        if (existing == null || incoming == null)
            return;

        existing.Label = incoming.Label;
        existing.PrivacyLevel = incoming.PrivacyLevel;
        existing.VisibleInRemote = incoming.VisibleInRemote;
        existing.Order = incoming.Order;
        existing.CurrentActiveScenes = incoming.CurrentActiveScenes == null ? new List<string>() : new List<string>(incoming.CurrentActiveScenes);
        existing.SceneIsExclusive = incoming.SceneIsExclusive;
        existing.ClearGroupSceneId = incoming.ClearGroupSceneId;
    }

    public static List<string> RemoveActiveScene(List<string> currentActiveScenes, string sceneId)
    {
        List<string> updatedActiveScenes = NormalizeSceneIds(currentActiveScenes);
        string normalizedSceneId = NormalizeSceneId(sceneId);
        if (normalizedSceneId == null)
            return updatedActiveScenes;

        return updatedActiveScenes.Where(current => !string.Equals(current, normalizedSceneId, StringComparison.InvariantCultureIgnoreCase)).ToList();
    }
}