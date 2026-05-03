using Microsoft.Extensions.DependencyInjection;
using Moq;
using NetMediate.Internals;
using System.Threading.Channels;

namespace NetMediate.Tests.Internals;

/// <summary>
/// Tests for the internal <see cref="Notifier"/> (production channel-based notifier),
/// distinct from <see cref="NetMediate.Moq.Notifier"/> which is used in unit tests.
/// </summary>
public class InternalNotifierTests
{
    public record TestNotification;

    private static (Notifier notifier, Channel<IPack> channel) BuildNotifier(
        INotificationHandler<TestNotification>[] handlers,
        IValidationHandler<TestNotification>[] validationHandlers)
    {
        var channel = Channel.CreateUnbounded<IPack>();
        var config = new Configuration(channel);

        var spMock = new Mock<IServiceProvider>();
        spMock.Setup(p => p.GetService(typeof(IEnumerable<INotificationHandler<TestNotification>>)))
              .Returns(handlers);
        spMock.Setup(p => p.GetService(typeof(IEnumerable<IValidationHandler<TestNotification>>)))
              .Returns(validationHandlers);
        spMock.Setup(p => p.GetService(typeof(IEnumerable<INotificationBehavior<TestNotification>>)))
              .Returns(Array.Empty<INotificationBehavior<TestNotification>>());

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(spMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return (new Notifier(config, scopeFactoryMock.Object), channel);
    }

    [Fact]
    public async Task DispatchNotifications_WithHandler_InvokesHandler()
    {
        // Arrange
        var handlerMock = new Mock<INotificationHandler<TestNotification>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestNotification>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var (notifier, _) = BuildNotifier([handlerMock.Object], []);
        var message = new TestNotification();

        // Act
        await notifier.DispatchNotifications(message, TestContext.Current.CancellationToken);

        // Assert — handler was invoked (covers the normal Notifier.DispatchNotifications path + finally)
        handlerMock.Verify(h => h.Handle(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchNotifications_WhenHandlerThrows_PropagatesAndCoversExceptionPath()
    {
        // Arrange
        var handlerMock = new Mock<INotificationHandler<TestNotification>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestNotification>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("handler failure"));

        var (notifier, _) = BuildNotifier([handlerMock.Object], []);
        var message = new TestNotification();

        // Act & Assert — covers the catch(Exception ex) block (activity?.SetStatus) and re-throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => notifier.DispatchNotifications(message, TestContext.Current.CancellationToken).AsTask()
        );
    }

    [Fact]
    public async Task DispatchNotifications_WithNoHandlers_CompletesWithoutInvoking()
    {
        // Arrange — no handlers → MountPipeline returns null → early return
        var (notifier, _) = BuildNotifier([], []);
        var message = new TestNotification();

        // Act & Assert — no exception, pipeline returns null path covered
        await notifier.DispatchNotifications(message, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Notify_Enumerable_WritesPacksToChannel()
    {
        // Arrange — production Notifier.Notify(IEnumerable) writes to the channel; no worker is running
        var handlerMock = new Mock<INotificationHandler<TestNotification>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestNotification>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var (notifier, channel) = BuildNotifier([handlerMock.Object], []);
        TestNotification[] messages = [new(), new()];

        // Act
        await notifier.Notify(messages, TestContext.Current.CancellationToken);

        // Assert — 2 packs written to channel (one per message)
        var packsRead = 0;
        while (channel.Reader.TryRead(out _)) packsRead++;
        Assert.Equal(2, packsRead);
    }
}
