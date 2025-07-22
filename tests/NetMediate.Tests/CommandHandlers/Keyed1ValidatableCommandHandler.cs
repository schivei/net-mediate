using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

[KeyedMessage("vkeyed1")]
internal sealed class Keyed1ValidatableCommandHandler : ICommandHandler<Keyed1ValidatableMessage>
{
    public Task Handle(Keyed1ValidatableMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
