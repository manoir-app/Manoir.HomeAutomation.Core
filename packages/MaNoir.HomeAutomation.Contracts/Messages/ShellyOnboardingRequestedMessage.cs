namespace Home.Common.Messages;

public sealed class ShellyOnboardingRequestedMessage : BaseMessage
{
    public const string OnboardingRequested = "homeautomation.shelly.onboard";

    public ShellyOnboardingRequestedMessage()
        : base(OnboardingRequested)
    {
    }

    public string IpAddress { get; set; }
}