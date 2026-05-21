using NetMediate.Quartz.Tests.Messages;

namespace NetMediate.Quartz.Tests.Handlers;

[Injectable]
internal sealed class ExceptionHandler : INotificationHandler<QuartzMessage>
{
    public async ValueTask Handle(QuartzMessage message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("This handler always throws.");
    }
}
