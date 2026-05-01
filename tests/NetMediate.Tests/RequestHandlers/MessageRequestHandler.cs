using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

internal sealed class MessageRequestHandler : BaseHandler, IRequestHandler<MessageRequest, int>
{
    public async ValueTask<int> Handle(MessageRequest query, CancellationToken cancellationToken = default) =>
        await Task.Run(() => Returns(query), cancellationToken);

    private static int Returns(MessageRequest query)
    {
        Marks(query);
        return query.CommandId;
    }
}
