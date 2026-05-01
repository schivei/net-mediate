using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

internal sealed class SimpleValidatableNotifyHandler
    : BaseHandler,
        INotificationHandler<SimpleValidatableMessage>
{
    public async ValueTask Handle(
        SimpleValidatableMessage message,
        CancellationToken cancellationToken = default
    ) => await Task.Run(() => Marks(message), cancellationToken);
}
