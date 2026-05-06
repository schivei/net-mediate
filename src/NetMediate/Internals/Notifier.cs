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
        foreach (var handler in handlers)
        {
            handler.Handle(message, cancellationToken);
        }

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

        return pipeline.Handle(
            key ?? Extensions.DEFAULT_ROUTING_KEY,
            message,
            DispatchNotifications,
            cancellationToken
        );
    }

    public async Task Notify<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        foreach (var message in messages)
        {
            await Notify(key ?? Extensions.DEFAULT_ROUTING_KEY, message, cancellationToken);
        }
    }
}
