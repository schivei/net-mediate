using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

[KeyedService(Key = "routing")]
internal sealed class KeyedRequestHandler : BaseHandler, IRequestHandler<MessageRequest, int>
{
    public async Task<int> Handle(MessageRequest query, CancellationToken cancellationToken = default) =>
        await Task.Run(() => Returns(query), cancellationToken);

    private static int Returns(MessageRequest query)
    {
        Marks(query);
        return query.CommandId;
    }
}
