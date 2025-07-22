using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

[KeyedMessage("keyed2")]
internal sealed class Keyed2RequestHandler : IRequestHandler<Keyed2Message, string>
{
    public Task<string> Handle(Keyed2Message message, CancellationToken cancellationToken = default) =>
        Task.FromResult(message.Name);
}
