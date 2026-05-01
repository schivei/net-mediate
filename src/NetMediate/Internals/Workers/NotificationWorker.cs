using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals.Workers;

internal sealed class NotificationWorker(Configuration configuration, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await configuration.ChannelReader.WaitToReadAsync(stoppingToken))
            {
                await ConsumeAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (!configuration.Disposed)
                await configuration.DisposeAsync();
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        while (configuration.ChannelReader.TryRead(out var pack))
        {
            if (pack is null)
                continue;

            await DispatchPackAsync(pack, cancellationToken);
        }
    }

    private async Task DispatchPackAsync(IPack pack, CancellationToken cancellationToken)
    {
        try
        {
            await pack.Dispatch(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process message of type {MessageType}.", pack.MessageTypeName);
        }
    }
}
