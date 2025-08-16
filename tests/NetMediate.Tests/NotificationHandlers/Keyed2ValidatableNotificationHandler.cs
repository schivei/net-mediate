using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

[KeyedMessage("vkeyed2")]
internal sealed class Keyed2ValidatableNotificationHandler
    : BaseHandler,
        INotificationHandler<Keyed2ValidatableMessage>
{
    public Task Handle(
        Keyed2ValidatableMessage message,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => Marks(message), cancellationToken);
}
