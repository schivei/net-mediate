using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals.Workers;

internal sealed class NotificationWorker(Configuration configuration, ITerminator terminator, ILogger<NotificationWorker> logger) : BackgroundService
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
        finally
        {
            if (!configuration.Disposed)
            {
                logger.LogInformation("Notification worker is shutting down. Disposing configuration and terminating application.");
                await configuration.DisposeAsync();
                terminator.Terminate();
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        while (configuration.ChannelReader.TryRead(out var message))
        {
            if (message is null)
                continue;

            await message.Dispatch(cancellationToken);
        }
    }
}
