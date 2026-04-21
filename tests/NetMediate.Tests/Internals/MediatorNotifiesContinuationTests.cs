using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

public sealed class MediatorNotifiesContinuationTests
{
    private readonly Mock<ILogger<Mediator>> _logger = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IServiceProvider> _provider = new();
    private readonly Configuration _cfg;
    private readonly Mediator _sut;

    public MediatorNotifiesContinuationTests()
    {
        _cfg = new Configuration(Channel.CreateUnbounded<INotificationPacket>())
        {
            IgnoreUnhandledMessages = false,
            LogUnhandledMessages = true,
        };

        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_provider.Object);

        _sut = new Mediator(_logger.Object, _cfg, _scopeFactory.Object);
    }

    public sealed class Msg { }

    private sealed class OkHandler : INotificationHandler<Msg>
    {
        public Task Handle(Msg notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FaultHandler : INotificationHandler<Msg>
    {
        public Task Handle(Msg notification, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("x"));
    }

    [Fact]
    public async Task Notifies_HandlerSuccess_DoesNotInvokeErrorCallback()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new OkHandler() });

        var called = false;
        Task onError(Type _, Msg __, Exception ___)
        {
            called = true;
            return Task.CompletedTask;
        }

        await _sut.Notifies(
            new NotificationPacket<Msg>(message, onError),
            TestContext.Current.CancellationToken
        );
        // Give time for continuation (even though it won't run)
        await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.False(called);
    }

    [Fact]
    public async Task Notifies_HandlerFaulted_InvokesErrorCallback()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new FaultHandler() });

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task onError(Type _, Msg __, Exception ___)
        {
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        }

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            _sut.Notifies(
                new NotificationPacket<Msg>(message, onError),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Contains(exception.InnerExceptions, e => e is InvalidOperationException);

        var completed = await Task.WhenAny(
            tcs.Task,
            Task.Delay(500, TestContext.Current.CancellationToken)
        );
        Assert.Same(tcs.Task, completed);
        Assert.True(await tcs.Task);
    }
}
