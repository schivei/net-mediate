using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

internal sealed class SimpleValidatableCommandHandler : ICommandHandler<SimpleValidatableMessage>
{
    public Task Handle(SimpleValidatableMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
