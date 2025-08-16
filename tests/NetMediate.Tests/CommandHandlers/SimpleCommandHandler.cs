using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

internal sealed class SimpleCommandHandler : BaseHandler, ICommandHandler<SimpleMessage>
{
    public Task Handle(SimpleMessage message, CancellationToken cancellationToken = default) =>
        Task.Run(() => Marks(message), cancellationToken);
}
