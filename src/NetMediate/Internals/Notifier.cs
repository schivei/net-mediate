using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

internal class Notifier(IServiceProvider serviceProvider, ILogger<Notifier> logger) : INotifiable
{
    public virtual Task DispatchNotifications<TMessage>(TMessage message, INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        // Fire-and-forget each handler individually to avoid Task.WhenAll allocation overhead.
        // Exceptions are logged per-handler so one failure does not suppress others.
        foreach (var handler in handlers)
        {
            _ = handler.Handle(message, cancellationToken)
                .ContinueWith(
                    t => logger.LogError(t.Exception, "{Message}", t.Exception!.Message),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
        }

        return Task.CompletedTask;
    }

    public Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull
    {
        // GetService (nullable) so that a notification with no registered handler is a no-op.
        // Executors are only registered when a handler is registered via RegisterNotificationHandler<>.
        var pipeline = serviceProvider
            .GetService<NotificationPipelineExecutor<TMessage>>();

        if (pipeline is null) return Task.CompletedTask;
        
        return pipeline.Handle(message, DispatchNotifications, cancellationToken);
    }

    public Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull
    {
        // Fire-and-forget each notification individually — no Task.WhenAll overhead.
        // Wrap each call so that synchronous exceptions from the pipeline do not halt the loop.
        foreach (var message in messages)
        {
            try
            {
                _ = Notify(message, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Message}", ex.Message);
            }
        }

        return Task.CompletedTask;
    }
}
