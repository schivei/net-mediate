using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

[KeyedMessage("keyed1")]
internal sealed class Keyed1CommandHandler : ICommandHandler<Keyed1Message>
{
    public Task Handle(Keyed1Message message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
