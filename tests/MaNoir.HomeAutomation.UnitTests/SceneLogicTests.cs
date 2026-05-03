using Home.Common.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class SceneLogicTests
{
    [TestMethod]
    public void PrepareScene_ShouldNormalizeIdentifiersAndInitializeCollections()
    {
        Scene scene = new Scene()
        {
            Id = " EVENING ",
            GroupId = " LIVING-ROOM ",
            RoomIds = null,
            LocationZoneIds = null,
            ActivationSteps = null,
            DeactivationSteps = null,
            DetectionCriteria = null,
            Images = null,
            InvocationStrings = null
        };

        Scene prepared = SceneLogic.PrepareScene(scene);

        Assert.AreSame(scene, prepared);
        Assert.AreEqual("evening", prepared.Id);
        Assert.AreEqual("living-room", prepared.GroupId);
        Assert.IsNotNull(prepared.RoomIds);
        Assert.IsNotNull(prepared.LocationZoneIds);
        Assert.IsNotNull(prepared.ActivationSteps);
        Assert.IsNotNull(prepared.DeactivationSteps);
        Assert.IsNotNull(prepared.DetectionCriteria);
        Assert.IsNotNull(prepared.Images);
        Assert.IsNotNull(prepared.InvocationStrings);
    }

    [TestMethod]
    public void PrepareSceneGroup_AndApplySceneGroupUpdate_ShouldNormalizeIdsAndReplaceMutableFields()
    {
        SceneGroup existing = new SceneGroup()
        {
            Id = "group-a",
            Label = "Old label",
            CurrentActiveScenes = new List<string>() { "morning" },
            SceneIsExclusive = false,
            ClearGroupSceneId = "clear-old"
        };

        SceneGroup incoming = new SceneGroup()
        {
            Id = " GROUP-A ",
            Label = "New label",
            CurrentActiveScenes = new List<string>() { " EVENING ", "evening", "night" },
            SceneIsExclusive = true,
            ClearGroupSceneId = " CLEAR-NEW "
        };

        SceneLogic.PrepareSceneGroup(incoming);
        SceneLogic.ApplySceneGroupUpdate(existing, incoming);

        Assert.AreEqual("group-a", incoming.Id);
        Assert.AreEqual("clear-new", incoming.ClearGroupSceneId);
        CollectionAssert.AreEqual(new[] { "evening", "night" }, incoming.CurrentActiveScenes);
        Assert.AreEqual("New label", existing.Label);
        Assert.IsTrue(existing.SceneIsExclusive);
        Assert.AreEqual("clear-new", existing.ClearGroupSceneId);
        CollectionAssert.AreEqual(new[] { "evening", "night" }, existing.CurrentActiveScenes);
    }

    [TestMethod]
    public void GetSceneImagePublicUrl_ShouldNormalizeIds_AndRejectInvalidImageCodes()
    {
        string publicUrl = SceneLogic.GetSceneImagePublicUrl(" MOVIE ", " Banner ");
        string invalidPublicUrl = SceneLogic.GetSceneImagePublicUrl("movie", "../banner");

        Assert.AreEqual("/api/core/files/public/home-automation/images/scenes/movie/banner.png", publicUrl);
        Assert.IsNull(invalidPublicUrl);
    }
}