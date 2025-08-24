using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals.Workers;

internal sealed class NotificationWorker(
    INotifiable mediator,
    Configuration configuration,
    ILogger<NotificationWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("Notification worker started.");

        try
        {
            while (await configuration.ChannelReader.WaitToReadAsync(stoppingToken))
            {
                try
                {
                    await ConsumeAsync(stoppingToken);
                }
                catch (ChannelClosedException)
                {
                    logger.LogDebug("Channel was closed, stopping notification worker.");
                    await configuration.DisposeAsync();
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            logger.LogDebug("System operation was canceled, stopping notification worker.");
        }

        await configuration.DisposeAsync();

        logger.LogDebug("Notification worker stopped.");
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        while (configuration.ChannelReader.TryRead(out var packet))
        {
            if (packet.Message is null)
                continue;
            
            logger.LogDebug(
                "Processing message of type {MessageType}.",
                packet.Message.GetType().Name
            );

            try
            {
                await mediator.Notifies(packet, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogTrace(
                    ex,
                    "An error occurred while processing message of type {MessageType}.",
                    packet.Message.GetType().Name
                );
            }
        }
    }
}
