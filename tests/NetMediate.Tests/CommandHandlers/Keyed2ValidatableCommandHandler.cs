using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

[KeyedMessage("vkeyed2")]
internal sealed class Keyed2ValidatableCommandHandler
    : BaseHandler,
        ICommandHandler<Keyed2ValidatableMessage>
{
    public Task Handle(
        Keyed2ValidatableMessage message,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => Marks(message), cancellationToken);
}
