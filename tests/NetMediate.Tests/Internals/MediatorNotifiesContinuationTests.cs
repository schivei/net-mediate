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

        _sut = new Mediator(
            _logger.Object,
            _cfg,
            _provider.Object,
            _scopeFactory.Object,
            new BuiltInNotificationProvider(_cfg)
        );
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
            Task.FromException(new InvalidOperationException("handler error"));
    }

    private sealed class PassThroughBehavior : INotificationBehavior<Msg>
    {
        public Task Handle(
            Msg message,
            NotificationHandlerDelegate next,
            CancellationToken cancellationToken = default
        ) => next(cancellationToken);
    }

    [Fact]
    public async Task Notifies_HandlerSuccess_CompletesWithoutException()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new OkHandler() });

        await _sut.Notifies(
            new NotificationPacket<Msg>(message),
            TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public async Task Notifies_HandlerFaulted_ThrowsAggregateException()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new FaultHandler() });

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            _sut.Notifies(
                new NotificationPacket<Msg>(message),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Contains(ex.InnerExceptions, e => e is InvalidOperationException);
    }

    [Fact]
    public async Task Notifies_HandlerFaulted_WithBehavior_ThrowsAggregateException()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new FaultHandler() });
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationBehavior<Msg>>)))
            .Returns(new[] { new PassThroughBehavior() });

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            _sut.Notifies(
                new NotificationPacket<Msg>(message),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Contains(exception.InnerExceptions, e => e is InvalidOperationException);
    }
}
