using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals;
using MoqNotifier = NetMediate.Moq.Notifier;

namespace NetMediate.Tests.Internals;

public sealed class MediatorNotifiesContinuationTests
{
    public sealed class Msg
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
        public Task Handle(Msg notification, CancellationToken cancellationToken = default)
        {
            notification.Check();
            return Task.CompletedTask;
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

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient(typeof(PipelineExecutor<,,>));
        configure(services);
        return services.BuildServiceProvider();
    }

    private static Mediator BuildMediator(IServiceProvider provider) =>
        new(provider, new MoqNotifier(provider));

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
    public async Task Notifies_HandlerSuccess_DoesNotInvokeErrorCallback()
    {
        var message = new Msg();
        await using var provider = BuildProvider(svc =>
            svc.AddSingleton<INotificationHandler<Msg>>(new OkHandler()));
        var sut = BuildMediator(provider);

        await sut.Notify(message, TestContext.Current.CancellationToken);
        await WaitForAsync(() => message.Checked, TestContext.Current.CancellationToken);

        Assert.False(message.Maked); // No behavior registered
        Assert.True(message.Checked);
    }

    [Fact]
    public async Task Notifies_HandlerFaulted_InvokesErrorCallback_WithoutNotificationBehavior()
    {
        var message = new Msg();
        await using var provider = BuildProvider(svc =>
            svc.AddSingleton<INotificationHandler<Msg>>(new FaultHandler()));
        var sut = BuildMediator(provider);

        // Fire-and-forget: no exception propagated
        await sut.Notify(message, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(message.Maked);
        Assert.False(message.Checked); // FaultHandler threw, Check() never called
    }

    [Fact]
    public async Task Notifies_HandlerFaulted_WithNotificationBehavior_BehaviorRunsBeforeHandler()
    {
        var message = new Msg();
        await using var provider = BuildProvider(svc =>
        {
            svc.AddSingleton<INotificationHandler<Msg>>(new FaultHandler());
            svc.AddTransient<IPipelineBehavior<Msg, Task>, PassThroughBehavior>();
        });
        var sut = BuildMediator(provider);

        await sut.Notify(message, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.True(message.Maked);  // Behavior ran and called Mark() before dispatching
        Assert.False(message.Checked); // Handler faulted, Check() never called
    }

    [Fact]
    public async Task Notifies_HandlerFaulted_WithNotificationBehavior_SuccessCallback()
    {
        var message = new Msg();
        await using var provider = BuildProvider(svc =>
        {
            svc.AddSingleton<INotificationHandler<Msg>>(new OkHandler());
            svc.AddTransient<IPipelineBehavior<Msg, Task>, PassThroughBehavior>();
        });
        var sut = BuildMediator(provider);

        await sut.Notify(message, TestContext.Current.CancellationToken);
        await WaitForAsync(() => message.Checked, TestContext.Current.CancellationToken);

        Assert.True(message.Maked);
        Assert.True(message.Checked);
    }
}
