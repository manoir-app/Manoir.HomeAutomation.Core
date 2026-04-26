using Microsoft.AspNetCore.Builder;
using MaNoir.HomeAutomation.Api;

namespace MaNoir.HomeAutomation.AdminUi;

public static class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        HomeAutomationApiModule.ConfigureBuilder(builder);

        WebApplication app = builder.Build();
        HomeAutomationApiModule.ConfigureApplication(app);

        app.Run();
    }
}