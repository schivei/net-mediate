using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

internal class Notifier(IServiceProvider serviceProvider, ILogger<Notifier> logger) : INotifiable
{
    public virtual Task DispatchNotifications<TMessage>(object? key, TMessage message, INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        foreach (var handler in handlers)
        {
            handler.Handle(message, cancellationToken)
                .ContinueWith(
                    t => logger.LogError(t.Exception, "{Message}", t.Exception!.Message),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
        }

        return Task.CompletedTask;
    }

    public Task Notify<TMessage>(object? key, TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull
    {
        var pipeline = serviceProvider
            .GetService<NotificationPipelineExecutor<TMessage>>();

        if (pipeline is null) return Task.CompletedTask;
        
        return pipeline.Handle(key, message, DispatchNotifications, cancellationToken);
    }

    public Task Notify<TMessage>(object? key, IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull
    {
        foreach (var message in messages)
        {
            try
            {
                _ = Notify(key, message, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Message}", ex.Message);
            }
        }

        return Task.CompletedTask;
    }
}
