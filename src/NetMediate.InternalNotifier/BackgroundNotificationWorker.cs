using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetMediate.Internals;

namespace NetMediate.InternalNotifier;

/// <summary>
/// Background hosted service that drains a notification <see cref="Channel{T}"/> and
/// dispatches each packet to its registered <see cref="INotificationHandler{TMessage}"/>
/// implementations.
/// </summary>
/// <remarks>
/// Started automatically when the application host starts.  Stop the host to allow
/// the worker to complete draining before shutdown.
/// </remarks>
internal sealed class BackgroundNotificationWorker(
    INotifiable mediator,
    ChannelReader<INotificationPacket> channelReader,
    ILogger<BackgroundNotificationWorker> logger
) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("Background notification worker started.");

        try
        {
            while (await channelReader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                await DrainBatchAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (ChannelClosedException)
        {
            logger.LogDebug("Notification channel closed; worker stopping.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            logger.LogDebug("Background notification worker cancelled.");
        }

        logger.LogDebug("Background notification worker stopped.");
    }

    private async Task DrainBatchAsync(CancellationToken cancellationToken)
    {
        while (channelReader.TryRead(out var packet))
        {
            if (packet.Message is null)
                continue;

            logger.LogDebug(
                "Dispatching notification of type {MessageType}.",
                packet.Message.GetType().Name
            );

            try
            {
                await mediator.Notifies(packet, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogTrace(
                    ex,
                    "Handler error for notification type {MessageType}.",
                    packet.Message.GetType().Name
                );
            }
        }
    }
}
