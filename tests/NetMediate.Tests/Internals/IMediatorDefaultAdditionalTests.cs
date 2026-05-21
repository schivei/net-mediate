using NetMediate.Tests.Messages;
using System.Collections.Concurrent;

namespace NetMediate.Tests.Internals;

public sealed class IMediatorDefaultAdditionalTests
{
    private sealed class TestMediator : IMediator
    {
        public int SingleNotifyCalls;
        public readonly ConcurrentBag<object?> Notified = [];

        public void Notify<TMessage>(
            TMessage message
        )
            where TMessage : notnull
        {
            SingleNotifyCalls++;
            Notified.Add(message);
        }

        public void Notify<TMessage>(
            object? key,
            TMessage message
        ) where TMessage : notnull => Notify(message);

        public ValueTask Send<TMessage>(TMessage message, CancellationToken cancellationToken = default)
            where TMessage : notnull => ValueTask.CompletedTask;

        public ValueTask Send<TMessage>(
            object? key,
            TMessage message,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull => ValueTask.CompletedTask;

        public ValueTask Sends<TMessage>(
            IEnumerable<TMessage> messages,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull => ValueTask.CompletedTask;

        public ValueTask Sends<TMessage>(
            object? key,
            IEnumerable<TMessage> messages,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull => ValueTask.CompletedTask;

        public ValueTask<TResponse> Request<TMessage, TResponse>(
            TMessage message,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull => ValueTask.FromResult(default(TResponse)!);

        public ValueTask<TResponse> Request<TMessage, TResponse>(
            object? key,
            TMessage message,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull => ValueTask.FromResult(default(TResponse)!);

        public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
            TMessage message,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull
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
        )
            where TMessage : notnull =>
            RequestStream<TMessage, TResponse>(message, cancellationToken);

        public void Notifies<TMessage>(
            IEnumerable<TMessage> messages
        )
            where TMessage : notnull
        {
            foreach (var message in messages)
                Notify(message);
        }

        public void Notifies<TMessage>(
            object? key,
            IEnumerable<TMessage> messages
        )
            where TMessage : notnull => Notify(messages);
    }

    [Fact]
    public void Notify_Single_Interfaced_WithoutOnError_Forwards()
    {
        var msg = new MessageNotification(1);
        var m = new TestMediator();
        m.Notify(msg);
        Assert.Equal(1, m.SingleNotifyCalls);
        Assert.Contains(msg, m.Notified);
    }
}
