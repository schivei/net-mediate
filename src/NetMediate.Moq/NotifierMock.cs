using System.Collections.Immutable;

namespace NetMediate.Moq;

public class NotifierMock : INotifiable
{
    public async Task DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        ImmutableArray<INotificationHandler<TMessage>> handlers,
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
