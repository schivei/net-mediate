using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

public sealed class MediatorNotifiesContinuationTests
{
    private readonly Mock<IServiceProvider> _provider = new();
    private readonly Mediator _sut;

    public MediatorNotifiesContinuationTests()
    {
        // Wire up an INotifiable that dispatches via the real Notifier (fire-and-forget)
        var logger = Mock.Of<ILogger<Notifier>>();

        // Set up empty pipeline behaviors by default
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<IPipelineBehavior<Msg, Task>>)))
            .Returns(Array.Empty<IPipelineBehavior<Msg, Task>>());

        // Provide PipelineExecutor for Notifier.Notify
        var executor = new PipelineExecutor<Msg, Task, INotificationHandler<Msg>>(_provider.Object);
        _provider
            .Setup(p => p.GetService(typeof(PipelineExecutor<Msg, Task, INotificationHandler<Msg>>)))
            .Returns(executor);

        var notifier = new Notifier(_provider.Object, logger);
        _sut = new Mediator(_provider.Object, notifier);
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

    private sealed class PassThroughBehavior : IPipelineBehavior<Msg, Task>
    {
        public Task Handle(
            Msg message,
            PipelineBehaviorDelegate<Msg, Task> next,
            CancellationToken cancellationToken = default
        ) => next(message.Mark(), cancellationToken);
    }

    [Fact]
    public async Task Notifies_HandlerSuccess_CheckedIsTrue()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new OkHandler() });

        await _sut.Notify(message, TestContext.Current.CancellationToken);

        // Give the fire-and-forget dispatch time to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(message.Maked);
        Assert.True(message.Checked);
    }

    [Fact]
    public async Task Notifies_HandlerFaulted_WithoutBehavior_CheckedIsFalse()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new FaultHandler() });

        // Fire-and-forget: exception is logged but not re-thrown
        await _sut.Notify(message, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(message.Maked);
        Assert.False(message.Checked);
    }

    [Fact]
    public async Task Notifies_HandlerSuccess_WithPassThroughBehavior_MakedAndCheckedAreTrue()
    {
        var message = new Msg();
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<Msg>>)))
            .Returns(new[] { new OkHandler() });
        _provider
            .Setup(p => p.GetService(typeof(IEnumerable<IPipelineBehavior<Msg, Task>>)))
            .Returns(new[] { new PassThroughBehavior() });

        await _sut.Notify(message, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.True(message.Maked);
        Assert.True(message.Checked);
    }
}
