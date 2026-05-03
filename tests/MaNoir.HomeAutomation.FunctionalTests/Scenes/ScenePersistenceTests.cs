using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.Core.Files;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NATS.Client;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Scenes;

[TestClass]
[DoNotParallelize]
public sealed class ScenePersistenceTests
{
    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertGroupAsync_AndGetGroupsAsync_ShouldPersistAndFilterRemoteGroups()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room", Order = 2, VisibleInRemote = true });
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "private-room", Label = "Private room", Order = 1, VisibleInRemote = false });

        List<SceneGroup> allGroups = await logic.GetGroupsAsync();
        List<SceneGroup> remoteGroups = await logic.GetGroupsAsync(true);

        Assert.HasCount(2, allGroups);
        Assert.AreEqual("private-room", allGroups[0].Id);
        Assert.AreEqual("living-room", allGroups[1].Id);
        Assert.HasCount(1, remoteGroups);
        Assert.AreEqual("living-room", remoteGroups[0].Id);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertSceneAsync_AndGetScenesAsync_ShouldPersistAndFilterRemoteScenes()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room", VisibleInRemote = true });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie", OrderInGroup = 2, VisibleInRemote = true });
        await logic.UpsertSceneAsync(new Scene() { Id = "cleanup", GroupId = "living-room", Label = "Cleanup", OrderInGroup = 1, VisibleInRemote = false });

        List<Scene> allScenes = await logic.GetScenesAsync("living-room");
        List<Scene> remoteScenes = await logic.GetScenesAsync("living-room", true);

        Assert.HasCount(2, allScenes);
        Assert.AreEqual("cleanup", allScenes[0].Id);
        Assert.AreEqual("movie", allScenes[1].Id);
        Assert.HasCount(1, remoteScenes);
        Assert.AreEqual("movie", remoteScenes[0].Id);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpdateActiveScenesForGroupAsync_WhenOnlyRemote_ShouldIgnoreNonRemoteScenes()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room", VisibleInRemote = true });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie", VisibleInRemote = true });
        await logic.UpsertSceneAsync(new Scene() { Id = "service", GroupId = "living-room", Label = "Service", VisibleInRemote = false });

        SceneGroup updatedGroup = await logic.UpdateActiveScenesForGroupAsync("living-room", new[] { "movie", "service" }, true);

        Assert.IsNotNull(updatedGroup);
        CollectionAssert.AreEqual(new[] { "movie" }, updatedGroup.CurrentActiveScenes);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task DeleteSceneAsync_ShouldRemoveActiveReferenceAndPublishContentChangedMessage()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie" });
        await logic.UpdateActiveScenesForGroupAsync("living-room", new[] { "movie" });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync(ScenarioContentChangedMessage.ScenarioChangedTopic);
        connection.Flush();

        bool deleted = await logic.DeleteSceneAsync("movie");
        Msg message = subscription.NextMessage(5000);
        ScenarioContentChangedMessage published = BaseMessage.ReadAs<ScenarioContentChangedMessage>(Encoding.UTF8.GetString(message.Data));
        SceneGroup group = await logic.GetGroupAsync("living-room");
        Scene deletedScene = await logic.GetSceneByIdAsync("movie");

        Assert.IsTrue(deleted);
        Assert.IsNotNull(published);
        Assert.AreEqual("living-room", published.SceneGroupId);
        Assert.AreEqual("movie", published.SceneId);
        Assert.IsNotNull(group);
        Assert.HasCount(0, group.CurrentActiveScenes);
        Assert.IsNull(deletedScene);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ExecuteSceneAsync_ShouldPublishLegacyExecuteMessage()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();
        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie" });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync("homeautomation.scenario.execute");
        connection.Flush();

        bool executed = await logic.ExecuteSceneAsync("movie");
        Msg message = subscription.NextMessage(5000);
        ExecuteScenarioHomeAutomationMessage published = BaseMessage.ReadAs<ExecuteScenarioHomeAutomationMessage>(Encoding.UTF8.GetString(message.Data));

        Assert.IsTrue(executed);
        Assert.IsNotNull(published);
        Assert.AreEqual("homeautomation.scenario.execute", message.Subject);
        Assert.AreEqual("movie", published.SceneId);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task UpsertImageAsync_AndDeleteImageAsync_ShouldStorePublicPngAndUpdateSceneImages()
    {
        await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
        await host.StartAsync();

        string tempFolder = Path.Combine(Path.GetTempPath(), "manoir-homeautomation-scenes-tests", Guid.NewGuid().ToString("N"));

        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);
        using ProcessEnvironmentVariableScope fileScope = new ProcessEnvironmentVariableScope("MANOIR_FILE_STORAGE_FOLDER", tempFolder);

        SceneLogic logic = new SceneLogic();
        await logic.UpsertGroupAsync(new SceneGroup() { Id = "living-room", Label = "Living room" });
        await logic.UpsertSceneAsync(new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie" });

        await using MemoryStream uploadStream = CreateJpegImageStream();

        Scene storedScene = await logic.UpsertImageAsync("movie", "Banner", uploadStream);

        string localFile = Path.Combine(tempFolder, "public", "home-automation", "images", "scenes", "movie", "banner.png");
        Assert.IsNotNull(storedScene);
        Assert.IsTrue(File.Exists(localFile));
        Assert.IsTrue(storedScene.Images.ContainsKey("banner"));
        Assert.AreEqual("/api/core/files/public/home-automation/images/scenes/movie/banner.png", storedScene.Images["banner"]);
        Assert.AreEqual("image/png", FileStorageHelper.GetStoredFileMetadata(localFile)?.ContentType);

        Scene updatedScene = await logic.DeleteImageAsync("movie", "banner");

        Assert.IsNotNull(updatedScene);
        Assert.IsFalse(File.Exists(localFile));
        Assert.IsFalse(updatedScene.Images.ContainsKey("banner"));

        if (Directory.Exists(tempFolder))
            Directory.Delete(tempFolder, true);
    }

    private static MemoryStream CreateJpegImageStream()
    {
        MemoryStream stream = new MemoryStream();
        using (Image<Rgba32> image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 255);
            image.SaveAsJpeg(stream);
        }

        stream.Position = 0;
        return stream;
    }
}