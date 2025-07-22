using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

internal sealed class SimpleRequestHandler : IRequestHandler<SimpleMessage, string>
{
    public Task<string> Handle(SimpleMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(message.Name);
}
