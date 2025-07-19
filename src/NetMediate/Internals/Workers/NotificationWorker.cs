using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals.Workers;

internal sealed class NotificationWorker(INotifiable mediator, Configuration configuration, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("Notification worker started.");

        await foreach(var message in configuration.ChannelReader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            if (message is null)
                continue;

            try
            {
                logger.LogDebug("Processing message of type {MessageType}: {Message}", message.GetType().Name, message);
                await mediator.Notifies(message, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (configuration.LogUnhandledMessages)
                {
                    logger.Log(configuration.UnhandledMessagesLogLevel, ex, "Error processing message of type {MessageType}: {Message}", message.GetType().Name, message);
                }
                else if (!configuration.IgnoreUnhandledMessages)
                {
                    throw;
                }
            }
        }

        logger.LogDebug("Notification worker stopped.");
    }
}
