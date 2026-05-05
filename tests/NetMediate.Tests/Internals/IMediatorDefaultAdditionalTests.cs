using NetMediate.Tests.Messages;
using System.Collections.Concurrent;

namespace NetMediate.Tests.Internals;

public sealed class IMediatorDefaultAdditionalTests
{
    private sealed class TestMediator : IMediator
    {
        public int SingleNotifyCalls;
        public readonly ConcurrentBag<object?> Notified = [];

        public async Task Notify<TMessage>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull
        {
            SingleNotifyCalls++;
            Notified.Add(message);
            await Task.CompletedTask;
        }

        public Task Notify<TMessage>(
            object? key,
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull => Notify(message, cancellationToken);

        public Task Send<TMessage>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull => Task.CompletedTask;

        public Task Send<TMessage>(
            object? key,
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull => Task.CompletedTask;

        public Task Send<TMessage>(IEnumerable<TMessage> commands, CancellationToken cancellationToken = default) where TMessage : notnull =>
            Task.CompletedTask;

        public Task Send<TMessage>(object? key, IEnumerable<TMessage> commands, CancellationToken cancellationToken = default) where TMessage : notnull =>
            Task.CompletedTask;

        public Task<TResponse> Request<TMessage, TResponse>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull => Task.FromResult(default(TResponse)!);

        public Task<TResponse> Request<TMessage, TResponse>(
            object? key,
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull => Task.FromResult(default(TResponse)!);

        public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull
        {
            return GetAsync();
            static async IAsyncEnumerable<TResponse> GetAsync()
            {
                await Task.CompletedTask;
                yield break;
            }
        }

        public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
            object? key,
            TMessage message,
            CancellationToken cancellationToken = default
        ) where TMessage : notnull => RequestStream<TMessage, TResponse>(message, cancellationToken);

        public async Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull =>
            await Task.WhenAll(messages.Select(m => Notify(m, cancellationToken)));

        public async Task Notify<TMessage>(object? key, IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull =>
            await Task.WhenAll(messages.Select(m => Notify(key, m, cancellationToken)));
    }

    [Fact]
    public async Task Notify_Single_Interfaced_WithoutOnError_Forwards()
    {
        var msg = new MessageNotification(1);
        var m = new TestMediator();
        await m.Notify(msg, TestContext.Current.CancellationToken);
        Assert.Equal(1, m.SingleNotifyCalls);
        Assert.Contains(msg, m.Notified);
    }

    [Fact]
    public async Task Notify_NotificationEnumerable_Forwards()
    {
        var m = new TestMediator();
        MessageNotification[] notifications = [new(1), new(2)];

        await m.Notify(
            (IEnumerable<MessageNotification>)notifications,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, m.SingleNotifyCalls);
        Assert.Equal(2, m.Notified.OfType<MessageNotification>().Count());
    }
}
