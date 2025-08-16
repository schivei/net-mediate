using NetMediate.Tests.Messages;
using System.Runtime.CompilerServices;

namespace NetMediate.Tests.StreamHandlers;

[KeyedMessage("vkeyed1")]
internal sealed class Keyed1ValidatableStreamHandler : BaseHandler, IStreamHandler<Keyed1ValidatableMessage, string>
{
    public async IAsyncEnumerable<string> Handle(Keyed1ValidatableMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
