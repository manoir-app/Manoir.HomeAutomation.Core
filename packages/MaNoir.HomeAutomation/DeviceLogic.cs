namespace MaNoir.HomeAutomation;

public sealed partial class DeviceLogic
{
    private readonly DeviceMongoOperations _mongoOperations;

    public DeviceLogic()
    {
        _mongoOperations = new DeviceMongoOperations();
    }
}