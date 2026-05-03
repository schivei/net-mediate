using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate.Adapters;
using NetMediate.Resilience;

namespace NetMediate.Tests;

public sealed class ResilienceBehaviorTests
{
    [Fact]
    public async Task RetryRequestBehavior_ShouldRetryUntilSuccess()
    {
        using var host = await CreateHostAsync(
            configureServices: services =>
            {
                services.AddNetMediateResilience(configureRetry: options =>
                {
                    options.MaxRetryCount = 3;
                    options.Delay = TimeSpan.Zero;
                });
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();
        var response = await mediator.Request<RetryRequestMessage, string>(
            new RetryRequestMessage("ok"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("ok", response);
        Assert.Equal(3, RetryRequestHandler.Attempts);
    }

    [Fact]
    public async Task RetryNotificationBehavior_WithMediatorNotify_ShouldDispatchNotification()
    {
        // With fire-and-forget dispatch, handler exceptions do not propagate back through
        // the behavior pipeline, so retry at the pipeline level does not trigger retries.
        // This test verifies that notifications ARE dispatched and the behavior is wired.
        using var host = await CreateHostAsync(
            configureServices: services =>
            {
                services.AddNetMediateResilience(configureRetry: options =>
                {
                    options.MaxRetryCount = 3;
                    options.Delay = TimeSpan.Zero;
                });
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();

        await mediator.Notify(
            new RetryNotificationViaMediatorMessage("ok"),
            TestContext.Current.CancellationToken
        );

        await WaitForAsync(
            () => RetryNotificationViaMediatorHandler.Attempts >= 1,
            TestContext.Current.CancellationToken
        );

        Assert.True(RetryNotificationViaMediatorHandler.Attempts >= 1);
    }

    [Fact]
    public async Task TimeoutRequestBehavior_ShouldThrowTimeoutException()
    {
        using var host = await CreateHostAsync(
            configureServices: services =>
            {
                services.AddNetMediateResilience(configureTimeout: options =>
                {
                    options.RequestTimeout = TimeSpan.FromMilliseconds(20);
                });
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await mediator.Request<TimeoutRequestMessage, string>(
                new TimeoutRequestMessage("slow"),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task CircuitBreakerRequestBehavior_ShouldOpenAfterThreshold()
    {
        using var host = await CreateHostAsync(
            configureServices: services =>
            {
                services.AddNetMediateCircuitBreaker(options =>
                {
                    options.FailureThreshold = 2;
                    options.OpenDuration = TimeSpan.FromSeconds(30);
                });
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.Request<CircuitBreakerRequestMessage, string>(
                new CircuitBreakerRequestMessage("fail"),
                TestContext.Current.CancellationToken
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.Request<CircuitBreakerRequestMessage, string>(
                new CircuitBreakerRequestMessage("fail"),
                TestContext.Current.CancellationToken
            )
        );

        var circuitException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.Request<CircuitBreakerRequestMessage, string>(new CircuitBreakerRequestMessage("fail"), TestContext.Current.CancellationToken)
        );

        Assert.Contains("Circuit open", circuitException.Message);
    }

    [Fact]
    public async Task TimeoutRequestBehavior_ShouldPassThrough_WhenTimeoutIsZero()
    {
        using var host = await CreateHostAsync(
            configureServices: services =>
            {
                services.AddNetMediateResilience(configureTimeout: options =>
                {
                    options.RequestTimeout = TimeSpan.Zero;
                });
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();
        // Zero timeout means "no timeout" — the request should complete normally.
        var result = await mediator.Request<RetryRequestMessage, string>(
            new RetryRequestMessage("pass"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("pass", result);
    }

    [Fact]
    public async Task TimeoutNotificationBehavior_ShouldPassThrough_WhenTimeoutIsZero()
    {
        using var host = await CreateNotificationHostAsync(
            configureServices: services =>
            {
                services.AddNetMediateResilience(configureTimeout: options =>
                {
                    options.NotificationTimeout = TimeSpan.Zero;
                });
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();
        // Should complete without throwing.
        await mediator.Notify(
            new TimeoutNotificationMessage("pass"),
            TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public async Task TimeoutNotificationBehavior_ShouldThrowTimeoutException_WhenNotificationExceedsTimeout()
    {
        using var host = await CreateNotificationHostAsync(
            configureServices: services =>
            {
                services.AddNetMediateResilience(configureTimeout: options =>
                {
                    options.NotificationTimeout = TimeSpan.FromMilliseconds(20);
                });
                services.AddNetMediateAdapters(opts => opts.ThrowOnAdapterFailure = true);
                services.AddSingleton<
                    INotificationAdapter<TimeoutNotificationMessage>,
                    SlowNotificationAdapter
                >();
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await mediator.Notify(
                new TimeoutNotificationMessage("slow"),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task CircuitBreakerNotificationBehavior_ShouldOpenAfterThreshold()
    {
        using var host = await CreateNotificationHostAsync(
            configureServices: services =>
            {
                services.AddNetMediateCircuitBreaker(options =>
                {
                    options.FailureThreshold = 2;
                    options.OpenDuration = TimeSpan.FromSeconds(30);
                });
                services.AddNetMediateAdapters(opts => opts.ThrowOnAdapterFailure = true);
                services.AddSingleton<
                    INotificationAdapter<CbNotificationMessage>,
                    ThrowingNotificationAdapter
                >();
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();

        // First two calls register failures.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new CbNotificationMessage(), TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new CbNotificationMessage(), TestContext.Current.CancellationToken)
        );

        // Third call should see an open circuit.
        var circuitEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new CbNotificationMessage(), TestContext.Current.CancellationToken)
        );
        Assert.Contains("Circuit open", circuitEx.Message);
    }

    [Fact]
    public async Task RetryNotificationBehavior_ShouldRetryViaAdapterException()
    {
        using var host = await CreateNotificationHostAsync(
            configureServices: services =>
            {
                services.AddNetMediateResilience(configureRetry: options =>
                {
                    options.MaxRetryCount = 2;
                    options.Delay = TimeSpan.Zero;
                });
                services.AddNetMediateAdapters(opts => opts.ThrowOnAdapterFailure = true);
                services.AddSingleton<
                    INotificationAdapter<RetryNotificationMessage>,
                    CountingThrowAdapter
                >();
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();

        // After MaxRetryCount exhaustion the final exception propagates.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new RetryNotificationMessage(), TestContext.Current.CancellationToken)
        );

        // 1 initial attempt + 2 retries = 3 total calls.
        Assert.Equal(3, CountingThrowAdapter.Invocations);
    }

    private static async Task<IHost> CreateHostAsync(Action<IServiceCollection> configureServices)
    {
        RetryRequestHandler.Reset();
        RetryNotificationViaMediatorHandler.Reset();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(configure =>
        {
            configure.RegisterRequestHandler<RetryRequestHandler, RetryRequestMessage, string>();
            configure.RegisterRequestHandler<TimeoutRequestHandler, TimeoutRequestMessage, string>();
            configure.RegisterRequestHandler<CircuitBreakerRequestHandler, CircuitBreakerRequestMessage, string>();
            configure.RegisterNotificationHandler<RetryNotificationViaMediatorHandler, RetryNotificationViaMediatorMessage>();
        });
        configureServices(builder.Services);

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static async Task<IHost> CreateNotificationHostAsync(Action<IServiceCollection> configureServices)
    {
        CountingThrowAdapter.Reset();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(configure =>
        {
            configure.RegisterNotificationHandler<TimeoutNotificationHandler, TimeoutNotificationMessage>();
            configure.RegisterNotificationHandler<CbNotificationHandler, CbNotificationMessage>();
            configure.RegisterNotificationHandler<RetryNotificationHandler, RetryNotificationMessage>();
        });
        configureServices(builder.Services);

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static async Task WaitForAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate())
                return;
            await Task.Delay(10, cancellationToken);
        }

        Assert.Fail("Timed out waiting for notification processing.");
    }

    public sealed record RetryRequestMessage(string Value);
    public sealed record RetryNotificationViaMediatorMessage(string Value);
    public sealed record TimeoutRequestMessage(string Value);
    public sealed record CircuitBreakerRequestMessage(string Value);

    // Distinct message types to avoid sharing static circuit-breaker state with request tests.
    public sealed record TimeoutNotificationMessage(string Value = "");
    public sealed record CbNotificationMessage;
    public sealed record RetryNotificationMessage;

    private sealed class RetryRequestHandler : IRequestHandler<RetryRequestMessage, string>
    {
        private static int s_attempts;
        public static int Attempts => Volatile.Read(ref s_attempts);
        public static void Reset() => Interlocked.Exchange(ref s_attempts, 0);

        public async Task<string> Handle(
            RetryRequestMessage query,
            CancellationToken cancellationToken = default
        )
        {
            var attempt = Interlocked.Increment(ref s_attempts);
            if (attempt < 3)
                throw new InvalidOperationException($"failed attempt {attempt}");

            return query.Value;
        }
    }

    private sealed class TimeoutRequestHandler : IRequestHandler<TimeoutRequestMessage, string>
    {
        public async Task<string> Handle(
            TimeoutRequestMessage query,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            return query.Value;
        }
    }

    private sealed class CircuitBreakerRequestHandler
        : IRequestHandler<CircuitBreakerRequestMessage, string>
    {
        public async Task<string> Handle(
            CircuitBreakerRequestMessage query,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("request failure");
    }

    private sealed class RetryNotificationViaMediatorHandler
        : INotificationHandler<RetryNotificationViaMediatorMessage>
    {
        private static int s_attempts;
        public static int Attempts => Volatile.Read(ref s_attempts);
        public static void Reset() => Interlocked.Exchange(ref s_attempts, 0);

        public async Task Handle(
            RetryNotificationViaMediatorMessage notification,
            CancellationToken cancellationToken = default
        )
        {
            var attempt = Interlocked.Increment(ref s_attempts);
            if (attempt < 3)
                throw new InvalidOperationException($"failed attempt {attempt}");
        }
    }

    private sealed class TimeoutNotificationHandler : INotificationHandler<TimeoutNotificationMessage>
    {
        public Task Handle(TimeoutNotificationMessage notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CbNotificationHandler : INotificationHandler<CbNotificationMessage>
    {
        public Task Handle(CbNotificationMessage notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RetryNotificationHandler : INotificationHandler<RetryNotificationMessage>
    {
        public Task Handle(RetryNotificationMessage notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Adapter that delays 100 ms, used to force notification timeout.
    /// </summary>
    private sealed class SlowNotificationAdapter : INotificationAdapter<TimeoutNotificationMessage>
    {
        public async Task ForwardAsync(
            AdapterEnvelope<TimeoutNotificationMessage> envelope,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    /// <summary>
    /// Adapter that always throws, used to trip the circuit breaker.
    /// </summary>
    private sealed class ThrowingNotificationAdapter : INotificationAdapter<CbNotificationMessage>
    {
        public Task ForwardAsync(
            AdapterEnvelope<CbNotificationMessage> envelope,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("adapter failure");
    }

    /// <summary>
    /// Adapter that always throws and counts invocations, used to verify retry behaviour.
    /// </summary>
    private sealed class CountingThrowAdapter : INotificationAdapter<RetryNotificationMessage>
    {
        private static int s_invocations;
        public static int Invocations => Volatile.Read(ref s_invocations);
        public static void Reset() => Interlocked.Exchange(ref s_invocations, 0);

        public Task ForwardAsync(
            AdapterEnvelope<RetryNotificationMessage> envelope,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref s_invocations);
            throw new InvalidOperationException("retry trigger");
        }
    }
}
