namespace Home.Common.Messages;

public class ScenarioContentChangedMessage : BaseMessage
{
    public const string ScenarioChangedTopic = "homeautomation.scenario.contentupdated";

    public ScenarioContentChangedMessage() : base(ScenarioChangedTopic)
    {
    }

    public string SceneId { get; set; }
    public string SceneGroupId { get; set; }
}