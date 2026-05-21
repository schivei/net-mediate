using NetMediate.Quartz.Tests.Messages;

namespace NetMediate.Quartz.Tests.Handlers;

[Injectable(Key = "keyed")]
internal sealed class KeyedHandler : INotificationHandler<TestMessage>
{
    public ValueTask Handle(TestMessage message, CancellationToken cancellationToken = default)
    {
        message.CheckValue = message.Value;
        return ValueTask.CompletedTask;
    }
}
