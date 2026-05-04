using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

/// <summary>
/// Tests for the internal <see cref="Notifier"/> (production fire-and-forget notifier).
/// </summary>
public class InternalNotifierTests
{
    public record TestNotification;

    private static (Notifier notifier, ServiceProvider provider) BuildNotifier(
        Action<IMediatorServiceBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.UseNetMediate(configure ?? (_ => { }));
        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<Notifier>>();
        return (new Notifier(provider, logger), provider);
    }

    private static async Task WaitForAsync(Func<bool> predicate, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (predicate()) return;
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

        await notifier.DispatchNotifications(message, [handler], TestContext.Current.CancellationToken);
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

        // Fire-and-forget: returns Task.CompletedTask immediately, exception is swallowed/logged
        var task = notifier.DispatchNotifications(message, [handler], TestContext.Current.CancellationToken);
        Assert.True(task.IsCompleted);
        await task; // must not throw
    }

    [Fact]
    public async Task DispatchNotifications_WithNoHandlers_CompletesWithoutInvoking()
    {
        var (notifier, provider) = BuildNotifier();
        await using var _ = provider;
        var message = new TestNotification();

        var task = notifier.DispatchNotifications(message, [], TestContext.Current.CancellationToken);
        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public async Task Notify_WithHandler_InvokesHandlerEventually()
    {
        var tcs = new TaskCompletionSource<bool>();
        var handler = new TcsNotificationHandler<TestNotification>(tcs);
        var (notifier, provider) = BuildNotifier(b =>
            b.RegisterNotificationHandler<TestNotification>(handler));
        await using var _ = provider;
        var message = new TestNotification();

        await notifier.Notify(message, TestContext.Current.CancellationToken);
        await WaitForAsync(() => tcs.Task.IsCompleted, TestContext.Current.CancellationToken);

        Assert.True(tcs.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void Notify_Enumerable_WhenNotifyThrowsSynchronously_CatchesAndLogs()
    {
        // A service provider that always throws forces Notify(TMessage) to throw
        // synchronously inside the foreach loop, exercising the catch branch.
        var services = new ServiceCollection();
        services.AddLogging();
        using var logProvider = services.BuildServiceProvider();
        var logger = logProvider.GetRequiredService<ILogger<Notifier>>();

        var notifier = new Notifier(new ThrowingServiceProvider(), logger);
        var messages = new[] { new TestNotification(), new TestNotification() };

        var task = notifier.Notify<TestNotification>(messages, CancellationToken.None);

        // Must complete synchronously and not throw — exceptions are caught and logged.
        Assert.True(task.IsCompletedSuccessfully);
    }

    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            throw new InvalidOperationException("test-throw");
    }

    private sealed class TcsNotificationHandler<T>(TaskCompletionSource<bool> tcs) : INotificationHandler<T>
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
