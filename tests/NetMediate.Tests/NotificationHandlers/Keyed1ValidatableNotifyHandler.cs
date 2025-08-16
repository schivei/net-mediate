using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

[KeyedMessage("vkeyed1")]
internal sealed class Keyed1ValidatableNotifyHandler
    : BaseHandler,
        INotificationHandler<Keyed1ValidatableMessage>
{
    public Task Handle(
        Keyed1ValidatableMessage message,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => Marks(message), cancellationToken);
}
