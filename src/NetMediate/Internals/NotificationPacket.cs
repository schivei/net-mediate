using System.Diagnostics.CodeAnalysis;

namespace NetMediate.Internals;

[ExcludeFromCodeCoverage]
internal readonly record struct NotificationPacket<TMessage>(TMessage Message) : INotificationPacket
{
    object INotificationPacket.Message => Message;

    public Task DispatchAsync(INotifiable notifiable, CancellationToken cancellationToken) =>
        notifiable.NotifiesTyped(this, cancellationToken);
}
