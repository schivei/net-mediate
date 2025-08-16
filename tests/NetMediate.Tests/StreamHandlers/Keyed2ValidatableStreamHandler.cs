using NetMediate.Tests.Messages;
using System.Runtime.CompilerServices;

namespace NetMediate.Tests.StreamHandlers;

[KeyedMessage("vkeyed2")]
internal sealed class Keyed2ValidatableStreamHandler : BaseHandler, IStreamHandler<Keyed2ValidatableMessage, string>
{
    public async IAsyncEnumerable<string> Handle(Keyed2ValidatableMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
