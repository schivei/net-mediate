using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

internal sealed class DecoupledRequestHandler : IRequestHandler<DecoupledValidatableMessage, string>
{
    public Task<string> Handle(DecoupledValidatableMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(message.Name);
}
