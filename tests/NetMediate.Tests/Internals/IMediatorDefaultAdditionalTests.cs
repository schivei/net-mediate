using NetMediate.Tests.Messages;
using System.Collections.Concurrent;

namespace NetMediate.Tests.Internals;

public sealed class IMediatorDefaultAdditionalTests
{
    private sealed class TestMediator : IMediator
    {
        public int SingleNotifyCalls;
        public readonly ConcurrentBag<object?> Notified = [];

        public Task Notify<TMessage>(
            TMessage message,
            CancellationToken cancellationToken = default
        )
        {
            SingleNotifyCalls++;
            Notified.Add(message);
            return Task.CompletedTask;
        }

        public Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<TResponse> Request<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(default(TResponse)!);

        public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default)
        {
            return GetAsync();
            static async IAsyncEnumerable<TResponse> GetAsync() { await Task.CompletedTask; yield break; }
        }

        Task IMediator.Notify<TMessage>(INotification<TMessage> notification, CancellationToken cancellationToken) =>
            Notify((TMessage)notification, cancellationToken);

        Task IMediator.Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken)
        {
            if (messages is null) return Task.CompletedTask;
            var arr = messages as TMessage[] ?? messages.ToArray();
            if (arr.Length == 0) return Task.CompletedTask;
            return Task.WhenAll(arr.Select(m => Notify(m, cancellationToken)));
        }

        Task IMediator.Notify<TMessage>(IEnumerable<INotification<TMessage>> notifications, CancellationToken cancellationToken)
        {
            if (notifications is null) return Task.CompletedTask;
            var arr = notifications as INotification<TMessage>[] ?? notifications.ToArray();
            if (arr.Length == 0) return Task.CompletedTask;
            return Task.WhenAll(arr.Select(n => Notify((TMessage)n, cancellationToken)));
        }

        Task IMediator.Send<TMessage>(ICommand<TMessage> command, CancellationToken cancellationToken) =>
            Send((TMessage)command, cancellationToken);

        Task<TResponse> IMediator.Request<TMessage, TResponse>(IRequest<TMessage, TResponse> request, CancellationToken cancellationToken) =>
            Request<TMessage, TResponse>((TMessage)request, cancellationToken);

        IAsyncEnumerable<TResponse> IMediator.RequestStream<TMessage, TResponse>(IStream<TMessage, TResponse> request, CancellationToken cancellationToken) =>
            RequestStream<TMessage, TResponse>((TMessage)request, cancellationToken);
    }

    private static readonly int[] integerMessages = [1, 2, 3];
    private static readonly string[] stringMessages = ["a", "b"];

    [Fact]
    public async Task Notify_Enumerable_Null_DoesNothing()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify<string>(messages: null!, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, m.SingleNotifyCalls);
    }

    [Fact]
    public async Task Notify_Enumerable_Empty_DoesNothing()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify(messages: Array.Empty<string>(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, m.SingleNotifyCalls);
    }

    [Fact]
    public async Task Notify_Enumerable_TwoItems_CallsTwice()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify(messages: stringMessages, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, m.SingleNotifyCalls);
        Assert.Contains("a", m.Notified);
        Assert.Contains("b", m.Notified);
    }

    [Fact]
    public async Task Notify_Single_ForwardsOnce()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify("x", TestContext.Current.CancellationToken);
        Assert.Equal(1, m.SingleNotifyCalls);
        Assert.Contains("x", m.Notified);
    }

    [Fact]
    public async Task Notify_Enumerable_Multiple_Forwards()
    {
        var m = new TestMediator();
        await ((IMediator)m).Notify(messages: integerMessages, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, m.SingleNotifyCalls);
        Assert.Contains(1, m.Notified);
        Assert.Contains(2, m.Notified);
        Assert.Contains(3, m.Notified);
    }

    [Fact]
    public async Task Notify_Interfaced_Forwards()
    {
        var msg = new MessageNotification(1);
        var m = new TestMediator();
        await ((IMediator)m).Notify(msg, TestContext.Current.CancellationToken);
        Assert.Equal(1, m.SingleNotifyCalls);
        Assert.Contains(msg, m.Notified);
    }

    [Fact]
    public async Task Notify_Enumerable_SinglePassEnumerable_Forwards()
    {
        var m = new TestMediator();
        var messages = new SinglePassEnumerable<string>(stringMessages);
        await ((IMediator)m).Notify(messages: messages, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, m.SingleNotifyCalls);
        Assert.Contains("a", m.Notified);
        Assert.Contains("b", m.Notified);
    }

    [Fact]
    public async Task Notify_NotificationEnumerable_SinglePassEnumerable_Forwards()
    {
        var m = new TestMediator();
        var notifications = new SinglePassEnumerable<INotification<MessageNotification>>(
            [new MessageNotification(1), new MessageNotification(2)]
        );
        await ((IMediator)m).Notify(notifications: notifications, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, m.SingleNotifyCalls);
        Assert.Equal(2, m.Notified.OfType<MessageNotification>().Count());
    }

    [Fact]
    public async Task Notify_IntEnumerable_SinglePassEnumerable_Forwards()
    {
        var m = new TestMediator();
        var messages = new SinglePassEnumerable<int>(integerMessages);
        await ((IMediator)m).Notify(messages: messages, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, m.SingleNotifyCalls);
        Assert.Contains(1, m.Notified);
        Assert.Contains(2, m.Notified);
        Assert.Contains(3, m.Notified);
    }
}
