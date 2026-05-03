using Home.Common.Model;

namespace MaNoir.HomeAutomation;

public sealed partial class SceneLogic
{
    private readonly SceneMongoOperations _sceneMongoOperations;
    private readonly SceneGroupMongoOperations _sceneGroupMongoOperations;

    public SceneLogic()
    {
        _sceneMongoOperations = new SceneMongoOperations();
        _sceneGroupMongoOperations = new SceneGroupMongoOperations();
    }
}