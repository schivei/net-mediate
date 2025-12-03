using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

internal sealed class MessageCommandHandler : BaseHandler, ICommandHandler<MessageCommand>
{
    public Task Handle(MessageCommand command, CancellationToken cancellationToken = default) =>
        Task.Run(() => Marks(command), cancellationToken);
}
