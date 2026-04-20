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
                services.AddNetMediateRetry(options =>
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
    public async Task RetryNotificationBehavior_ShouldRetryUntilSuccess()
    {
        var behavior = new RetryNotificationBehavior<RetryNotificationMessage>(
            new RetryBehaviorOptions { MaxRetryCount = 3, Delay = TimeSpan.Zero }
        );
        var attempts = 0;

        await behavior.Handle(
            new RetryNotificationMessage("ok"),
            _ =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException(new InvalidOperationException("failed"))
                    : Task.CompletedTask;
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task TimeoutRequestBehavior_ShouldThrowTimeoutException()
    {
        using var host = await CreateHostAsync(
            configureServices: services =>
            {
                services.AddNetMediateTimeout(options =>
                {
                    options.RequestTimeout = TimeSpan.FromMilliseconds(20);
                });
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            mediator.Request<TimeoutRequestMessage, string>(
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Request<CircuitBreakerRequestMessage, string>(
                new CircuitBreakerRequestMessage("fail"),
                TestContext.Current.CancellationToken
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Request<CircuitBreakerRequestMessage, string>(
                new CircuitBreakerRequestMessage("fail"),
                TestContext.Current.CancellationToken
            )
        );

        var circuitException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Request<CircuitBreakerRequestMessage, string>(
                new CircuitBreakerRequestMessage("fail"),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("Circuit open", circuitException.Message);
    }

    private static async Task<IHost> CreateHostAsync(Action<IServiceCollection> configureServices)
    {
        RetryRequestHandler.Reset();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(typeof(ResilienceBehaviorTests).Assembly);
        configureServices(builder.Services);

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    public sealed record RetryRequestMessage(string Value);
    public sealed record RetryNotificationMessage(string Value);
    public sealed record TimeoutRequestMessage(string Value);
    public sealed record CircuitBreakerRequestMessage(string Value);

    private sealed class RetryRequestHandler : IRequestHandler<RetryRequestMessage, string>
    {
        private static int s_attempts;
        public static int Attempts => Volatile.Read(ref s_attempts);
        public static void Reset() => Interlocked.Exchange(ref s_attempts, 0);

        public Task<string> Handle(
            RetryRequestMessage query,
            CancellationToken cancellationToken = default
        )
        {
            var attempt = Interlocked.Increment(ref s_attempts);
            if (attempt < 3)
                throw new InvalidOperationException($"failed attempt {attempt}");
            return Task.FromResult(query.Value);
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
        public Task<string> Handle(
            CircuitBreakerRequestMessage query,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("request failure");
    }
}
