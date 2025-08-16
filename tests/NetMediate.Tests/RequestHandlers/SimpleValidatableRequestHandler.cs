using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

internal sealed class SimpleValidatableRequestHandler
    : BaseHandler,
        IRequestHandler<SimpleValidatableMessage, string>
{
    public Task<string> Handle(
        SimpleValidatableMessage message,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Marks(message).Name);
}
