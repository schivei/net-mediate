using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

internal sealed class DecoupledCommandHandler
    : BaseHandler,
        ICommandHandler<DecoupledValidatableMessage>
{
    public Task Handle(
        DecoupledValidatableMessage message,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => Marks(message), cancellationToken);
}
