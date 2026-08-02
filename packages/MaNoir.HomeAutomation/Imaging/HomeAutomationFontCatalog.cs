using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace MaNoir.HomeAutomation;

public sealed class HomeAutomationFontCatalog : IDisposable
{
    private readonly Dictionary<string, HomeAutomationFontDefinition> _definitions;
    private readonly Dictionary<string, SKTypeface> _typefaces;

    public HomeAutomationFontCatalog(string rootFolder = null, IEnumerable<HomeAutomationFontDefinition> definitions = null)
    {
        RootFolder = string.IsNullOrWhiteSpace(rootFolder) ? GetDefaultRootFolder() : rootFolder.Trim();
        _definitions = new Dictionary<string, HomeAutomationFontDefinition>(StringComparer.OrdinalIgnoreCase);
        _typefaces = new Dictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);

        foreach (HomeAutomationFontDefinition definition in definitions ?? GetDefaultDefinitions())
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.RelativePath))
                continue;

            _definitions[definition.Id.Trim()] = definition;
        }
    }

    public string RootFolder { get; }

    public static string GetDefaultRootFolder()
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
    }

    public static IReadOnlyList<HomeAutomationFontDefinition> GetDefaultDefinitions()
    {
        return
        [
            new HomeAutomationFontDefinition() { Id = "sans-regular", RelativePath = "NotoSans-Regular.ttf" },
            new HomeAutomationFontDefinition() { Id = "sans-bold", RelativePath = "NotoSans-Bold.ttf" },
            new HomeAutomationFontDefinition() { Id = "serif-regular", RelativePath = "NotoSerif-Regular.ttf" },
            new HomeAutomationFontDefinition() { Id = "mono-regular", RelativePath = "NotoSansMono-Regular.ttf" },
        ];
    }

    public IReadOnlyCollection<string> GetRegisteredFontIds()
    {
        return _definitions.Keys;
    }

    public bool TryGetFontPath(string fontId, out string fontPath)
    {
        fontPath = null;
        if (string.IsNullOrWhiteSpace(fontId))
            return false;

        if (!_definitions.TryGetValue(fontId.Trim(), out HomeAutomationFontDefinition definition))
            return false;

        fontPath = Path.Combine(RootFolder, definition.RelativePath);
        return true;
    }

    public string GetFontPath(string fontId)
    {
        if (!TryGetFontPath(fontId, out string fontPath))
            throw new KeyNotFoundException($"Unknown font id '{fontId}'.");

        return fontPath;
    }

    public SKTypeface GetTypeface(string fontId)
    {
        if (string.IsNullOrWhiteSpace(fontId))
            throw new ArgumentException("The font id is required.", nameof(fontId));

        string normalizedId = fontId.Trim();
        if (_typefaces.TryGetValue(normalizedId, out SKTypeface cachedTypeface))
            return cachedTypeface;

        string fontPath = GetFontPath(normalizedId);
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException(
                $"Font '{normalizedId}' was not found at '{fontPath}'. Add the font file to the executable Assets/Fonts folder and ensure it is copied to output.",
                fontPath);
        }

        SKTypeface typeface = SKTypeface.FromFile(fontPath);
        if (typeface == null)
            throw new InvalidOperationException($"Unable to load font '{normalizedId}' from '{fontPath}'.");

        _typefaces[normalizedId] = typeface;
        return typeface;
    }

    public void Dispose()
    {
        foreach (SKTypeface typeface in _typefaces.Values)
        {
            typeface.Dispose();
        }

        _typefaces.Clear();
    }
}