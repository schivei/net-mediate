using NetMediate.Tests.Messages;

namespace NetMediate.Tests.CommandHandlers;

internal sealed class MessageCommandHandler : BaseHandler, ICommandHandler<MessageCommand>
{
    public ValueTask Handle(
        MessageCommand command,
        CancellationToken cancellationToken = default
    )
    {
        Marks(command);
        return ValueTask.CompletedTask;
    }
}
