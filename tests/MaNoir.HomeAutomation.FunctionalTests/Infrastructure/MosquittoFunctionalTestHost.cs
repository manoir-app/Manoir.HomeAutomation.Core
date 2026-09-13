using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Infrastructure;

internal sealed class MosquittoFunctionalTestHost : IAsyncDisposable
{
    private const ushort ContainerPort = 1883;
    private static readonly SemaphoreSlim Sync = new SemaphoreSlim(1, 1);
    private static readonly IContainer Container = new ContainerBuilder("eclipse-mosquitto:2")
        .WithPortBinding(ContainerPort, true)
        .WithEntrypoint("sh", "-c")
        .WithCommand("printf 'listener 1883 0.0.0.0\nallow_anonymous true\n' > /tmp/mosquitto.conf && mosquitto -c /tmp/mosquitto.conf")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(ContainerPort))
        .Build();
    private static bool _started;

    public MosquittoFunctionalTestHost()
    {
    }

    public string Host => Container.Hostname;

    public int Port => Container.GetMappedPublicPort(ContainerPort);

    public async Task StartAsync()
    {
        await Sync.WaitAsync();
        try
        {
            if (_started)
            {
                return;
            }

            await Container.StartAsync();
            _started = true;
        }
        finally
        {
            Sync.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    internal static async Task DisposeSharedAsync()
    {
        await Sync.WaitAsync();
        try
        {
            if (!_started)
            {
                return;
            }

            await Container.DisposeAsync();
            _started = false;
        }
        finally
        {
            Sync.Release();
        }
    }
}