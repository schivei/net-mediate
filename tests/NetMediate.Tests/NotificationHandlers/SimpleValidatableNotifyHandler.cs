using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

internal sealed class SimpleValidatableNotifyHandler : INotificationHandler<SimpleValidatableMessage>
{
    public Task Handle(SimpleValidatableMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
