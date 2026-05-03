using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate.Adapters;

namespace NetMediate.Tests.Adapters;

public sealed class NotificationAdaptersTests
{
    // -----------------------------------------------------------------------
    // AdapterEnvelope
    // -----------------------------------------------------------------------

    [Fact]
    public void AdapterEnvelope_Create_ShouldPopulateAllFields()
    {
        var before = DateTimeOffset.UtcNow;
        var msg = new AdapterTestMessage("hello");
        var envelope = AdapterEnvelope<AdapterTestMessage>.Create(msg);
        var after = DateTimeOffset.UtcNow;

        Assert.NotEqual(Guid.Empty, envelope.MessageId);
        Assert.Equal(nameof(AdapterTestMessage), envelope.MessageType);
        Assert.InRange(envelope.OccurredAt, before, after);
        Assert.Same(msg, envelope.Message);
    }

    // -----------------------------------------------------------------------
    // DI registration helpers
    // -----------------------------------------------------------------------

    [Fact]
    public void AddNetMediateAdapters_ShouldRegisterBehaviorAndOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetMediateAdapters(opts => opts.InvokeAdaptersInParallel = true);
        using var provider = services.BuildServiceProvider();

        var opts = provider.GetRequiredService<NotificationAdapterOptions>();
        Assert.True(opts.InvokeAdaptersInParallel);
    }

    [Fact]
    public void AddNotificationAdapter_TypeOverload_ShouldRegisterAdapter()
    {
        var services = new ServiceCollection();
        services.AddNotificationAdapter<AdapterTestMessage, RecordingAdapter>();
        using var provider = services.BuildServiceProvider();

        var adapters = provider.GetServices<INotificationAdapter<AdapterTestMessage>>().ToList();
        Assert.Single(adapters);
        Assert.IsType<RecordingAdapter>(adapters[0]);
    }

    [Fact]
    public void AddNotificationAdapter_InstanceOverload_ShouldRegisterAdapter()
    {
        var adapter = new RecordingAdapter();
        var services = new ServiceCollection();
        services.AddNotificationAdapter<AdapterTestMessage>(adapter);
        using var provider = services.BuildServiceProvider();

        var adapters = provider.GetServices<INotificationAdapter<AdapterTestMessage>>().ToList();
        Assert.Single(adapters);
        Assert.Same(adapter, adapters[0]);
    }

    // -----------------------------------------------------------------------
    // NotificationAdapterBehavior – no adapters (early return)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NotificationAdapterBehavior_NoAdapters_ShouldOnlyCallNext()
    {
        var (behavior, provider) = BuildBehavior(new ServiceCollection(), new NotificationAdapterOptions());
        using var _ = provider;
        var nextCalled = false;

        await behavior.Handle(
            new AdapterTestMessage("x"),
            (_, _) => { nextCalled = true; return Task.CompletedTask; },
            TestContext.Current.CancellationToken
        );

        Assert.True(nextCalled);
    }

    // -----------------------------------------------------------------------
    // Sequential invocation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NotificationAdapterBehavior_Sequential_ShouldInvokeAllAdaptersInOrder()
    {
        var order = new List<int>();
        var adapter1 = new OrderedAdapter(1, order);
        var adapter2 = new OrderedAdapter(2, order);

        var services = new ServiceCollection();
        services.AddSingleton<INotificationAdapter<AdapterTestMessage>>(adapter1);
        services.AddSingleton<INotificationAdapter<AdapterTestMessage>>(adapter2);

        var (behavior, provider) = BuildBehavior(services, new NotificationAdapterOptions { InvokeAdaptersInParallel = false });
        using var _ = provider;

        await behavior.Handle(
            new AdapterTestMessage("seq"),
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new[] { 1, 2 }, order);
    }

    // -----------------------------------------------------------------------
    // Parallel invocation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NotificationAdapterBehavior_Parallel_ShouldInvokeAllAdapters()
    {
        var adapter1 = new RecordingAdapter();
        var adapter2 = new RecordingAdapter();

        var services = new ServiceCollection();
        services.AddSingleton<INotificationAdapter<AdapterTestMessage>>(adapter1);
        services.AddSingleton<INotificationAdapter<AdapterTestMessage>>(adapter2);

        var (behavior, provider) = BuildBehavior(services, new NotificationAdapterOptions { InvokeAdaptersInParallel = true });
        using var _ = provider;

        await behavior.Handle(
            new AdapterTestMessage("par"),
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, adapter1.Invocations);
        Assert.Equal(1, adapter2.Invocations);
    }

    // -----------------------------------------------------------------------
    // Failure suppression (ThrowOnAdapterFailure = false)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NotificationAdapterBehavior_SuppressedFailure_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationAdapter<AdapterTestMessage>>(new ThrowingAdapter());

        var (behavior, provider) = BuildBehavior(
            services,
            new NotificationAdapterOptions { ThrowOnAdapterFailure = false }
        );
        using var _ = provider;

        // Should complete without exception even though the adapter throws.
        await behavior.Handle(
            new AdapterTestMessage("suppress"),
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken
        );
    }

    // -----------------------------------------------------------------------
    // Failure propagation (ThrowOnAdapterFailure = true – default)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NotificationAdapterBehavior_PropagatedFailure_ShouldThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationAdapter<AdapterTestMessage>>(new ThrowingAdapter());

        var (behavior, provider) = BuildBehavior(
            services,
            new NotificationAdapterOptions { ThrowOnAdapterFailure = true }
        );
        using var _ = provider;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new AdapterTestMessage("throw"),
                (_, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken
            )
        );
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (NotificationAdapterBehavior<AdapterTestMessage> Behavior, IDisposable Provider)
        BuildBehavior(ServiceCollection extraServices, NotificationAdapterOptions options)
    {
        extraServices.AddLogging();
        var provider = extraServices.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<NotificationAdapterBehavior<AdapterTestMessage>>>();
        return (new NotificationAdapterBehavior<AdapterTestMessage>(provider, options, logger), provider);
    }

    public sealed record AdapterTestMessage(string Value);

    private sealed class RecordingAdapter : INotificationAdapter<AdapterTestMessage>
    {
        public int Invocations;

        public Task ForwardAsync(AdapterEnvelope<AdapterTestMessage> envelope, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Invocations);
            return Task.CompletedTask;
        }
    }

    private sealed class OrderedAdapter(int id, List<int> order) : INotificationAdapter<AdapterTestMessage>
    {
        public Task ForwardAsync(AdapterEnvelope<AdapterTestMessage> envelope, CancellationToken cancellationToken = default)
        {
            lock (order) order.Add(id);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAdapter : INotificationAdapter<AdapterTestMessage>
    {
        public Task ForwardAsync(AdapterEnvelope<AdapterTestMessage> envelope, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("adapter failure");
    }
}
