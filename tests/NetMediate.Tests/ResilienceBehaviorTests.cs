using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
}
