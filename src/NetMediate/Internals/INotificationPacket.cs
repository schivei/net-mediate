namespace NetMediate.Internals;

internal interface INotificationPacket
{
    object Message { get; }
    Delegate ErrorHandler { get; }

    Task OnErrorAsync(
        Type handlerType,
        Exception exception
    );
}
