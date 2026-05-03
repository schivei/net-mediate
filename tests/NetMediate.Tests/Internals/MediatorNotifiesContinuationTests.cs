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
        _cfg = new Configuration(Channel.CreateUnbounded<IPack>())
        {
            IgnoreUnhandledMessages = true,
        };

        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_provider.Object);

        _sut = new Mediator(_cfg, _scopeFactory.Object, new Moq.Notifier(_scopeFactory.Object), _logger.Object);
    }

    public sealed class Msg : INotification
    {
        public bool Maked { get; private set; }

        public bool Checked { get; private set; }

        public Msg Mark()
        {
            Maked = true;
            return this;
        }

        public void Check()
        {
            Checked = true;
        }
    }

    private sealed class OkHandler : INotificationHandler<Msg>
    {
        public async Task Handle(Msg notification, CancellationToken cancellationToken = default)
        {
            notification.Check();
            await Task.CompletedTask;
        }
    }

    private sealed class FaultHandler : INotificationHandler<Msg>
    {
        public Task Handle(Msg notification, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("x"));
    }

    private sealed class PassThroughBehavior : INotificationBehavior<Msg>
    {
        public Task Handle(
            Msg message,
            NotificationHandlerDelegate<Msg> next,
            CancellationToken cancellationToken = default
        ) => next(message.Mark(), cancellationToken);
    }

    [Fact]
    public async Task Notifies_HandlerSuccess_DoesNotInvokeErrorCallback()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new OkHandler() });

        await _sut.Notify(
            message,
            TestContext.Current.CancellationToken
        );

        Assert.False(message.Maked);
        Assert.True(message.Checked);
    }

    [Fact]
    public async Task Notifies_HandlerFaulted_InvokesErrorCallback_WithoutNotificationBehavior()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new FaultHandler() });

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await _sut.Notify(
                message,
                TestContext.Current.CancellationToken
            );
        });

        Assert.False(message.Maked);
        Assert.False(message.Checked);
    }

    [Fact]
    public async Task Notifies_HandlerFaulted_WithNotificationBehavior_ThrowsAndInvokesErrorCallback()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new FaultHandler() });
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationBehavior<Msg>>)))
            .Returns(new[] { new PassThroughBehavior() });

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await _sut.Notify(
                message,
                TestContext.Current.CancellationToken
            );
        });

        Assert.True(message.Maked);
        Assert.False(message.Checked);
    }

    [Fact]
    public async Task Notifies_HandlerFaulted_WithNotificationBehavior_SuccessCallback()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new OkHandler() });
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationBehavior<Msg>>)))
            .Returns(new[] { new PassThroughBehavior() });

        await _sut.Notify(
            message,
            TestContext.Current.CancellationToken
        );

        Assert.True(message.Maked);
        Assert.True(message.Checked);
    }
}
