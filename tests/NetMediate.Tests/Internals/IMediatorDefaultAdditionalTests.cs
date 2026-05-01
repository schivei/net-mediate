using NetMediate.Tests.Messages;
using System.Collections.Concurrent;

namespace NetMediate.Tests.Internals;

public sealed class IMediatorDefaultAdditionalTests
{
    private sealed class TestMediator : IMediator
    {
        public int SingleNotifyCalls;
        public readonly ConcurrentBag<object?> Notified = [];

        public async ValueTask Notify<TMessage>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull, INotification
        {
            SingleNotifyCalls++;
            Notified.Add(message);
            await Task.CompletedTask;
        }

        public ValueTask Send<TMessage>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull, ICommand => ValueTask.CompletedTask;

        public ValueTask<TResponse> Request<TMessage, TResponse>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull, IRequest<TResponse> => ValueTask.FromResult(default(TResponse)!);

        public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull, IStream<TResponse>
        {
            return GetAsync();
            static async IAsyncEnumerable<TResponse> GetAsync()
            {
                await Task.CompletedTask;
                yield break;
            }
        }

        public async ValueTask Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull, INotification =>
            await Task.WhenAll(messages.Select(m => Notify(m, cancellationToken).AsTask()));
    }

    [Fact]
    public async Task Notify_Single_Interfaced_WithoutOnError_Forwards()
    {
        var msg = new MessageNotification(1);
        var m = new TestMediator();
        await ((IMediator)m).Notify(msg, TestContext.Current.CancellationToken);
        Assert.Equal(1, m.SingleNotifyCalls);
        Assert.Contains(msg, m.Notified);
    }

    [Fact]
    public async Task Notify_NotificationEnumerable_Forwards()
    {
        var m = new TestMediator();
        MessageNotification[] notifications = [new MessageNotification(1), new MessageNotification(2)];

        await m.Notify(
            notifications,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, m.SingleNotifyCalls);
        Assert.Equal(2, m.Notified.OfType<MessageNotification>().Count());
    }
}
