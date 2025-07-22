using NetMediate.Tests.Messages;

namespace NetMediate.Tests.StreamHandlers;

[KeyedMessage("keyed1")]
internal sealed class Keyed1StreamHandler : IStreamHandler<Keyed1Message, string>
{
    public async IAsyncEnumerable<string> Handle(Keyed1Message message, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        while (!cancellationToken.IsCancellationRequested)
        {
            yield return message.Name;

            yield break;
        }
    }
}
