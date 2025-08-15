using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace NetMediate.Internals.Workers;

internal sealed class NotificationWorker(INotifiable mediator, Configuration configuration, ILogger<NotificationWorker> logger) : BackgroundService
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
        while (configuration.ChannelReader.TryRead(out var message))
        {
            if (message is null)
                continue;

            try
            {
                logger.LogDebug("Processing message of type {MessageType}: {Message}", message.GetType().Name, message);
                await mediator.Notifies(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not ChannelClosedException)
            {
                if (!configuration.IgnoreUnhandledMessages)
                    throw;

                if (configuration.LogUnhandledMessages)
                    logger.Log(configuration.UnhandledMessagesLogLevel, ex, "Error processing message of type {MessageType}: {Message}", message.GetType().Name, message);
            }
        }
    }
}
