using SkiaSharp;

namespace MaNoir.HomeAutomation;

public enum HomeAutomationImageFormat
{
    Unknown = 0,
    Png = 1,
    Jpeg = 2,
    Webp = 3,
    Gif = 4,
    Bmp = 5,
}

public sealed class HomeAutomationImageInfo
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required HomeAutomationImageFormat Format { get; init; }

    public required string MimeType { get; init; }
}

public sealed class HomeAutomationFontDefinition
{
    public required string Id { get; init; }

    public required string RelativePath { get; init; }
}

public enum HomeAutomationTextHorizontalAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
}

public enum HomeAutomationTextVerticalAlignment
{
    Top = 0,
    Middle = 1,
    Bottom = 2,
}

public sealed class HomeAutomationTextRenderOptions
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public float FontSize { get; init; } = 32;

    public float HorizontalPadding { get; init; } = 16;

    public float VerticalPadding { get; init; } = 16;

    public SKColor TextColor { get; init; } = SKColors.Black;

    public SKColor BackgroundColor { get; init; } = SKColors.Transparent;

    public HomeAutomationTextHorizontalAlignment HorizontalAlignment { get; init; } = HomeAutomationTextHorizontalAlignment.Center;

    public HomeAutomationTextVerticalAlignment VerticalAlignment { get; init; } = HomeAutomationTextVerticalAlignment.Middle;

    public bool IsAntialias { get; init; } = true;
}