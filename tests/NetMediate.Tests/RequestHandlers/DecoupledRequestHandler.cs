using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

internal sealed class DecoupledRequestHandler : BaseHandler, IRequestHandler<DecoupledValidatableMessage, string>
{
    public Task<string> Handle(DecoupledValidatableMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(Marks(message).Name);
}
