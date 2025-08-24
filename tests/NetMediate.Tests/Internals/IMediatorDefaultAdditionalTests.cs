using System.Collections.Concurrent;

namespace NetMediate.Tests.Internals;

public sealed class IMediatorDefaultAdditionalTests
{
    private sealed class TestMediator : IMediator
    {
        public int SingleNotifyCalls;
        public readonly ConcurrentBag<object?> Notified = [];

        public Task Notify<TMessage>(TMessage message, NotificationErrorDelegate<TMessage> onError, CancellationToken cancellationToken = default)
        {
            SingleNotifyCalls++;
            Notified.Add(message);
            return Task.CompletedTask;
        }

        public Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<TResponse> Request<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default) => Task.FromResult(default(TResponse)!);

        public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default)
        {
            return GetAsync();
            static async IAsyncEnumerable<TResponse> GetAsync()
            {
                await Task.CompletedTask;
                yield break;
            }
        }
    }

    private static readonly int[] integerMessages = [1, 2, 3];
    private static readonly string[] stringMessages = ["a", "b"];

    [Fact]
    public async Task Notify_Enumerable_WithOnError_Null_DoesNothing()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify<string>(messages: null!);
        Assert.Equal(0, m.SingleNotifyCalls);
    }

    [Fact]
    public async Task Notify_Enumerable_WithOnError_Empty_DoesNothing()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify(messages: Array.Empty<string>());
        Assert.Equal(0, m.SingleNotifyCalls);
    }

    [Fact]
    public async Task Notify_Enumerable_WithOnError_TwoItems_CallsTwice()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify(messages: stringMessages);
        Assert.Equal(2, m.SingleNotifyCalls);
        Assert.Contains("a", m.Notified);
        Assert.Contains("b", m.Notified);
    }

    [Fact]
    public async Task Notify_Single_WithoutOnError_ForwardsOnce()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify("x");
        Assert.Equal(1, m.SingleNotifyCalls);
        Assert.Contains("x", m.Notified);
    }

    [Fact]
    public async Task Notify_Enumerable_WithoutOnError_Null_And_Empty_DoesNothing()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify<string>(messages: null!);
        await ((IMediator)m).Notify(messages: Array.Empty<string>());
        Assert.Equal(0, m.SingleNotifyCalls);
    }

    [Fact]
    public async Task Notify_Enumerable_WithoutOnError_Multiple_Forwards()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify(messages: integerMessages);
        Assert.Equal(3, m.SingleNotifyCalls);
        Assert.Contains(1, m.Notified);
        Assert.Contains(2, m.Notified);
        Assert.Contains(3, m.Notified);
    }
}