using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

internal sealed class SimpleValidatableCommandHandler : BaseHandler, ICommandHandler<SimpleValidatableMessage>
{
    public Task Handle(SimpleValidatableMessage message, CancellationToken cancellationToken = default) =>
        Task.Run(() => Marks(message), cancellationToken);
}
