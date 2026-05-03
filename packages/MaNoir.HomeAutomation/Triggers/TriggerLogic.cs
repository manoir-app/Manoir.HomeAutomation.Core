namespace MaNoir.HomeAutomation;

public sealed partial class TriggerLogic
{
    private readonly TriggerMongoOperations _mongoOperations;

    public TriggerLogic()
    {
        _mongoOperations = new TriggerMongoOperations();
    }
}