using System.Diagnostics.CodeAnalysis;

namespace NetMediate.Internals;

[ExcludeFromCodeCoverage]
internal readonly record struct NotificationPacket<TMessage>(
    TMessage Message,
    NotificationErrorDelegate<TMessage> ErrorHandler = default
) : INotificationPacket
{
    object INotificationPacket.Message => Message;
    Delegate INotificationPacket.ErrorHandler => ErrorHandler;

    public Task DispatchAsync(INotifiable notifiable, CancellationToken cancellationToken) =>
        notifiable.NotifiesTyped(this, cancellationToken);

    public async Task OnErrorAsync(Type handlerType, Exception exception) =>
        await (ErrorHandler ?? ((_, _, _) => Task.CompletedTask))(handlerType, Message, exception);
}
