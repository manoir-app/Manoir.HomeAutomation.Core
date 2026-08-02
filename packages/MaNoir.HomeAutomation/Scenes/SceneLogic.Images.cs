using Home.Common.Model;
using MaNoir.Core.Files;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed partial class SceneLogic
{
    private const string SceneImageFileScope = "home-automation";
    private const string SceneIconImageCode = "icon";
    private const string SceneBannerImageCode = "banner";
    private const string GeneratedImageMarkerExtension = ".generated.txt";
    private static readonly HomeAutomationImageProcessor ImageProcessor = new HomeAutomationImageProcessor();
    private static readonly HomeAutomationFontCatalog FontCatalog = new HomeAutomationFontCatalog();

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

        if (string.Equals(normalizedImageCode, SceneIconImageCode, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(scene.IconUrl))
        {
            scene.IconUrl = null;
            hasChanged = true;
        }

        if (string.Equals(normalizedImageCode, SceneBannerImageCode, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(scene.BannerUrl))
        {
            scene.BannerUrl = null;
            hasChanged = true;
        }

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
            await ImageProcessor.SaveAsPngAsync(content, temporaryFile, cancellationToken);

            DeleteImageArtifacts(localFile);
            File.Move(temporaryFile, localFile, true);
            FileStorageHelper.UpdateStoredFileMetadata(localFile, FileStorageHelper.GetContentType(localFile));

            string publicUrl = GetSceneImagePublicUrl(scene.Id, normalizedImageCode);
            scene.Images ??= new Dictionary<string, string>();
            scene.Images[normalizedImageCode] = publicUrl;

            if (string.Equals(normalizedImageCode, SceneIconImageCode, StringComparison.Ordinal))
                scene.IconUrl = publicUrl;
            else if (string.Equals(normalizedImageCode, SceneBannerImageCode, StringComparison.Ordinal))
                scene.BannerUrl = publicUrl;

            await _sceneMongoOperations.SaveAsync(scene, cancellationToken);
        }
        catch
        {
            DeleteImageArtifacts(temporaryFile);
            throw;
        }

        return await _sceneMongoOperations.GetByIdAsync(scene.Id, cancellationToken);
    }

    private static void DeleteImageArtifacts(string localFile)
    {
        if (string.IsNullOrWhiteSpace(localFile))
            return;

        if (File.Exists(localFile))
            File.Delete(localFile);

        string generatedMarkerFile = GetGeneratedImageMarkerFile(localFile);
        if (File.Exists(generatedMarkerFile))
            File.Delete(generatedMarkerFile);

        FileStorageHelper.DeleteStoredFileMetadata(localFile);
    }

    private async Task EnsureGeneratedImagesAsync(Scene scene, CancellationToken cancellationToken)
    {
        if (scene == null)
            return;

        scene.Images ??= new Dictionary<string, string>();

        await EnsureGeneratedImageAsync(scene, SceneIconImageCode, scene.IconUrl, CreateSceneIconOptions(), GetSceneIconText(scene), "sans-bold", cancellationToken);
        await EnsureGeneratedImageAsync(scene, SceneBannerImageCode, scene.BannerUrl, CreateSceneBannerOptions(), GetSceneBannerText(scene), "sans-bold", cancellationToken);
    }

    private async Task EnsureGeneratedImageAsync(Scene scene, string imageCode, string explicitUrl, HomeAutomationTextRenderOptions options, string text, string fontId, CancellationToken cancellationToken)
    {
        string normalizedSceneId = NormalizeSceneId(scene.Id);
        string normalizedImageCode = NormalizeSceneImageCode(imageCode);
        if (normalizedSceneId == null || normalizedImageCode == null)
            return;

        string localFile = FileStorageHelper.GetPublicFilePath(SceneImageFileScope, GetSceneImageRelativePath(normalizedSceneId, normalizedImageCode));
        if (string.IsNullOrWhiteSpace(localFile))
            return;

        string publicUrl = GetSceneImagePublicUrl(normalizedSceneId, normalizedImageCode);
        string fingerprint = BuildGeneratedImageFingerprint(text, fontId, options);
        string markerFile = GetGeneratedImageMarkerFile(localFile);

        if (!ShouldGenerateImage(scene, normalizedImageCode, explicitUrl, localFile, markerFile, publicUrl, fingerprint))
            return;

        string temporaryFile = string.Concat(localFile, ".", Guid.NewGuid().ToString("N"), ".tmp");

        try
        {
            byte[] imageBytes = await ImageProcessor.CreateTextImageAsync(text, FontCatalog, fontId, options, cancellationToken: cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(localFile));
            await File.WriteAllBytesAsync(temporaryFile, imageBytes, cancellationToken);

            DeleteImageArtifacts(localFile);
            File.Move(temporaryFile, localFile, true);
            FileStorageHelper.UpdateStoredFileMetadata(localFile, FileStorageHelper.GetContentType(localFile));
            await File.WriteAllTextAsync(markerFile, fingerprint, Encoding.UTF8, cancellationToken);

            scene.Images[normalizedImageCode] = publicUrl;

            if (string.Equals(normalizedImageCode, SceneIconImageCode, StringComparison.Ordinal))
                scene.IconUrl = publicUrl;
            else if (string.Equals(normalizedImageCode, SceneBannerImageCode, StringComparison.Ordinal))
                scene.BannerUrl = publicUrl;
        }
        catch
        {
            DeleteImageArtifacts(temporaryFile);
            throw;
        }
    }

    private static bool ShouldGenerateImage(Scene scene, string imageCode, string explicitUrl, string localFile, string markerFile, string publicUrl, string expectedFingerprint)
    {
        bool hasManagedUrl = string.Equals(explicitUrl, publicUrl, StringComparison.Ordinal);
        bool hasImageEntry = scene.Images.TryGetValue(imageCode, out string imageUrl) && string.Equals(imageUrl, publicUrl, StringComparison.Ordinal);
        bool markerExists = File.Exists(markerFile);

        if (markerExists)
        {
            string currentFingerprint = File.ReadAllText(markerFile).Trim();
            return !File.Exists(localFile)
                || !string.Equals(currentFingerprint, expectedFingerprint, StringComparison.Ordinal)
                || !hasManagedUrl
                || !hasImageEntry;
        }

        if (File.Exists(localFile) || scene.Images.ContainsKey(imageCode) || !string.IsNullOrWhiteSpace(explicitUrl))
            return false;

        return true;
    }

    private static string BuildGeneratedImageFingerprint(string text, string fontId, HomeAutomationTextRenderOptions options)
    {
        return string.Join('|',
        [
            fontId ?? string.Empty,
            text ?? string.Empty,
            options.Width.ToString(),
            options.Height.ToString(),
            options.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            options.TextColor.Red.ToString(),
            options.TextColor.Green.ToString(),
            options.TextColor.Blue.ToString(),
            options.TextColor.Alpha.ToString(),
            options.BackgroundColor.Red.ToString(),
            options.BackgroundColor.Green.ToString(),
            options.BackgroundColor.Blue.ToString(),
            options.BackgroundColor.Alpha.ToString(),
            ((int)options.HorizontalAlignment).ToString(),
            ((int)options.VerticalAlignment).ToString(),
            options.IsAntialias.ToString()
        ]);
    }

    private static string GetGeneratedImageMarkerFile(string localFile)
    {
        return string.Concat(localFile, GeneratedImageMarkerExtension);
    }

    private static string GetSceneBannerText(Scene scene)
    {
        return GetSceneDisplayText(scene);
    }

    private static string GetSceneIconText(Scene scene)
    {
        string displayText = GetSceneDisplayText(scene);
        if (string.IsNullOrWhiteSpace(displayText))
            return "?";

        string[] tokens = displayText
            .Split(new[] { ' ', '-', '_', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            return displayText.Substring(0, 1).ToUpperInvariant();

        if (tokens.Length == 1)
            return tokens[0].Substring(0, Math.Min(2, tokens[0].Length)).ToUpperInvariant();

        return string.Concat(tokens.Take(2).Select(token => char.ToUpperInvariant(token[0])));
    }

    private static string GetSceneDisplayText(Scene scene)
    {
        if (!string.IsNullOrWhiteSpace(scene?.Label))
            return scene.Label.Trim();

        if (!string.IsNullOrWhiteSpace(scene?.Id))
            return scene.Id.Trim();

        return "Scene";
    }

    private static HomeAutomationTextRenderOptions CreateSceneIconOptions()
    {
        return new HomeAutomationTextRenderOptions()
        {
            Width = 256,
            Height = 256,
            FontSize = 96,
            TextColor = SKColors.White,
            BackgroundColor = new SKColor(0x23, 0x3D, 0x4D),
            HorizontalAlignment = HomeAutomationTextHorizontalAlignment.Center,
            VerticalAlignment = HomeAutomationTextVerticalAlignment.Middle,
        };
    }

    private static HomeAutomationTextRenderOptions CreateSceneBannerOptions()
    {
        return new HomeAutomationTextRenderOptions()
        {
            Width = 1280,
            Height = 640,
            FontSize = 118,
            TextColor = SKColors.White,
            BackgroundColor = new SKColor(0x17, 0x27, 0x33),
            HorizontalAlignment = HomeAutomationTextHorizontalAlignment.Center,
            VerticalAlignment = HomeAutomationTextVerticalAlignment.Middle,
        };
    }
}