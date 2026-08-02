using Home.Common.Messages;
using System.Text.Json;

namespace MaNoir.Agents.Sarah;

public sealed class SarahMessageRouter
{
    private readonly SceneExecutionService _sceneExecutionService;
    private readonly ShellyGen1RuntimeService _shellyGen1RuntimeService;
    private readonly ShellyGen2RuntimeService _shellyGen2RuntimeService;
    private readonly SarahRuntime _runtime;

    public SarahMessageRouter(SarahRuntime runtime, SceneExecutionService sceneExecutionService, ShellyGen1RuntimeService shellyGen1RuntimeService = null, ShellyGen2RuntimeService shellyGen2RuntimeService = null)
    {
        _runtime = runtime;
        _sceneExecutionService = sceneExecutionService;
        _shellyGen1RuntimeService = shellyGen1RuntimeService;
        _shellyGen2RuntimeService = shellyGen2RuntimeService;
    }

    public MessageResponse HandleMessage(MessageOrigin origin, string topic, string messageBody)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return MessageResponse.GenericFail;

        switch (topic.ToLowerInvariant())
        {
            case "homeautomation.scenario.execute":
                return _sceneExecutionService.Execute(messageBody, deactivate: false);
            case "homeautomation.scenario.disable":
                return _sceneExecutionService.Execute(messageBody, deactivate: true);
            case DeviceActionTriggeredMessage.DeviceActionTriggered:
                return _sceneExecutionService.TriggerForDeviceAction(messageBody);
            case ShellyOnboardingRequestedMessage.OnboardingRequested:
                return HandleShellyOnboarding(messageBody);
            default:
                _runtime.ReportMessageIgnored(topic);
                return MessageResponse.OK;
        }
    }

    private MessageResponse HandleShellyOnboarding(string messageBody)
    {
        try
        {
            ShellyOnboardingRequestedMessage request = JsonSerializer.Deserialize<ShellyOnboardingRequestedMessage>(messageBody);
            return ((_shellyGen1RuntimeService != null && _shellyGen1RuntimeService.OnboardAsync(request?.IpAddress).GetAwaiter().GetResult())
                || (_shellyGen2RuntimeService != null && _shellyGen2RuntimeService.OnboardAsync(request?.IpAddress).GetAwaiter().GetResult()))
                ? MessageResponse.OK
                : MessageResponse.GenericFail;
        }
        catch (JsonException)
        {
            return MessageResponse.GenericFail;
        }
    }
}