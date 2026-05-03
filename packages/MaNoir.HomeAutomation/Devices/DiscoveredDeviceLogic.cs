namespace MaNoir.HomeAutomation;

public sealed partial class DiscoveredDeviceLogic
{
    private readonly DiscoveredDeviceMongoOperations _mongoOperations;
    private readonly DeviceMongoOperations _deviceMongoOperations;

    public DiscoveredDeviceLogic()
    {
        _mongoOperations = new DiscoveredDeviceMongoOperations();
        _deviceMongoOperations = new DeviceMongoOperations();
    }
}