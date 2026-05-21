using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

internal sealed class MessageRequestHandler : BaseHandler, IRequestHandler<MessageRequest, int>
{
    public ValueTask<int> Handle(
        MessageRequest query,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(Returns(query));

    private static int Returns(MessageRequest query)
    {
        Marks(query);
        return query.CommandId;
    }
}
