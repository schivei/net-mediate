using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

internal sealed class MessageRequestHandler : BaseHandler, IRequestHandler<MessageRequest, int>
{
    public Task<int> Handle(MessageRequest query, CancellationToken cancellationToken = default) =>
        Task.Run(() => Returns(query));

    private static int Returns(MessageRequest query)
    {
        Marks(query);
        return query.CommandId;
    }
}
