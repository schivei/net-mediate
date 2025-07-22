using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

[KeyedMessage("keyed1")]
internal sealed class Keyed1NotifyHandler : INotificationHandler<Keyed1Message>
{
    public Task Handle(Keyed1Message message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
