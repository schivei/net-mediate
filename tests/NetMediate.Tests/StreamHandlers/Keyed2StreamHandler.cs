using System.Runtime.CompilerServices;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests.StreamHandlers;

[KeyedMessage("keyed2")]
internal sealed class Keyed2StreamHandler : BaseHandler, IStreamHandler<Keyed2Message, string>
{
    public async IAsyncEnumerable<string> Handle(
        Keyed2Message message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
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
