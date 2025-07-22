using NetMediate.Tests.Messages;

namespace NetMediate.Tests.StreamHandlers;

[KeyedMessage("vkeyed1")]
internal sealed class Keyed1ValidatableStreamHandler : IStreamHandler<Keyed1ValidatableMessage, string>
{
    public async IAsyncEnumerable<string> Handle(Keyed1ValidatableMessage message, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        while (!cancellationToken.IsCancellationRequested)
        {
            yield return message.Name;

            yield break;
        }
    }
}
