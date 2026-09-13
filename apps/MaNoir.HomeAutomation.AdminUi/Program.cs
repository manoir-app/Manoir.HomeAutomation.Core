using Microsoft.AspNetCore.Builder;
using MaNoir.Core.AdminUi.Hosting;
using MaNoir.HomeAutomation.Api;

namespace MaNoir.HomeAutomation.AdminUi;

public static class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.AddMaNoirAdminUiHosting();
        HomeAutomationApiModule.ConfigureBuilder(builder);

        WebApplication app = builder.Build();
        app.UseMaNoirAdminUiHosting();
        HomeAutomationApiModule.ConfigureApplication(app);

        app.Run();
    }
}