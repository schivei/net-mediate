using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

internal sealed class MessageCommandHandler : BaseHandler, ICommandHandler<MessageCommand>
{
    public async ValueTask Handle(MessageCommand command, CancellationToken cancellationToken = default) =>
        await Task.Run(() => Marks(command), cancellationToken);
}
