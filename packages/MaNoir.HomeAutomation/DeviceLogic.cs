using System;

namespace MaNoir.HomeAutomation;

public sealed partial class DeviceLogic
{
    private readonly DeviceMongoOperations _mongoOperations;

    public DeviceLogic()
    {
        try
        {
            _mongoOperations = new DeviceMongoOperations();
        }
        catch (InvalidOperationException)
        {
            _mongoOperations = null;
        }
    }
}