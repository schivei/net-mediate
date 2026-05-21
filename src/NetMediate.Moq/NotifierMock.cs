namespace NetMediate.Moq;

public class NotifierMock : INotifiable
{
    public async ValueTask DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        if (handlers.Length == 0)
            return;

        foreach (var handler in handlers)
            await handler.Handle(message, cancellationToken);
    }
}
