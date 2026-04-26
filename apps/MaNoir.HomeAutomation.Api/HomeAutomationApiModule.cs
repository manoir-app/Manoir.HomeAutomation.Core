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

        app.MapControllers();
    }
}
