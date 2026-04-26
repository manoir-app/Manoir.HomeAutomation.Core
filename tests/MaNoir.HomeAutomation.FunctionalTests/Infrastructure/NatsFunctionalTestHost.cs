using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Infrastructure;

internal sealed class NatsFunctionalTestHost : IAsyncDisposable
{
    private const ushort ContainerPort = 4222;
    private static readonly SemaphoreSlim Sync = new SemaphoreSlim(1, 1);
    private static readonly IContainer Container = new ContainerBuilder("nats:2.10-alpine")
        .WithPortBinding(ContainerPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(ContainerPort))
        .Build();
    private static bool _started;

    public NatsFunctionalTestHost()
    {
    }

    public string Host => Container.Hostname;

    public int Port => Container.GetMappedPublicPort(ContainerPort);

    public string ConnectionString => $"nats://{Host}:{Port}";

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