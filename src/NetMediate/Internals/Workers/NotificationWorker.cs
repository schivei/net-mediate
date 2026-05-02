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
            logger.LogDebug("Cancellation requested.");
        }
        finally
        {
            configuration.Dispose();
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        var pack = await configuration.ChannelReader.ReadAsync(cancellationToken);
        await DispatchPackAsync(pack, cancellationToken);
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
            logger.LogWarning(ex, "Failed to process message.");
        }
    }
}
