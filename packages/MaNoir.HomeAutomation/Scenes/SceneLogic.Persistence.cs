using Home.Common;
using Home.Common.Messages;
using Home.Common.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed partial class SceneLogic
{
    public async Task<List<SceneGroup>> GetGroupsAsync(bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        List<SceneGroup> groups = await _sceneGroupMongoOperations.GetAllAsync(cancellationToken);
        if (!onlyRemote)
            return groups;

        return groups.Where(group => group.VisibleInRemote).ToList();
    }

    public async Task<SceneGroup> GetGroupAsync(string groupId, bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        string normalizedGroupId = NormalizeSceneGroupId(groupId);
        if (normalizedGroupId == null)
            return null;

        SceneGroup group = await _sceneGroupMongoOperations.GetByIdAsync(normalizedGroupId, cancellationToken);
        if (group == null)
            return null;

        if (onlyRemote && !group.VisibleInRemote)
            return null;

        return group;
    }

    public async Task<List<Scene>> GetAllScenesAsync(bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        List<Scene> scenes = await _sceneMongoOperations.GetAllAsync(cancellationToken);
        if (!onlyRemote)
            return scenes;

        return scenes.Where(scene => scene.VisibleInRemote).ToList();
    }

    public async Task<List<Scene>> GetScenesAsync(string groupId, bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        SceneGroup group = await GetGroupAsync(groupId, onlyRemote, cancellationToken);
        if (group == null)
            return new List<Scene>();

        List<Scene> scenes = await _sceneMongoOperations.GetByGroupIdAsync(group.Id, cancellationToken);
        if (!onlyRemote)
            return scenes;

        return scenes.Where(scene => scene.VisibleInRemote).ToList();
    }

    public async Task<Scene> GetSceneByIdAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        string normalizedSceneId = NormalizeSceneId(sceneId);
        if (normalizedSceneId == null)
            return null;

        return await _sceneMongoOperations.GetByIdAsync(normalizedSceneId, cancellationToken);
    }

    public async Task<Scene> GetSceneAsync(string groupId, string sceneId, bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        SceneGroup group = await GetGroupAsync(groupId, onlyRemote, cancellationToken);
        string normalizedSceneId = NormalizeSceneId(sceneId);
        if (group == null || normalizedSceneId == null)
            return null;

        Scene scene = await _sceneMongoOperations.GetByIdAsync(normalizedSceneId, cancellationToken);
        if (scene == null || !string.Equals(scene.GroupId, group.Id, StringComparison.Ordinal))
            return null;

        if (onlyRemote && !scene.VisibleInRemote)
            return null;

        return scene;
    }

    public async Task<SceneGroup> UpsertGroupAsync(SceneGroup group, CancellationToken cancellationToken = default)
    {
        SceneGroup preparedGroup = PrepareSceneGroup(group);
        if (preparedGroup == null)
            return null;

        if (string.IsNullOrWhiteSpace(preparedGroup.Id))
            preparedGroup.Id = Guid.NewGuid().ToString("D").ToLowerInvariant();

        SceneGroup existingGroup = await _sceneGroupMongoOperations.GetByIdAsync(preparedGroup.Id, cancellationToken);
        if (existingGroup == null)
        {
            await _sceneGroupMongoOperations.InsertAsync(preparedGroup, cancellationToken);
        }
        else
        {
            ApplySceneGroupUpdate(existingGroup, preparedGroup);
            await _sceneGroupMongoOperations.SaveAsync(existingGroup, cancellationToken);
        }

        SceneGroup storedGroup = await _sceneGroupMongoOperations.GetByIdAsync(preparedGroup.Id, cancellationToken);
        PublishScenarioContentChangedBestEffort(storedGroup?.Id, null);
        return storedGroup;
    }

    public async Task<Scene> UpsertSceneAsync(Scene scene, CancellationToken cancellationToken = default)
    {
        Scene preparedScene = PrepareScene(scene);
        if (preparedScene == null)
            return null;

        if (string.IsNullOrWhiteSpace(preparedScene.Id))
            preparedScene.Id = Guid.NewGuid().ToString("D").ToLowerInvariant();

        Scene existingScene = await _sceneMongoOperations.GetByIdAsync(preparedScene.Id, cancellationToken);
        if (existingScene == null)
        {
            await _sceneMongoOperations.InsertAsync(preparedScene, cancellationToken);
        }
        else
        {
            ApplySceneUpdate(existingScene, preparedScene);
            await _sceneMongoOperations.SaveAsync(existingScene, cancellationToken);
        }

        Scene storedScene = await _sceneMongoOperations.GetByIdAsync(preparedScene.Id, cancellationToken);
        await EnsureGeneratedImagesAsync(storedScene, cancellationToken);
        await _sceneMongoOperations.SaveAsync(storedScene, cancellationToken);
        storedScene = await _sceneMongoOperations.GetByIdAsync(preparedScene.Id, cancellationToken);
        PublishScenarioContentChangedBestEffort(storedScene?.GroupId, storedScene?.Id);
        return storedScene;
    }

    public async Task<SceneGroup> UpdateActiveScenesForGroupAsync(string groupId, IEnumerable<string> sceneIds, bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        SceneGroup group = await GetGroupAsync(groupId, onlyRemote, cancellationToken);
        if (group == null)
            return null;

        List<string> requestedSceneIds = NormalizeSceneIds(sceneIds);

        if (onlyRemote)
        {
            foreach (string requestedSceneId in requestedSceneIds.ToList())
            {
                if (group.CurrentActiveScenes != null && group.CurrentActiveScenes.Exists(current => string.Equals(current, requestedSceneId, StringComparison.InvariantCultureIgnoreCase)))
                    continue;

                Scene remoteVisibleScene = await GetSceneAsync(group.Id, requestedSceneId, true, cancellationToken);
                if (remoteVisibleScene == null)
                    requestedSceneIds.Remove(requestedSceneId);
            }
        }

        group.CurrentActiveScenes = requestedSceneIds;
        await _sceneGroupMongoOperations.SaveAsync(group, cancellationToken);
        return await _sceneGroupMongoOperations.GetByIdAsync(group.Id, cancellationToken);
    }

    public async Task<SceneGroup> DeleteActiveSceneForGroupAsync(string groupId, string sceneId, bool onlyRemote = false, CancellationToken cancellationToken = default)
    {
        SceneGroup group = await GetGroupAsync(groupId, onlyRemote, cancellationToken);
        Scene scene = await GetSceneAsync(groupId, sceneId, onlyRemote, cancellationToken);
        if (group == null || scene == null)
            return group;

        group.CurrentActiveScenes = RemoveActiveScene(group.CurrentActiveScenes, scene.Id);
        await _sceneGroupMongoOperations.SaveAsync(group, cancellationToken);
        return await _sceneGroupMongoOperations.GetByIdAsync(group.Id, cancellationToken);
    }

    public async Task<bool> DeleteSceneAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        Scene scene = await GetSceneByIdAsync(sceneId, cancellationToken);
        if (scene == null)
            return true;

        SceneGroup group = scene.GroupId == null ? null : await _sceneGroupMongoOperations.GetByIdAsync(scene.GroupId, cancellationToken);
        if (group != null && group.CurrentActiveScenes != null && group.CurrentActiveScenes.Exists(current => string.Equals(current, scene.Id, StringComparison.InvariantCultureIgnoreCase)))
        {
            group.CurrentActiveScenes = RemoveActiveScene(group.CurrentActiveScenes, scene.Id);
            await _sceneGroupMongoOperations.SaveAsync(group, cancellationToken);
        }

        await _sceneMongoOperations.DeleteAsync(scene.Id, cancellationToken);
        PublishScenarioContentChangedBestEffort(group?.Id, scene.Id);
        return true;
    }

    public async Task<bool> DeleteGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        SceneGroup group = await GetGroupAsync(groupId, false, cancellationToken);
        if (group == null)
            return true;

        await _sceneMongoOperations.DeleteByGroupIdAsync(group.Id, cancellationToken);
        await _sceneGroupMongoOperations.DeleteAsync(group.Id, cancellationToken);
        PublishScenarioContentChangedBestEffort(group.Id, null);
        return true;
    }

    public async Task<bool> ExecuteSceneAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        Scene scene = await GetSceneByIdAsync(sceneId, cancellationToken);
        if (scene == null)
            return false;

        PublishExecuteSceneBestEffort(scene.Id);
        return true;
    }

    private static void PublishScenarioContentChangedBestEffort(string sceneGroupId, string sceneId)
    {
        try
        {
            NatsInterprocess.Push(new ScenarioContentChangedMessage()
            {
                SceneGroupId = sceneGroupId,
                SceneId = sceneId
            });
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to publish scene content change for {sceneGroupId}/{sceneId}: {exception.Message}");
        }
    }

    private static void PublishExecuteSceneBestEffort(string sceneId)
    {
        try
        {
            NatsInterprocess.Push(ExecuteScenarioHomeAutomationMessage.GetExecuteMessage(sceneId));
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to publish scene execute message for {sceneId}: {exception.Message}");
        }
    }
}