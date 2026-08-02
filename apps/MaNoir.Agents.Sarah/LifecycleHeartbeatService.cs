using System;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.Core.Agents;
using MaNoir.Core.Contracts.Models.Agents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MaNoir.Agents.Sarah;

public sealed class LifecycleHeartbeatService : BackgroundService
{
    private readonly AgentRegistryLogic _agentRegistryLogic;
    private readonly ILogger<LifecycleHeartbeatService> _logger;
    private readonly SarahRuntime _runtime;
    private bool _isRegistered;

    public LifecycleHeartbeatService(SarahRuntime runtime, ILogger<LifecycleHeartbeatService> logger)
    {
        _agentRegistryLogic = new AgentRegistryLogic();
        _logger = logger;
        _runtime = runtime;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _runtime.ReportStarting();
        await TryRegisterAsync(AgentState.Starting, "Starting", cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_runtime.HeartbeatInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_isRegistered)
            {
                await TryRegisterAsync(AgentState.Degraded, "Waiting for registration", stoppingToken);
                continue;
            }

            try
            {
                await _agentRegistryLogic.HeartbeatAsync(_runtime.CreateHeartbeatRequest(AgentState.Ready, "Running"), stoppingToken);
                _runtime.ReportHeartbeat();
            }
            catch (Exception ex)
            {
                _isRegistered = false;
                _runtime.ReportHeartbeatFailed(ex);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_isRegistered)
        {
            try
            {
                await _agentRegistryLogic.HeartbeatAsync(_runtime.CreateHeartbeatRequest(AgentState.Stopping, "Stopping"), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Stopping heartbeat update failed for {AgentId}.", _runtime.AgentId);
            }
        }

        _runtime.ReportStopping();
        await base.StopAsync(cancellationToken);
    }

    private async Task TryRegisterAsync(AgentState state, string statusMessage, CancellationToken cancellationToken)
    {
        try
        {
            RegisteredAgent agent = await _agentRegistryLogic.RegisterAsync(_runtime.CreateRegistrationRequest(state, statusMessage), cancellationToken);
            _isRegistered = true;
            _runtime.ReportRegistrationSucceeded(agent);
        }
        catch (Exception ex)
        {
            _isRegistered = false;
            _runtime.ReportRegistrationFailed(ex);
        }
    }
}