using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

internal sealed class MessageNotificationHandler : BaseHandler, INotificationHandler<MessageNotification>
{
    public async ValueTask Handle(MessageNotification notification, CancellationToken cancellationToken = default) =>
        await Task.Run(() => Marks(notification), cancellationToken);
}
