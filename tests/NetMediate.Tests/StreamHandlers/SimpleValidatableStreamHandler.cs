using NetMediate.Tests.Messages;
using System.Runtime.CompilerServices;

namespace NetMediate.Tests.StreamHandlers;

internal sealed class SimpleValidatableStreamHandler : BaseHandler, IStreamHandler<SimpleValidatableMessage, string>
{
    public async IAsyncEnumerable<string> Handle(SimpleValidatableMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
