using System;
using System.Threading;
using System.Threading.Tasks;
using Home.Common;
using Microsoft.Extensions.Hosting;

namespace MaNoir.Agents.Sarah;

public sealed class MessagePumpService : BackgroundService
{
    private readonly SarahMessageRouter _messageRouter;
    private readonly SarahRuntime _runtime;

    public MessagePumpService(SarahRuntime runtime, SarahMessageRouter messageRouter)
    {
        _messageRouter = messageRouter;
        _runtime = runtime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runtime.ReportTopicsSubscribed();

        Task listenerTask = Task.Run(() => NatsInterprocessListener.Run(_runtime.MessageTopics, _messageRouter.HandleMessage), CancellationToken.None);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            NatsInterprocessListener.Stop();
            _runtime.ReportInterprocessStopped();
            await listenerTask;
        }
    }
}