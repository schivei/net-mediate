using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

internal sealed class SimpleRequestHandler : BaseHandler, IRequestHandler<SimpleMessage, string>
{
    public Task<string> Handle(SimpleMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(Marks(message).Name);
}
