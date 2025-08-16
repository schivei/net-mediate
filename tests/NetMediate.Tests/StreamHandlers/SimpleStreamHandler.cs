using NetMediate.Tests.Messages;
using System.Runtime.CompilerServices;

namespace NetMediate.Tests.StreamHandlers;

internal sealed class SimpleStreamHandler : BaseHandler, IStreamHandler<SimpleMessage, string>
{
    public async IAsyncEnumerable<string> Handle(SimpleMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        while (!cancellationToken.IsCancellationRequested)
        {
            yield return message.Name;

            yield break;
        }

        Marks(message);
    }
}
