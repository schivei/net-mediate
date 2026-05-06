using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

/// <summary>
/// Tests for the internal <see cref="Notifier"/> (production fire-and-forget notifier).
/// </summary>
public class InternalNotifierTests
{
    private record TestNotification;

    private static (Notifier notifier, ServiceProvider provider) BuildNotifier(
        Action<IMediatorServiceBuilder>? configure = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.UseNetMediate(configure ?? (_ => { }));
        var provider = services.BuildServiceProvider();
        return (new Notifier(provider), provider);
    }

    private static async Task WaitForAsync(Func<bool> predicate, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (predicate())
                return;
            await Task.Delay(10, ct);
        }
    }

    [Fact]
    public async Task DispatchNotifications_WithHandler_InvokesHandler()
    {
        var tcs = new TaskCompletionSource<bool>();
        var handler = new TcsNotificationHandler<TestNotification>(tcs);
        var (notifier, provider) = BuildNotifier();
        await using var _ = provider;
        var message = new TestNotification();

        await notifier.DispatchNotifications(
            null,
            message,
            [handler],
            TestContext.Current.CancellationToken
        );
        await WaitForAsync(() => tcs.Task.IsCompleted, TestContext.Current.CancellationToken);

        Assert.True(tcs.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DispatchNotifications_WhenHandlerThrows_LogsAndDoesNotThrow()
    {
        var handler = new ThrowingNotificationHandler<TestNotification>();
        var (notifier, provider) = BuildNotifier();
        await using var _ = provider;
        var message = new TestNotification();

        var task = notifier.DispatchNotifications(
            null,
            message,
            [handler],
            TestContext.Current.CancellationToken
        );
        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public async Task DispatchNotifications_WithNoHandlers_CompletesWithoutInvoking()
    {
        var (notifier, provider) = BuildNotifier();
        await using var _ = provider;
        var message = new TestNotification();

        var task = notifier.DispatchNotifications(
            null,
            message,
            [],
            TestContext.Current.CancellationToken
        );
        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public async Task Notify_WithHandler_InvokesHandlerEventually()
    {
        var tcs = new TaskCompletionSource<bool>();
        var handler = new TcsNotificationHandler<TestNotification>(tcs);
        var (notifier, provider) = BuildNotifier(b => b.RegisterNotificationHandler(handler));
        await using var _ = provider;
        var message = new TestNotification();

        await notifier.Notify(null, message, TestContext.Current.CancellationToken);
        await WaitForAsync(() => tcs.Task.IsCompleted, TestContext.Current.CancellationToken);

        Assert.True(tcs.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Notify_Enumerable_WhenNotifyThrowsSynchronously_CatchesAndLogs()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NotificationPipelineExecutor<TestNotification>>();
        
        await using var logProvider = services.BuildServiceProvider();
        
        var notifier = new Notifier(new ThrowingServiceProvider());
        var messages = new[] { new TestNotification(), new TestNotification() };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => notifier.Notify(null, messages, TestContext.Current.CancellationToken)
        );

        Assert.Equal("test-throw", ex.Message);
    }

    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType) =>
            throw new InvalidOperationException("test-throw");
    }

    private sealed class TcsNotificationHandler<T>(TaskCompletionSource<bool> tcs)
        : INotificationHandler<T>
        where T : notnull
    {
        public Task Handle(T notification, CancellationToken ct = default)
        {
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingNotificationHandler<T> : INotificationHandler<T>
        where T : notnull
    {
        public Task Handle(T notification, CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("handler failure"));
    }
}
