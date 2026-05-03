using Home.Common.Model;
using MaNoir.Core.Files;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed partial class SceneLogic
{
    private const string SceneImageFileScope = "home-automation";

    public async Task<Scene> DeleteImageAsync(string sceneId, string imageCode, CancellationToken cancellationToken = default)
    {
        string normalizedSceneId = NormalizeSceneId(sceneId);
        string normalizedImageCode = NormalizeSceneImageCode(imageCode);
        if (normalizedSceneId == null || normalizedImageCode == null)
            return null;

        Scene scene = await _sceneMongoOperations.GetByIdAsync(normalizedSceneId, cancellationToken);
        if (scene == null)
            return null;

        string relativePath = GetSceneImageRelativePath(scene.Id, normalizedImageCode);
        string localFile = FileStorageHelper.GetPublicFilePath(SceneImageFileScope, relativePath);
        DeleteImageArtifacts(localFile);

        bool hasChanged = false;
        scene.Images ??= new Dictionary<string, string>();
        if (scene.Images.Remove(normalizedImageCode))
            hasChanged = true;

        if (hasChanged)
            await _sceneMongoOperations.SaveAsync(scene, cancellationToken);

        return await _sceneMongoOperations.GetByIdAsync(scene.Id, cancellationToken);
    }

    public async Task<Scene> UpsertImageAsync(string sceneId, string imageCode, Stream content, CancellationToken cancellationToken = default)
    {
        string normalizedSceneId = NormalizeSceneId(sceneId);
        string normalizedImageCode = NormalizeSceneImageCode(imageCode);
        if (normalizedSceneId == null || normalizedImageCode == null || content == null)
            return null;

        Scene scene = await _sceneMongoOperations.GetByIdAsync(normalizedSceneId, cancellationToken);
        if (scene == null)
            return null;

        string relativePath = GetSceneImageRelativePath(scene.Id, normalizedImageCode);
        string localFile = FileStorageHelper.GetPublicFilePath(SceneImageFileScope, relativePath);
        if (localFile == null)
            return null;

        string temporaryFile = string.Concat(localFile, ".", Guid.NewGuid().ToString("N"), ".tmp");
        try
        {
            await SaveImageAsPngAsync(content, temporaryFile, cancellationToken);

            DeleteImageArtifacts(localFile);
            File.Move(temporaryFile, localFile, true);
            FileStorageHelper.UpdateStoredFileMetadata(localFile, FileStorageHelper.GetContentType(localFile));

            scene.Images ??= new Dictionary<string, string>();
            scene.Images[normalizedImageCode] = GetSceneImagePublicUrl(scene.Id, normalizedImageCode);
            await _sceneMongoOperations.SaveAsync(scene, cancellationToken);
        }
        catch
        {
            DeleteImageArtifacts(temporaryFile);
            throw;
        }

        return await _sceneMongoOperations.GetByIdAsync(scene.Id, cancellationToken);
    }

    private static async Task SaveImageAsPngAsync(Stream content, string targetFile, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
            throw new InvalidDataException("The image payload is empty.");

        buffer.Position = 0;
        var format = Image.DetectFormat(buffer);
        if (format?.DefaultMimeType == null || !format.DefaultMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid file format.");

        Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
        if (string.Equals(format.DefaultMimeType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            buffer.Position = 0;
            await using FileStream targetStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);
            await buffer.CopyToAsync(targetStream, cancellationToken);
            return;
        }

        buffer.Position = 0;
        using Image image = Image.Load(buffer);
        image.SaveAsPng(targetFile);
    }

    private static void DeleteImageArtifacts(string localFile)
    {
        if (string.IsNullOrWhiteSpace(localFile))
            return;

        if (File.Exists(localFile))
            File.Delete(localFile);

        FileStorageHelper.DeleteStoredFileMetadata(localFile);
    }
}