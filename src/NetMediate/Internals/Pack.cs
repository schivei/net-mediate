namespace NetMediate.Internals;

internal readonly record struct Pack<TMessage>(TMessage Message, NotificationHandlerDelegate<TMessage> Notifier) : IPack where TMessage : notnull, INotification
{
    public ValueTask Dispatch(CancellationToken cancellationToken = default) => Notifier(Message, cancellationToken);
}

internal interface IPack
{
    ValueTask Dispatch(CancellationToken cancellationToken);
}
