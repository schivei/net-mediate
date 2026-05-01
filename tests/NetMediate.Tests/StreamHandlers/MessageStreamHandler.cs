using NetMediate.Tests.Messages;
using System.Runtime.CompilerServices;

namespace NetMediate.Tests.StreamHandlers;

internal sealed class MessageStreamHandler : BaseHandler, IStreamHandler<MessageStream, int>
{
    public async IAsyncEnumerable<int> Handle(MessageStream request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < request.CommandId; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
        }

        await Task.Run(() => Marks(request), cancellationToken);
    }
}
