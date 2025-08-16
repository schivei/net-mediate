using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

internal sealed class DecoupledNotifyHandler : BaseHandler, INotificationHandler<DecoupledValidatableMessage>
{
    public Task Handle(DecoupledValidatableMessage message, CancellationToken cancellationToken = default) =>
        Task.Run(() => Marks(message), cancellationToken);
}
