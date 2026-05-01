namespace NetMediate.Internals;

internal readonly record struct Pack<TMessage>(TMessage Message, NotificationHandlerDelegate<TMessage> Notifier) : IPack where TMessage : notnull, INotification
{
    public string MessageTypeName => typeof(TMessage).Name;

    public ValueTask Dispatch(CancellationToken cancellationToken = default) => Notifier(Message, cancellationToken);
}

internal interface IPack
{
    string MessageTypeName { get; }

    ValueTask Dispatch(CancellationToken cancellationToken);
}
