using NetMediate.Tests.Messages;

namespace NetMediate.Tests.NotificationHandlers;

internal sealed class DecoupledNotifyHandler : INotificationHandler<DecoupledValidatableMessage>
{
    public Task Handle(DecoupledValidatableMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
