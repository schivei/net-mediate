using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

[KeyedMessage("vkeyed1")]
internal sealed class Keyed1ValidatableRequestHandler
    : BaseHandler,
        IRequestHandler<Keyed1ValidatableMessage, string>
{
    public Task<string> Handle(
        Keyed1ValidatableMessage message,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Marks(message).Name);
}
