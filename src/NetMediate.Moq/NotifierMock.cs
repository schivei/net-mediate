namespace NetMediate.Moq;

public class NotifierMock : INotifiable
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
}
