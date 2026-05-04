using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

internal sealed class SimpleValidatableNotifyHandler
    : BaseHandler,
        INotificationHandler<SimpleValidatableMessage>
{
    public async Task Handle(
        SimpleValidatableMessage message,
        CancellationToken cancellationToken = default
    ) => await Task.Run(() => Marks(message), cancellationToken);
}
