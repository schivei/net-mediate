using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

/// <summary>
/// Tests for the internal <see cref="Notifier"/> (production notifier),
/// distinct from <see cref="NetMediate.Moq.Notifier"/> which is used in unit tests.
/// </summary>
public class InternalNotifierTests
{
    public record TestNotification;

    private static Notifier BuildNotifier(
        INotificationHandler<TestNotification>[] handlers,
        ILogger<Notifier>? logger = null)
    {
        var spMock = new Mock<IServiceProvider>();
        spMock.Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<TestNotification>>)))
              .Returns(handlers);
        spMock.Setup(p => p.GetService(typeof(IEnumerable<IPipelineBehavior<TestNotification, Task>>)))
              .Returns(Array.Empty<IPipelineBehavior<TestNotification, Task>>());

        // Provide a real PipelineExecutor wired to the mock SP
        var executor = new PipelineExecutor<TestNotification, Task, INotificationHandler<TestNotification>>(spMock.Object);
        spMock.Setup(p => p.GetService(typeof(PipelineExecutor<TestNotification, Task, INotificationHandler<TestNotification>>)))
              .Returns(executor);

        var resolvedLogger = logger ?? Mock.Of<ILogger<Notifier>>();
        return new Notifier(spMock.Object, resolvedLogger);
    }

    [Fact]
    public async Task DispatchNotifications_WithHandler_InvokesHandler()
    {
        // Arrange
        var handlerMock = new Mock<INotificationHandler<TestNotification>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestNotification>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var notifier = BuildNotifier([handlerMock.Object]);
        var message = new TestNotification();

        // Act
        await notifier.DispatchNotifications(message, [handlerMock.Object], TestContext.Current.CancellationToken);

        // Assert — handler was invoked
        handlerMock.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchNotifications_WithNoHandlers_CompletesWithoutInvoking()
    {
        // Arrange — no handlers → early return
        var notifier = BuildNotifier([]);
        var message = new TestNotification();

        // Act & Assert — no exception
        await notifier.DispatchNotifications(message, [], TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Notify_SingleMessage_DispatchesViaNotifier()
    {
        // Arrange
        var handlerMock = new Mock<INotificationHandler<TestNotification>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestNotification>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var notifier = BuildNotifier([handlerMock.Object]);
        var message = new TestNotification();

        // Act
        await notifier.Notify(message, TestContext.Current.CancellationToken);

        // Give the fire-and-forget dispatch a moment to complete
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert — handler was invoked via the pipeline
        handlerMock.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Notify_Enumerable_DispatchesAllMessages()
    {
        // Arrange
        var handlerMock = new Mock<INotificationHandler<TestNotification>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestNotification>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var notifier = BuildNotifier([handlerMock.Object]);
        TestNotification[] messages = [new(), new()];

        // Act
        await notifier.Notify(messages, TestContext.Current.CancellationToken);

        // Give the fire-and-forget dispatch a moment to complete
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert — handler was invoked for each message
        handlerMock.Verify(h => h.Handle(It.IsAny<TestNotification>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
