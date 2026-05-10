namespace NetMediate.Moq;

public class NotifierMock(IServiceProvider serviceProvider) : INotifiable
{
    private readonly Internals.Notifier _notifier = new(serviceProvider);

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
