using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Infrastructure;

[TestClass]
public sealed class FunctionalTestAssemblyHooks
{
    private static ProcessEnvironmentVariableScope _mongoConnectionStringScope;
    private static ProcessEnvironmentVariableScope _natsHostScope;
    private static ProcessEnvironmentVariableScope _natsPortScope;
    private static ProcessEnvironmentVariableScope _natsCompatPortScope;
    private static ProcessEnvironmentVariableScope _mqttHostScope;
    private static ProcessEnvironmentVariableScope _mqttPortScope;
    private static ProcessEnvironmentVariableScope _mosquittoHostScope;
    private static ProcessEnvironmentVariableScope _mosquittoPortScope;

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        MongoDbFunctionalTestHost mongoHost = new MongoDbFunctionalTestHost();
        NatsFunctionalTestHost natsHost = new NatsFunctionalTestHost();
        MosquittoFunctionalTestHost mosquittoHost = new MosquittoFunctionalTestHost();

        await mongoHost.StartAsync();
        await natsHost.StartAsync();
        await mosquittoHost.StartAsync();

        _mongoConnectionStringScope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", mongoHost.ConnectionString);
        _natsHostScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_HOST", natsHost.Host);
        _natsPortScope = new ProcessEnvironmentVariableScope("NATS_SERVICE_PORT", natsHost.Port.ToString());
        _natsCompatPortScope = new ProcessEnvironmentVariableScope("NATS_PORT_4222_TCP_PROTO", null);
        _mqttHostScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_HOST", mosquittoHost.Host);
        _mqttPortScope = new ProcessEnvironmentVariableScope("MQTT_SERVICE_PORT", mosquittoHost.Port.ToString());
        _mosquittoHostScope = new ProcessEnvironmentVariableScope("MOSQUITTO_SERVICE_HOST", mosquittoHost.Host);
        _mosquittoPortScope = new ProcessEnvironmentVariableScope("MOSQUITTO_SERVICE_PORT", mosquittoHost.Port.ToString());
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        DisposeScope(ref _mosquittoPortScope);
        DisposeScope(ref _mosquittoHostScope);
        DisposeScope(ref _mqttPortScope);
        DisposeScope(ref _mqttHostScope);
        DisposeScope(ref _natsCompatPortScope);
        DisposeScope(ref _natsPortScope);
        DisposeScope(ref _natsHostScope);
        DisposeScope(ref _mongoConnectionStringScope);

        await MosquittoFunctionalTestHost.DisposeSharedAsync();
        await NatsFunctionalTestHost.DisposeSharedAsync();
        await MongoDbFunctionalTestHost.DisposeSharedAsync();
    }

    private static void DisposeScope(ref ProcessEnvironmentVariableScope scope)
    {
        if (scope == null)
            return;

        scope.Dispose();
        scope = null;
    }
}