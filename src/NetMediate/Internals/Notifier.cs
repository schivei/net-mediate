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

        _ = Task.WhenAll(handlers.Select(h => h.Handle(message, cancellationToken))).ContinueWith(_ => { }, cancellationToken);

        return Task.CompletedTask;
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

        _ = pipeline.Handle(
            key,
            message,
            DispatchNotifications,
            cancellationToken
        ).ContinueWith(static _ => { }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task Notify<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        _ = Task.WhenAll(
            messages.Select(m => Notify(key, m, cancellationToken))
        ).ContinueWith(static _ => { }, cancellationToken);

        return Task.CompletedTask;
    }
}
