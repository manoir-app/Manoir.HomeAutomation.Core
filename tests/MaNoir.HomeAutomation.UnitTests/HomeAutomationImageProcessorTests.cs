using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System.IO;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class HomeAutomationImageProcessorTests
{
    [TestMethod]
    public async Task ReadInfoAsync_ShouldReturnImageDimensionsAndFormat()
    {
        HomeAutomationImageProcessor processor = new HomeAutomationImageProcessor();
        await using MemoryStream input = CreateImage(3, 2, SKEncodedImageFormat.Jpeg, new SKColor(12, 34, 56));

        HomeAutomationImageInfo info = await processor.ReadInfoAsync(input);

        Assert.IsNotNull(info);
        Assert.AreEqual(3, info.Width);
        Assert.AreEqual(2, info.Height);
        Assert.AreEqual(HomeAutomationImageFormat.Jpeg, info.Format);
        Assert.AreEqual("image/jpeg", info.MimeType);
    }

    [TestMethod]
    public async Task ConvertAsync_ShouldTranscodeJpegToPng()
    {
        HomeAutomationImageProcessor processor = new HomeAutomationImageProcessor();
        await using MemoryStream input = CreateImage(2, 1, SKEncodedImageFormat.Jpeg, new SKColor(120, 40, 10));

        byte[] pngBytes = await processor.ConvertAsync(input, HomeAutomationImageFormat.Png);

        Assert.IsTrue(pngBytes.Length > 8);
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, pngBytes[..8]);
    }

    [TestMethod]
    public async Task ResizeAsync_ShouldFitImageWithinBoundsWithoutUpscaling()
    {
        HomeAutomationImageProcessor processor = new HomeAutomationImageProcessor();
        await using MemoryStream input = CreateImage(100, 50, SKEncodedImageFormat.Png, new SKColor(10, 20, 30));

        byte[] resizedBytes = await processor.ResizeAsync(input, 40, 40);
        using SKBitmap resizedBitmap = SKBitmap.Decode(resizedBytes);

        Assert.IsNotNull(resizedBitmap);
        Assert.AreEqual(40, resizedBitmap.Width);
        Assert.AreEqual(20, resizedBitmap.Height);
    }

    [TestMethod]
    public async Task ComposeOverlayAsync_ShouldDrawOverlayAtSpecifiedLocation()
    {
        HomeAutomationImageProcessor processor = new HomeAutomationImageProcessor();
        await using MemoryStream background = CreateImage(2, 2, SKEncodedImageFormat.Png, SKColors.White);
        await using MemoryStream overlay = CreateImage(1, 1, SKEncodedImageFormat.Png, SKColors.Red);

        byte[] outputBytes = await processor.ComposeOverlayAsync(background, overlay, 1, 0);
        using SKBitmap bitmap = SKBitmap.Decode(outputBytes);

        Assert.IsNotNull(bitmap);
        Assert.AreEqual(SKColors.White, bitmap.GetPixel(0, 0));
        Assert.AreEqual(SKColors.Red, bitmap.GetPixel(1, 0));
        Assert.AreEqual(SKColors.White, bitmap.GetPixel(0, 1));
        Assert.AreEqual(SKColors.White, bitmap.GetPixel(1, 1));
    }

    [TestMethod]
    public async Task CreateTextImageAsync_ShouldRenderTextOnBackground()
    {
        HomeAutomationImageProcessor processor = new HomeAutomationImageProcessor();
        HomeAutomationTextRenderOptions options = new HomeAutomationTextRenderOptions()
        {
            Width = 240,
            Height = 80,
            FontSize = 32,
            TextColor = SKColors.Black,
            BackgroundColor = SKColors.White,
            HorizontalAlignment = HomeAutomationTextHorizontalAlignment.Center,
            VerticalAlignment = HomeAutomationTextVerticalAlignment.Middle,
        };

        byte[] outputBytes = await processor.CreateTextImageAsync("Test", SKTypeface.Default, options);
        using SKBitmap bitmap = SKBitmap.Decode(outputBytes);

        Assert.IsNotNull(bitmap);
        Assert.AreEqual(240, bitmap.Width);
        Assert.AreEqual(80, bitmap.Height);
        Assert.IsTrue(ContainsPixelDifferentFrom(bitmap, SKColors.White));
    }

    private static MemoryStream CreateImage(int width, int height, SKEncodedImageFormat format, SKColor color)
    {
        MemoryStream stream = new MemoryStream();
        using SKBitmap bitmap = new SKBitmap(width, height);
        using SKCanvas canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(format, 100);
        data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }

    private static bool ContainsPixelDifferentFrom(SKBitmap bitmap, SKColor color)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != color)
                    return true;
            }
        }

        return false;
    }
}