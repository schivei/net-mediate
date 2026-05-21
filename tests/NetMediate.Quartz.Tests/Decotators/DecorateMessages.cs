using NetMediate.Quartz.Tests.Messages;
using System.Threading.Channels;

namespace NetMediate.Quartz.Tests.Decotators;

[DecoratorFor<INotificationHandler<TestMessage>>]
internal sealed class DecorateKeyedTestMessage : INotificationHandler<TestMessage>
{
    [Inject(Key = "keyed")] internal required ChannelWriter<TestMessage> Channel { get; init; }

    [Inject] internal required INotificationHandler<TestMessage> Inner { get; init; }

    public async ValueTask Handle(TestMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await Inner.Handle(message, cancellationToken);
            await Channel.WriteAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            Channel.Complete(ex);
        }
    }
}
