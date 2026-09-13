using System;
using System.Threading.Tasks;
using MaNoir.Core.Mesh;
using MaNoir.Agents.Sarah.Awtrix;
using MaNoir.Agents.Sarah.Hue;
using MaNoir.HomeAutomation.Devices.Shelly;
using MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;
using MaNoir.Agents.Sarah.Shelly;
using MaNoir.Agents.Sarah.Zigbee2Mqtt;
using MaNoir.Agents.Sarah.Wled;
using MaNoir.HomeAutomation.Devices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MaNoir.Agents.Sarah;

public static class Program
{
    public static async Task Main(string[] args)
    {
        await WaitForLocalMeshAsync();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton<SarahRuntime>();
        builder.Services.AddSingleton<SarahDeviceService>();
        builder.Services.AddSingleton<RuntimeDeviceRegistry>();
        builder.Services.AddSingleton<HueRuntimeService>();
        builder.Services.AddSingleton<SceneExecutionService>();
        builder.Services.AddSingleton<TriggerRuntimeService>();
        builder.Services.AddSingleton<SarahMessageRouter>();
        builder.Services.AddSingleton<IHostedService, LifecycleHeartbeatService>();
        builder.Services.AddSingleton<IHostedService, MessagePumpService>();
        builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<TriggerRuntimeService>());
        builder.Services.AddSingleton<IHostedService, Zigbee2MqttRuntimeService>();
        builder.Services.AddSingleton<AwtrixRuntimeService>();
        builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<AwtrixRuntimeService>());
        builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<HueRuntimeService>());
        builder.Services.AddSingleton<ShellyGen1RuntimeService>();
        builder.Services.AddSingleton<ShellyGen2RuntimeService>();
        builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<ShellyGen1RuntimeService>());
        builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<ShellyGen2RuntimeService>());
        builder.Services.AddSingleton<WledRuntimeService>();
        builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<WledRuntimeService>());

        using IHost host = builder.Build();
        await host.RunAsync();
    }

    private static async Task WaitForLocalMeshAsync()
    {
        AutomationMeshLogic meshLogic = new AutomationMeshLogic();

        while (true)
        {
            try
            {
                if (await meshLogic.GetLocalAsync() != null)
                    return;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Could not check the local mesh: {exception.Message}");
            }

            Console.WriteLine("Waiting for the local mesh before starting Sarah services.");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}