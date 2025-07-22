using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

internal sealed class DecoupledCommandHandler : ICommandHandler<DecoupledValidatableMessage>
{
    public Task Handle(DecoupledValidatableMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
