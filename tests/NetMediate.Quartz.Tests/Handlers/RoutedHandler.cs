using NetMediate.Quartz.Tests.Messages;

namespace NetMediate.Quartz.Tests.Handlers;

[Injectable(Key = "my_routing")]
internal sealed class RoutedHandler : INotificationHandler<QuartzMessage>
{
    public ValueTask Handle(QuartzMessage message, CancellationToken cancellationToken = default)
    {
        message.CheckValue = message.Value;
        return ValueTask.CompletedTask;
    }
}
