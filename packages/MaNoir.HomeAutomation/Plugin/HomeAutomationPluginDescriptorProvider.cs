using MaNoir.Core.Contracts.Models.Contributions;

namespace MaNoir.HomeAutomation;

public static class HomeAutomationPluginDescriptorProvider
{
    public const string PluginId = "home-automation";
    public const string RepositoryUrl = "https://github.com/manoir-app/Manoir.HomeAutomation.Core";

    public static PluginDescriptor Create(string version)
    {
        string resolvedVersion = version;
        if (string.IsNullOrWhiteSpace(resolvedVersion))
            resolvedVersion = "0.0.0";

        return new PluginDescriptor()
        {
            Id = PluginId,
            Label = "Home Automation",
            Version = resolvedVersion,
            Description = "Root home automation capabilities for local protocols and scenarios.",
            Publisher = "MaNoir",
            RepositoryUrl = RepositoryUrl,
            DependencyRepositoryUrls = [],
            AccessZones = [],
            Contributions = [],
        };
    }
}
