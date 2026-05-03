namespace Home.Common.Messages;

public class ExecuteScenarioHomeAutomationMessage : BaseMessage
{
    public ExecuteScenarioHomeAutomationMessage() : base("homeautomation.scenario.execute")
    {
    }

    public ExecuteScenarioHomeAutomationMessage(string topic) : base(topic)
    {
    }

    public static ExecuteScenarioHomeAutomationMessage GetExecuteMessage(string sceneId)
    {
        return new ExecuteScenarioHomeAutomationMessage()
        {
            SceneId = sceneId
        };
    }

    public static ExecuteScenarioHomeAutomationMessage GetDisableMessage(string sceneId)
    {
        return new ExecuteScenarioHomeAutomationMessage("homeautomation.scenario.disable")
        {
            SceneId = sceneId
        };
    }

    public string SceneId { get; set; }
}