using NetMediate.Tests.Messages;

namespace NetMediate.Tests.StreamHandlers;

internal sealed class SimpleValidatableStreamHandler : IStreamHandler<SimpleValidatableMessage, string>
{
    public async IAsyncEnumerable<string> Handle(SimpleValidatableMessage message, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        while (!cancellationToken.IsCancellationRequested)
        {
            yield return message.Name;

            yield break;
        }
    }
}
