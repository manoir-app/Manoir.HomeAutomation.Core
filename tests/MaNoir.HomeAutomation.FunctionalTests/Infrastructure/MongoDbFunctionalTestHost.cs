using MaNoir.Core.DataAccess;
using MaNoir.Core.Setup;
using MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;
using Testcontainers.MongoDb;

namespace MaNoir.HomeAutomation.FunctionalTests.Infrastructure;

internal sealed class MongoDbFunctionalTestHost : IAsyncDisposable
{
    private static readonly SemaphoreSlim Sync = new SemaphoreSlim(1, 1);
    private static readonly MongoDbContainer Container = new MongoDbBuilder("mongo:7.0")
        .Build();
    private static bool _started;

    public MongoDbFunctionalTestHost()
    {
    }

    public string ConnectionString
    {
        get { return Container.GetConnectionString(); }
    }

    public async Task StartAsync()
    {
        await Sync.WaitAsync();
        try
        {
            if (!_started)
            {
                await Container.StartAsync();
                _started = true;
            }

            MongoClient client = new MongoClient(ConnectionString);
            await client.DropDatabaseAsync(MongoDbHelper.DefaultDatabaseName);
            InitialSetupLogic.InvalidateCachedStatus(ConnectionString);
        }
        finally
        {
            Sync.Release();
        }
    }

    public IMongoDatabase GetDatabase(string databaseName)
    {
        MongoClient client = new MongoClient(ConnectionString);
        return client.GetDatabase(databaseName);
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