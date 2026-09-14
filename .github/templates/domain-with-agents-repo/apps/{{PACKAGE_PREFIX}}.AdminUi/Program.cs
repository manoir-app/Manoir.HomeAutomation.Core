using Microsoft.AspNetCore.Builder;
using MaNoir.Core.AdminUi.Hosting;

namespace {{PACKAGE_PREFIX}}.AdminUi;

public static class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.AddMaNoirAdminUiHosting();
        // TODO: wire this domain's API module here (see Manoir.HomeAutomation.Core's
        // HomeAutomationApiModule.ConfigureBuilder for a reference implementation).

        WebApplication app = builder.Build();
        app.UseMaNoirAdminUiHosting();
        // TODO: wire this domain's API module here (see HomeAutomationApiModule.ConfigureApplication).

        app.Run();
    }
}
