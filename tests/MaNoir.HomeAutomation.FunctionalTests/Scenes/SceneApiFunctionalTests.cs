using Home.Common.Messages;
using Home.Common.Model;
using MaNoir.HomeAutomation.Api;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NATS.Client;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Scenes;

[TestClass]
[DoNotParallelize]
public sealed class SceneApiFunctionalTests
{
    [TestMethod]
    [TestCategory("Functional")]
    public async Task SceneApi_ShouldFilterRemoteScenes_AndManageActiveScenes()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();

        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);

        await using WebApplication app = CreateApplication();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();

        await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/groups", new SceneGroup()
        {
            Id = "living-room",
            Label = "Living room",
            VisibleInRemote = true
        });
        await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/groups", new SceneGroup()
        {
            Id = "private-room",
            Label = "Private room",
            VisibleInRemote = false
        });
        await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/scenes", new Scene()
        {
            Id = "movie",
            GroupId = "living-room",
            Label = "Movie",
            VisibleInRemote = true
        });
        await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/scenes", new Scene()
        {
            Id = "service",
            GroupId = "living-room",
            Label = "Service",
            VisibleInRemote = false
        });

        List<SceneGroup> remoteGroups = await (await client.GetAsync("/v1.0/homeautomation/scenes/groups?onlyRemote=true")).Content.ReadFromJsonAsync<List<SceneGroup>>();
        List<Scene> remoteScenes = await (await client.GetAsync("/v1.0/homeautomation/scenes/groups/living-room/scenes?onlyRemote=true")).Content.ReadFromJsonAsync<List<Scene>>();
        HttpResponseMessage updateActiveResponse = await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/groups/living-room/activeScenes?onlyRemote=true", new[] { "movie", "service" });
        SceneGroup updatedGroup = await updateActiveResponse.Content.ReadFromJsonAsync<SceneGroup>();
        HttpResponseMessage deleteActiveResponse = await client.DeleteAsync("/v1.0/homeautomation/scenes/groups/living-room/activeScenes?scene=movie");
        SceneGroup clearedGroup = await deleteActiveResponse.Content.ReadFromJsonAsync<SceneGroup>();

        Assert.IsNotNull(remoteGroups);
        Assert.HasCount(1, remoteGroups);
        Assert.AreEqual("living-room", remoteGroups[0].Id);
        Assert.IsNotNull(remoteScenes);
        Assert.HasCount(1, remoteScenes);
        Assert.AreEqual("movie", remoteScenes[0].Id);
        Assert.AreEqual(HttpStatusCode.OK, updateActiveResponse.StatusCode);
        CollectionAssert.AreEqual(new[] { "movie" }, updatedGroup.CurrentActiveScenes);
        Assert.AreEqual(HttpStatusCode.OK, deleteActiveResponse.StatusCode);
        Assert.HasCount(0, clearedGroup.CurrentActiveScenes);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task SceneImagesApi_ShouldUploadAndDeleteSceneImage()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();

        string tempFolder = Path.Combine(Path.GetTempPath(), "manoir-homeautomation-scene-api-tests", Guid.NewGuid().ToString("N"));

        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope fileScope = new ProcessEnvironmentVariableScope("MANOIR_FILE_STORAGE_FOLDER", tempFolder);

        await using WebApplication app = CreateApplication();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();

        HttpResponseMessage groupResponse = await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/groups", new SceneGroup()
        {
            Id = "living-room",
            Label = "Living room"
        });

        HttpResponseMessage sceneResponse = await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/scenes", new Scene()
        {
            Id = "movie",
            GroupId = "living-room",
            Label = "Movie"
        });

        byte[] imagePayload = CreateJpegImagePayload();
        using HttpRequestMessage uploadRequest = new(HttpMethod.Post, "/v1.0/homeautomation/scenes/scenes/movie/images/banner")
        {
            Content = new ByteArrayContent(imagePayload)
        };
        uploadRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

        HttpResponseMessage uploadResponse = await client.SendAsync(uploadRequest);
        Scene uploadedScene = await uploadResponse.Content.ReadFromJsonAsync<Scene>();

        string localFile = Path.Combine(tempFolder, "public", "home-automation", "images", "scenes", "movie", "banner.png");

        Assert.AreEqual(HttpStatusCode.OK, groupResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, sceneResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.IsTrue(File.Exists(localFile));
        Assert.IsNotNull(uploadedScene);
        Assert.IsTrue(uploadedScene.Images.ContainsKey("banner"));
        Assert.AreEqual("/api/core/files/public/home-automation/images/scenes/movie/banner.png", uploadedScene.Images["banner"]);

        HttpResponseMessage deleteResponse = await client.DeleteAsync("/v1.0/homeautomation/scenes/scenes/movie/images/banner");
        Scene deletedScene = await deleteResponse.Content.ReadFromJsonAsync<Scene>();

        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.IsFalse(File.Exists(localFile));
        Assert.IsNotNull(deletedScene);
        Assert.IsFalse(deletedScene.Images.ContainsKey("banner"));

        if (Directory.Exists(tempFolder))
            Directory.Delete(tempFolder, true);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task ExecuteSceneApi_ShouldPublishLegacyExecuteMessage()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();

        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        await using WebApplication app = CreateApplication();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();

        await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/groups", new SceneGroup() { Id = "living-room", Label = "Living room" });
        await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/scenes", new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie" });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync("homeautomation.scenario.execute");
        connection.Flush();

        HttpResponseMessage executeResponse = await client.GetAsync("/v1.0/homeautomation/scenes/execute/movie");
        Msg message = subscription.NextMessage(5000);
        ExecuteScenarioHomeAutomationMessage published = BaseMessage.ReadAs<ExecuteScenarioHomeAutomationMessage>(Encoding.UTF8.GetString(message.Data));

        Assert.AreEqual(HttpStatusCode.OK, executeResponse.StatusCode);
        Assert.IsNotNull(published);
        Assert.AreEqual("movie", published.SceneId);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task DeleteSceneApi_ShouldRemoveActiveReference_AndPublishContentChangedMessage()
    {
        await using MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        await mongoHost.StartAsync();
        await using NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        await natsHost.StartAsync();

        using ProcessEnvironmentVariableScope mongoScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        using ProcessEnvironmentVariableScope hostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        using ProcessEnvironmentVariableScope portScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        using ProcessEnvironmentVariableScope compatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);

        await using WebApplication app = CreateApplication();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();

        await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/groups", new SceneGroup() { Id = "living-room", Label = "Living room" });
        await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/scenes", new Scene() { Id = "movie", GroupId = "living-room", Label = "Movie" });
        await client.PostAsJsonAsync("/v1.0/homeautomation/scenes/groups/living-room/activeScenes", new[] { "movie" });

        ConnectionFactory factory = new ConnectionFactory();
        using IConnection connection = factory.CreateConnection(natsHost.ConnectionString);
        using ISyncSubscription subscription = connection.SubscribeSync(ScenarioContentChangedMessage.ScenarioChangedTopic);
        connection.Flush();

        HttpResponseMessage deleteResponse = await client.DeleteAsync("/v1.0/homeautomation/scenes/scenes/movie");
        Msg message = subscription.NextMessage(5000);
        ScenarioContentChangedMessage published = BaseMessage.ReadAs<ScenarioContentChangedMessage>(Encoding.UTF8.GetString(message.Data));
        SceneGroup group = await (await client.GetAsync("/v1.0/homeautomation/scenes/groups/living-room")).Content.ReadFromJsonAsync<SceneGroup>();
        HttpResponseMessage getSceneResponse = await client.GetAsync("/v1.0/homeautomation/scenes/scenes/movie");

        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.IsNotNull(published);
        Assert.AreEqual("living-room", published.SceneGroupId);
        Assert.AreEqual("movie", published.SceneId);
        Assert.IsNotNull(group);
        Assert.HasCount(0, group.CurrentActiveScenes);
        Assert.AreEqual(HttpStatusCode.NotFound, getSceneResponse.StatusCode);
    }

    private static WebApplication CreateApplication()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions() { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        HomeAutomationApiModule.ConfigureBuilder(builder);

        WebApplication app = builder.Build();
        HomeAutomationApiModule.ConfigureApplication(app);
        return app;
    }

    private static byte[] CreateJpegImagePayload()
    {
        using SKBitmap bitmap = new SKBitmap(1, 1);
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}