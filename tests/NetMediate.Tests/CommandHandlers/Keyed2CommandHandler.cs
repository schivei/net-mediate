using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

[KeyedMessage("keyed2")]
internal sealed class Keyed2CommandHandler : ICommandHandler<Keyed2Message>
{
    public Task Handle(Keyed2Message message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
