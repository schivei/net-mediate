using NetMediate.Tests.Messages;

namespace NetMediate.Tests.StreamHandlers;

internal sealed class DecoupledStreamHandler : IStreamHandler<DecoupledValidatableMessage, string>
{
    public async IAsyncEnumerable<string> Handle(DecoupledValidatableMessage message, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        while(!cancellationToken.IsCancellationRequested)
        {
            yield return message.Name;

            yield break;
        }
    }
}
