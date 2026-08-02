using System;
using System.Collections.Generic;
using System.Reflection;
using Home.Common.Messages;
using MaNoir.Core.Contracts.Models.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaNoir.Agents.Sarah;

public sealed class SarahRuntime
{
    private readonly ILogger<SarahRuntime> _logger;

    private static readonly string[] FixedMessageTopics =
    [
        "homeautomation.scenario.execute",
        "homeautomation.scenario.disable",
        DeviceActionTriggeredMessage.DeviceActionTriggered,
        ShellyOnboardingRequestedMessage.OnboardingRequested
    ];

    private static readonly string[] FixedCapabilities =
    [
        "home-automation",
        "home-automation.scenes",
        "home-automation.triggers",
        "home-automation.discovery",
        "home-automation.protocols"
    ];

    public SarahRuntime()
        : this(NullLogger<SarahRuntime>.Instance)
    {
    }

    public SarahRuntime(ILogger<SarahRuntime> logger)
    {
        _logger = logger;
    }

    public string AgentId => "sarah";

    public string MeshId => "local";

    public string DisplayName => "Sarah";

    public string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(30);

    public string[] MessageTopics => [.. FixedMessageTopics];

    public List<string> Capabilities => [.. FixedCapabilities];

    public void ReportStarting()
    {
        _logger.LogInformation("Starting agent {AgentId} for mesh {MeshId}.", AgentId, MeshId);
    }

    public void ReportHeartbeat()
    {
        _logger.LogInformation("Heartbeat emitted by agent {AgentId} for mesh {MeshId}.", AgentId, MeshId);
    }

    public void ReportRegistrationSucceeded(RegisteredAgent agent)
    {
        _logger.LogInformation("Agent {AgentId} registered in mesh {MeshId} with state {State}.", agent.AgentId, agent.MeshId, agent.State);
    }

    public void ReportRegistrationFailed(Exception exception)
    {
        _logger.LogWarning(exception, "Agent {AgentId} could not register in mesh {MeshId}.", AgentId, MeshId);
    }

    public void ReportHeartbeatFailed(Exception exception)
    {
        _logger.LogWarning(exception, "Heartbeat failed for agent {AgentId} in mesh {MeshId}.", AgentId, MeshId);
    }

    public void ReportStopping()
    {
        _logger.LogInformation("Stopping agent {AgentId}.", AgentId);
    }

    public void ReportTopicsSubscribed()
    {
        _logger.LogInformation("Agent {AgentId} subscribed to topics: {Topics}.", AgentId, string.Join(", ", MessageTopics));
    }

    public void ReportInterprocessStopped()
    {
        _logger.LogInformation("Interprocess listener stopped for agent {AgentId}.", AgentId);
    }

    public void ReportMessageIgnored(string topic)
    {
        _logger.LogDebug("Ignored unsupported topic {Topic} for agent {AgentId}.", topic, AgentId);
    }

    public AgentRegistrationRequest CreateRegistrationRequest(AgentState state, string statusMessage = null)
    {
        return new AgentRegistrationRequest()
        {
            AgentId = AgentId,
            DisplayName = DisplayName,
            MeshId = MeshId,
            Version = Version,
            Capabilities = Capabilities,
            State = state,
            StatusMessage = statusMessage
        };
    }

    public AgentHeartbeatRequest CreateHeartbeatRequest(AgentState state, string statusMessage = null)
    {
        return new AgentHeartbeatRequest()
        {
            AgentId = AgentId,
            MeshId = MeshId,
            State = state,
            StatusMessage = statusMessage
        };
    }
}