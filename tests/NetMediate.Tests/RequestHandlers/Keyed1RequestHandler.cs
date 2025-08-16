using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

[KeyedMessage("keyed1")]
internal sealed class Keyed1RequestHandler : BaseHandler, IRequestHandler<Keyed1Message, string>
{
    public Task<string> Handle(
        Keyed1Message message,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Marks(message).Name);
}
