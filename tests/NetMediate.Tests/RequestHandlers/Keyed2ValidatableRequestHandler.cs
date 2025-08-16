using NetMediate.Tests.Messages;

namespace NetMediate.Tests.RequestHandlers;

[KeyedMessage("vkeyed2")]
internal sealed class Keyed2ValidatableRequestHandler : BaseHandler, IRequestHandler<Keyed2ValidatableMessage, string>
{
    public Task<string> Handle(Keyed2ValidatableMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(Marks(message).Name);
}
