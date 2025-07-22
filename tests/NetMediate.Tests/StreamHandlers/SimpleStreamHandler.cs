using NetMediate.Tests.Messages;

namespace NetMediate.Tests.StreamHandlers;

internal sealed class SimpleStreamHandler : IStreamHandler<SimpleMessage, string>
{
    public async IAsyncEnumerable<string> Handle(SimpleMessage message, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        while (!cancellationToken.IsCancellationRequested)
        {
            yield return message.Name;

            yield break;
        }
    }
}
