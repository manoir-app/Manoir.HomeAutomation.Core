using MaNoir.Core.AdminUi.Hosting;
using MaNoir.HomeAutomation.Api;
using MaNoir.HomeAutomation.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Authentication;

[TestClass]
[DoNotParallelize]
public sealed class ApiAuthenticationFunctionalTests
{
    private const string ProtectedPath = "/v1.0/devices/discovery/enabled";

    [TestMethod]
    [TestCategory("Functional")]
    public async Task Api_ShouldOnlyOpenTheListedEndpointsWithoutAUser()
    {
        await using WebApplication app = CreateApplication();
        await app.StartAsync();

        List<RouteEndpoint> endpoints = [.. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()];
        List<string> anonymousRoutes = [.. endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null)
            .Select(endpoint => endpoint.RoutePattern.RawText.TrimStart('/'))
            .Distinct()
            .Order(StringComparer.Ordinal)];

        CollectionAssert.AreEqual(new[]
        {
            "api/system",
            "health",
            "v1.0/devices/discovery/appdevice",
            "v1.0/devices/discovery/appdevice/{deviceId}/check",
            "v1.0/system/mesh/local/triggers/{triggerId}/raise"
        }, anonymousRoutes);
        Assert.IsTrue(endpoints.All(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null
            || endpoint.Metadata.GetMetadata<IAuthorizeData>() != null));
    }

    [TestMethod]
    [TestCategory("Functional")]
    [DataRow("/v1.0/devices/discovered")]
    [DataRow("/v1.0/homeautomation/scenes/scenes")]
    [DataRow("/v1.0/homeautomation/scenes/execute/movie")]
    [DataRow("/v1.0/homeautomation/scripts")]
    [DataRow("/v1.0/system/mesh/local/triggers")]
    public async Task Api_ShouldRejectCallsWithoutAUser(string path)
    {
        await using WebApplication app = CreateApplication();
        await app.StartAsync();

        HttpResponseMessage response = await app.GetTestClient().GetAsync(path);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task Api_ShouldKeepHealthAndServiceInfoOpen()
    {
        await using WebApplication app = CreateApplication();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();

        HttpResponseMessage health = await client.GetAsync("/health");
        HttpResponseMessage system = await client.GetAsync("/api/system");

        Assert.AreEqual(HttpStatusCode.NoContent, health.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, system.StatusCode);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task Api_ShouldAcceptACoreTokenFromTheHeaderOrTheSessionCookie()
    {
        await using WebApplication app = CreateApplication();
        await app.StartAsync();

        HttpResponseMessage bearer = await HomeAutomationApiTestAuthentication.CreateAuthenticatedClient(app).GetAsync(ProtectedPath);

        using HttpRequestMessage cookieRequest = new(HttpMethod.Get, ProtectedPath);
        cookieRequest.Headers.Add("Cookie", "manoir_users_access_token=" + HomeAutomationApiTestAuthentication.CreateAccessToken());
        HttpResponseMessage cookie = await app.GetTestClient().SendAsync(cookieRequest);

        Assert.AreEqual(HttpStatusCode.OK, bearer.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, cookie.StatusCode);
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task Api_ShouldRejectForeignAndExpiredTokens()
    {
        await using WebApplication app = CreateApplication();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();

        string[] tokens =
        [
            "not-a-token",
            HomeAutomationApiTestAuthentication.CreateAccessToken(signingKey: "another-signing-key-value-with-more-than-32-chars"),
            HomeAutomationApiTestAuthentication.CreateAccessToken(audience: "someone.else"),
            HomeAutomationApiTestAuthentication.CreateAccessToken(expiresUtc: DateTime.UtcNow.AddMinutes(-10))
        ];

        foreach (string token in tokens)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, ProtectedPath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response = await client.SendAsync(request);

            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, token);
        }
    }

    [TestMethod]
    [TestCategory("Functional")]
    public async Task AdminUiHost_ShouldServePagesAndHealthCheckWithoutAUser()
    {
        string contentRoot = Directory.CreateTempSubdirectory("manoir-adminui-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(contentRoot, "wwwroot", "assets"));
            await File.WriteAllTextAsync(Path.Combine(contentRoot, "wwwroot", "index.html"), "<html><head></head><body>admin-ui</body></html>");
            await File.WriteAllTextAsync(Path.Combine(contentRoot, "wwwroot", "assets", "app.js"), "console.log('admin-ui');");

            // Same composition as MaNoir.HomeAutomation.AdminUi/Program.cs.
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions() { EnvironmentName = Environments.Development, ContentRootPath = contentRoot });
            builder.WebHost.UseTestServer();
            builder.AddMaNoirAdminUiHosting();
            HomeAutomationApiTestAuthentication.Configure(builder);
            HomeAutomationApiModule.ConfigureBuilder(builder);

            await using WebApplication app = builder.Build();
            app.UseMaNoirAdminUiHosting();
            HomeAutomationApiModule.ConfigureApplication(app);
            await app.StartAsync();
            HttpClient client = app.GetTestClient();

            foreach (string path in new[] { "/", "/space/overview", "/assets/app.js", "/healthz", "/health" })
            {
                HttpResponseMessage response = await client.GetAsync(path);

                Assert.IsTrue(response.IsSuccessStatusCode, $"{path} answered {(int)response.StatusCode}");
            }

            Assert.Contains("admin-ui", await client.GetStringAsync("/space/overview"));
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(ProtectedPath)).StatusCode);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("Functional")]
    public void Host_ShouldRefuseToStartInProductionWithoutASigningKey()
    {
        using ProcessEnvironmentVariableScope keyScope = new ProcessEnvironmentVariableScope("HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY", null);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions() { EnvironmentName = Environments.Production });

        Assert.ThrowsExactly<InvalidOperationException>(() => HomeAutomationApiModule.ConfigureBuilder(builder));
    }

    private static WebApplication CreateApplication()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions() { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        HomeAutomationApiTestAuthentication.Configure(builder);
        HomeAutomationApiModule.ConfigureBuilder(builder);

        WebApplication app = builder.Build();
        HomeAutomationApiModule.ConfigureApplication(app);
        return app;
    }
}
