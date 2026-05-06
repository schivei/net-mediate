using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal class Notifier(IServiceProvider serviceProvider) : INotifiable
{
    public Task DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        if (handlers.Length == 0)
            return Task.CompletedTask;

        return Task.WhenAll(handlers.Select(h => h.Handle(message, cancellationToken)));
    }

    public Task Notify<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        var pipeline = serviceProvider.GetService<NotificationPipelineExecutor<TMessage>>();

        if (pipeline is null)
            return Task.CompletedTask;

        return pipeline.Handle(
            key ?? Extensions.DEFAULT_ROUTING_KEY,
            message,
            DispatchNotifications,
            cancellationToken
        );
    }

    public Task Notify<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        return Task.WhenAll(
            messages.Select(m => Notify(key ?? Extensions.DEFAULT_ROUTING_KEY, m, cancellationToken))
        );
    }
}
