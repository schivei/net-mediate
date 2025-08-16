using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

[KeyedMessage("keyed2")]
internal sealed class Keyed2NotifyHandler : BaseHandler, INotificationHandler<Keyed2Message>
{
    public Task Handle(Keyed2Message message, CancellationToken cancellationToken = default) =>
        Task.Run(() => Marks(message), cancellationToken);
}
