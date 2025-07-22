using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

internal sealed class SimpleCommandHandler : ICommandHandler<SimpleMessage>
{
    public Task Handle(SimpleMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
