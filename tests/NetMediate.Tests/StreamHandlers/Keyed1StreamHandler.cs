using NetMediate.Tests.Messages;
using System.Runtime.CompilerServices;

namespace NetMediate.Tests.StreamHandlers;

[KeyedMessage("keyed1")]
internal sealed class Keyed1StreamHandler : BaseHandler, IStreamHandler<Keyed1Message, string>
{
    public async IAsyncEnumerable<string> Handle(Keyed1Message message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
