using NetMediate.Tests.Messages;

namespace NetMediate.Tests.StreamHandlers;

[KeyedMessage("vkeyed2")]
internal sealed class Keyed2ValidatableStreamHandler : IStreamHandler<Keyed2ValidatableMessage, string>
{
    public async IAsyncEnumerable<string> Handle(Keyed2ValidatableMessage message, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        while (!cancellationToken.IsCancellationRequested)
        {
            yield return message.Name;

            yield break;
        }
    }
}
