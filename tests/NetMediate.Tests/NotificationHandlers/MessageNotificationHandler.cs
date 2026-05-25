using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

internal sealed class MessageNotificationHandler
    : BaseHandler,
        INotificationHandler<MessageNotification>
{
    public Task Handle(
        MessageNotification notification,
        CancellationToken cancellationToken = default
    )
    {
        Marks(notification);
        return Task.CompletedTask;
    }
}
