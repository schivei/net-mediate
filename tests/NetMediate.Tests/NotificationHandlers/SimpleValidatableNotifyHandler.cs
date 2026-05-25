using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

internal sealed class SimpleValidatableNotifyHandler
    : BaseHandler,
        INotificationHandler<SimpleValidatableMessage>
{
    public Task Handle(
        SimpleValidatableMessage message,
        CancellationToken cancellationToken = default
    )
    {
        Marks(message);
        return Task.CompletedTask;
    }
}
