using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MaNoir.Core.Contributions;
using MaNoir.Core.Entities;

namespace MaNoir.HomeAutomation.Api;

public static class HomeAutomationApiModule
{
    public static EntityProjectionRepositoryRegistry EntityProjectionRegistry { get; private set; }

    public static void ConfigureBuilder(WebApplicationBuilder builder)
    {
        EntityProjectionRegistry = HomeAutomationEntityProjectionRegistry.CreateDefault();

        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(HomeAutomationApiModule).Assembly);

        HomeAutomationApiAuthentication.Configure(builder);
    }

    public static void ConfigureApplication(WebApplication app)
    {
        string version = null;
        if (typeof(HomeAutomationApiModule).Assembly.GetName().Version != null)
            version = typeof(HomeAutomationApiModule).Assembly.GetName().Version.ToString(3);

        new PluginRegistrationLogic()
            .PublishPluginDescriptorAsync(HomeAutomationPluginDescriptorProvider.Create(version))
            .GetAwaiter()
            .GetResult();

        app.UseAuthentication();
        app.UseAuthorization();

        // Every API endpoint requires a user unless it says [AllowAnonymous]. The requirement is set on the
        // controllers rather than as a fallback policy, which would also close the Admin UI pages and /healthz.
        app.MapControllers().RequireAuthorization();
    }
}
