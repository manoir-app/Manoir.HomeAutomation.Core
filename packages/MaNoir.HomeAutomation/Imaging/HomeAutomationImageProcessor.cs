using SkiaSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed class HomeAutomationImageProcessor
{
    public async Task<HomeAutomationImageInfo> ReadInfoAsync(Stream content, CancellationToken cancellationToken = default)
    {
        using MemoryStream buffer = await ReadBufferAsync(content, cancellationToken);
        using SKCodec codec = CreateCodec(buffer);

        return new HomeAutomationImageInfo()
        {
            Width = codec.Info.Width,
            Height = codec.Info.Height,
            Format = FromEncodedFormat(codec.EncodedFormat),
            MimeType = GetMimeType(codec.EncodedFormat)
        };
    }

    public async Task<byte[]> ConvertAsync(Stream content, HomeAutomationImageFormat format, int quality = 90, CancellationToken cancellationToken = default)
    {
        using MemoryStream buffer = await ReadBufferAsync(content, cancellationToken);
        using SKBitmap bitmap = DecodeBitmap(buffer);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = EncodeImage(image, format, quality);
        return encoded.ToArray();
    }

    public async Task<byte[]> ResizeAsync(Stream content, int maxWidth, int maxHeight, HomeAutomationImageFormat format = HomeAutomationImageFormat.Png, int quality = 90, CancellationToken cancellationToken = default)
    {
        if (maxWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWidth));

        if (maxHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHeight));

        using MemoryStream buffer = await ReadBufferAsync(content, cancellationToken);
        using SKBitmap bitmap = DecodeBitmap(buffer);

        (int width, int height) = FitWithinBounds(bitmap.Width, bitmap.Height, maxWidth, maxHeight);
        using SKBitmap outputBitmap = width == bitmap.Width && height == bitmap.Height
            ? bitmap.Copy()
            : ResizeBitmap(bitmap, width, height);

        using SKImage image = SKImage.FromBitmap(outputBitmap);
        using SKData encoded = EncodeImage(image, format, quality);
        return encoded.ToArray();
    }

    public async Task<byte[]> ComposeOverlayAsync(Stream backgroundContent, Stream overlayContent, int left, int top, HomeAutomationImageFormat format = HomeAutomationImageFormat.Png, int quality = 90, CancellationToken cancellationToken = default)
    {
        using MemoryStream backgroundBuffer = await ReadBufferAsync(backgroundContent, cancellationToken);
        using MemoryStream overlayBuffer = await ReadBufferAsync(overlayContent, cancellationToken);
        using SKBitmap backgroundBitmap = DecodeBitmap(backgroundBuffer);
        using SKBitmap overlayBitmap = DecodeBitmap(overlayBuffer);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(backgroundBitmap.Width, backgroundBitmap.Height));

        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawBitmap(backgroundBitmap, 0, 0);
        surface.Canvas.DrawBitmap(overlayBitmap, left, top);

        using SKImage image = surface.Snapshot();
        using SKData encoded = EncodeImage(image, format, quality);
        return encoded.ToArray();
    }

    public Task<byte[]> CreateTextImageAsync(string text, SKTypeface typeface, HomeAutomationTextRenderOptions options, HomeAutomationImageFormat format = HomeAutomationImageFormat.Png, int quality = 90, CancellationToken cancellationToken = default)
    {
        if (typeface == null)
            throw new ArgumentNullException(nameof(typeface));

        ValidateTextRenderOptions(options);
        cancellationToken.ThrowIfCancellationRequested();

        using SKSurface surface = SKSurface.Create(new SKImageInfo(options.Width, options.Height));
        surface.Canvas.Clear(options.BackgroundColor);

        using SKPaint paint = new SKPaint()
        {
            Color = options.TextColor,
            IsAntialias = options.IsAntialias,
            Typeface = typeface,
            TextSize = options.FontSize,
        };

        string effectiveText = text ?? string.Empty;
        if (effectiveText.Length > 0)
        {
            SKRect bounds = new SKRect();
            paint.MeasureText(effectiveText, ref bounds);
            float x = GetTextX(bounds, options);
            float y = GetTextY(bounds, options);
            surface.Canvas.DrawText(effectiveText, x, y, paint);
        }

        using SKImage image = surface.Snapshot();
        using SKData encoded = EncodeImage(image, format, quality);
        return Task.FromResult(encoded.ToArray());
    }

    public Task<byte[]> CreateTextImageAsync(string text, HomeAutomationFontCatalog fontCatalog, string fontId, HomeAutomationTextRenderOptions options, HomeAutomationImageFormat format = HomeAutomationImageFormat.Png, int quality = 90, CancellationToken cancellationToken = default)
    {
        if (fontCatalog == null)
            throw new ArgumentNullException(nameof(fontCatalog));

        return CreateTextImageAsync(text, fontCatalog.GetTypeface(fontId), options, format, quality, cancellationToken);
    }

    public async Task SaveAsPngAsync(Stream content, string targetFile, CancellationToken cancellationToken = default)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        if (string.IsNullOrWhiteSpace(targetFile))
            throw new ArgumentException("The target file path is required.", nameof(targetFile));

        using MemoryStream buffer = await ReadBufferAsync(content, cancellationToken);
        using SKCodec codec = CreateCodec(buffer);

        string targetDirectory = Path.GetDirectoryName(targetFile);
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new InvalidOperationException("The target file path must include a directory.");

        Directory.CreateDirectory(targetDirectory);
        if (codec.EncodedFormat == SKEncodedImageFormat.Png)
        {
            buffer.Position = 0;
            await using FileStream targetStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);
            await buffer.CopyToAsync(targetStream, cancellationToken);
            return;
        }

        buffer.Position = 0;
        byte[] encodedBytes = await ConvertAsync(buffer, HomeAutomationImageFormat.Png, 100, cancellationToken);
        await using FileStream output = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);
        await output.WriteAsync(encodedBytes, cancellationToken);
    }

    private static async Task<MemoryStream> ReadBufferAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        MemoryStream buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            buffer.Dispose();
            throw new InvalidDataException("The image payload is empty.");
        }

        buffer.Position = 0;
        return buffer;
    }

    private static SKCodec CreateCodec(MemoryStream buffer)
    {
        buffer.Position = 0;
        using SKManagedStream detectionStream = new SKManagedStream(buffer, false);
        SKCodec codec = SKCodec.Create(detectionStream);
        if (codec == null)
            throw new InvalidDataException("Invalid file format.");

        return codec;
    }

    private static SKBitmap DecodeBitmap(MemoryStream buffer)
    {
        buffer.Position = 0;
        using SKManagedStream imageStream = new SKManagedStream(buffer, false);
        SKBitmap bitmap = SKBitmap.Decode(imageStream);
        if (bitmap == null)
            throw new InvalidDataException("Invalid file format.");

        return bitmap;
    }

    private static SKData EncodeImage(SKImage image, HomeAutomationImageFormat format, int quality)
    {
        SKEncodedImageFormat encodedFormat = ToEncodedFormat(format);
        SKData data = image.Encode(encodedFormat, NormalizeQuality(quality));
        if (data == null)
            throw new InvalidOperationException("Unable to encode the image.");

        return data;
    }

    private static SKBitmap ResizeBitmap(SKBitmap bitmap, int width, int height)
    {
        SKBitmap resizedBitmap = new SKBitmap(width, height, bitmap.ColorType, bitmap.AlphaType);
        bool scaled = bitmap.ScalePixels(resizedBitmap, SKFilterQuality.Medium);
        if (!scaled)
        {
            resizedBitmap.Dispose();
            throw new InvalidOperationException("Unable to resize the image.");
        }

        return resizedBitmap;
    }

    private static (int Width, int Height) FitWithinBounds(int width, int height, int maxWidth, int maxHeight)
    {
        decimal scale = Math.Min(1m, Math.Min((decimal)maxWidth / width, (decimal)maxHeight / height));
        int fittedWidth = Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero));
        int fittedHeight = Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero));
        return (fittedWidth, fittedHeight);
    }

    private static int NormalizeQuality(int quality)
    {
        if (quality < 0 || quality > 100)
            throw new ArgumentOutOfRangeException(nameof(quality));

        return quality;
    }

    private static SKEncodedImageFormat ToEncodedFormat(HomeAutomationImageFormat format)
    {
        return format switch
        {
            HomeAutomationImageFormat.Png => SKEncodedImageFormat.Png,
            HomeAutomationImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            HomeAutomationImageFormat.Webp => SKEncodedImageFormat.Webp,
            _ => throw new NotSupportedException($"Image format '{format}' is not supported for encoding.")
        };
    }

    private static HomeAutomationImageFormat FromEncodedFormat(SKEncodedImageFormat format)
    {
        return format switch
        {
            SKEncodedImageFormat.Png => HomeAutomationImageFormat.Png,
            SKEncodedImageFormat.Jpeg => HomeAutomationImageFormat.Jpeg,
            SKEncodedImageFormat.Webp => HomeAutomationImageFormat.Webp,
            SKEncodedImageFormat.Gif => HomeAutomationImageFormat.Gif,
            SKEncodedImageFormat.Bmp => HomeAutomationImageFormat.Bmp,
            _ => HomeAutomationImageFormat.Unknown
        };
    }

    private static string GetMimeType(SKEncodedImageFormat format)
    {
        return format switch
        {
            SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Webp => "image/webp",
            SKEncodedImageFormat.Gif => "image/gif",
            SKEncodedImageFormat.Bmp => "image/bmp",
            _ => "application/octet-stream"
        };
    }

    private static void ValidateTextRenderOptions(HomeAutomationTextRenderOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        if (options.Width <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.Width));

        if (options.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.Height));

        if (options.FontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.FontSize));

        if (options.HorizontalPadding < 0)
            throw new ArgumentOutOfRangeException(nameof(options.HorizontalPadding));

        if (options.VerticalPadding < 0)
            throw new ArgumentOutOfRangeException(nameof(options.VerticalPadding));
    }

    private static float GetTextX(SKRect bounds, HomeAutomationTextRenderOptions options)
    {
        return options.HorizontalAlignment switch
        {
            HomeAutomationTextHorizontalAlignment.Left => options.HorizontalPadding - bounds.Left,
            HomeAutomationTextHorizontalAlignment.Center => ((options.Width - bounds.Width) / 2f) - bounds.Left,
            HomeAutomationTextHorizontalAlignment.Right => options.Width - options.HorizontalPadding - bounds.Right,
            _ => options.HorizontalPadding - bounds.Left,
        };
    }

    private static float GetTextY(SKRect bounds, HomeAutomationTextRenderOptions options)
    {
        return options.VerticalAlignment switch
        {
            HomeAutomationTextVerticalAlignment.Top => options.VerticalPadding - bounds.Top,
            HomeAutomationTextVerticalAlignment.Middle => ((options.Height - bounds.Height) / 2f) - bounds.Top,
            HomeAutomationTextVerticalAlignment.Bottom => options.Height - options.VerticalPadding - bounds.Bottom,
            _ => options.VerticalPadding - bounds.Top,
        };
    }
}