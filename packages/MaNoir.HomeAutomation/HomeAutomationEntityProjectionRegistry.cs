using MaNoir.Core.Entities;

namespace MaNoir.HomeAutomation;

public static class HomeAutomationEntityProjectionRegistry
{
    public static EntityProjectionRepositoryRegistry CreateDefault()
    {
        EntityProjectionRepositoryRegistry registry = EntityProjectionRepositoryRegistry.CreateDefault();
        registry.Register(new DeviceProjectedEntityRepository());
        return registry;
    }
}