using NetMediate.Internals;

namespace NetMediate.Moq;

public class Notifier(IServiceProvider serviceProvider) : INotifiable
{
    private readonly Internals.Notifier _notifier = new(serviceProvider);

    public async Task DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        foreach (var handler in handlers)
        {
            await handler.Handle(message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task Notify<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull => _notifier.Notify(key, message, cancellationToken);

    /// <inheritdoc />
    public Task Notify<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull => _notifier.Notify(key, messages, cancellationToken);
}
