using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System.Collections.Generic;
using System.IO;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class HomeAutomationFontCatalogTests
{
    [TestMethod]
    public void GetFontPath_ShouldResolveRelativePathUnderConfiguredRoot()
    {
        HomeAutomationFontCatalog catalog = new HomeAutomationFontCatalog(
            rootFolder: Path.Combine("c:", "fonts-root"),
            definitions:
            [
                new HomeAutomationFontDefinition() { Id = "custom", RelativePath = Path.Combine("noto", "Custom-Regular.ttf") }
            ]);

        string path = catalog.GetFontPath("custom");

        StringAssert.EndsWith(path, Path.Combine("fonts-root", "noto", "Custom-Regular.ttf"));
    }

    [TestMethod]
    public void GetFontPath_WhenFontIdIsUnknown_ShouldThrow()
    {
        HomeAutomationFontCatalog catalog = new HomeAutomationFontCatalog(rootFolder: "c:\\fonts-root", definitions: new List<HomeAutomationFontDefinition>());

        Assert.ThrowsExactly<KeyNotFoundException>(() => catalog.GetFontPath("missing"));
    }

    [TestMethod]
    public void GetTypeface_WhenFontFileIsMissing_ShouldThrowHelpfulError()
    {
        string rootFolder = Path.Combine(Path.GetTempPath(), "manoir-fonts", Path.GetRandomFileName());
        HomeAutomationFontCatalog catalog = new HomeAutomationFontCatalog(
            rootFolder: rootFolder,
            definitions:
            [
                new HomeAutomationFontDefinition() { Id = "sans-regular", RelativePath = "NotoSans-Regular.ttf" }
            ]);

        FileNotFoundException exception = Assert.ThrowsExactly<FileNotFoundException>(() => catalog.GetTypeface("sans-regular"));

        StringAssert.Contains(exception.Message, "Assets/Fonts");
        StringAssert.Contains(exception.Message, "sans-regular");
    }

    [TestMethod]
    public void GetTypeface_WhenSharedBundledFontExists_ShouldLoadTypeface()
    {
        using HomeAutomationFontCatalog catalog = new HomeAutomationFontCatalog();

        using SKTypeface typeface = catalog.GetTypeface("sans-regular");

        Assert.IsNotNull(typeface);
    }
}