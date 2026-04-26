using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.FunctionalTests.Infrastructure;

[TestClass]
public sealed class FunctionalTestAssemblyHooks
{
    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        await MosquittoFunctionalTestHost.DisposeSharedAsync();
        await NatsFunctionalTestHost.DisposeSharedAsync();
        await MongoDbFunctionalTestHost.DisposeSharedAsync();
    }
}