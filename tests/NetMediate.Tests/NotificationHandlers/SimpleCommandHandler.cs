using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

internal sealed class SimpleNotifyHandler : INotificationHandler<SimpleMessage>
{
    public Task Handle(SimpleMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
