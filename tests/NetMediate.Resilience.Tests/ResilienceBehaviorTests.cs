using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate.Resilience;

namespace NetMediate.Resilience.Tests;

public sealed class ResilienceBehaviorTests
{
    [Fact]
    public async Task RetryRequestBehavior_ShouldRetryUntilSuccess()
    {
        using var host = await CreateRequestHostAsync(services =>
        {
            services.Configure<RetryBehaviorOptions>(opts => { opts.MaxRetryCount = 3; opts.Delay = TimeSpan.Zero; });
        });

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
        using var host = await CreateRequestHostAsync(services =>
        {
            services.Configure<RetryBehaviorOptions>(opts => { opts.MaxRetryCount = 3; opts.Delay = TimeSpan.Zero; });
        });

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
        using var host = await CreateRequestHostAsync(services =>
        {
            services.Configure<TimeoutBehaviorOptions>(opts => { opts.RequestTimeout = TimeSpan.FromMilliseconds(20); });
        });

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
        using var host = await CreateRequestHostAsync(services =>
        {
            services.Configure<CircuitBreakerBehaviorOptions>(opts => { opts.FailureThreshold = 2; opts.OpenDuration = TimeSpan.FromSeconds(30); });
        });

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
            await mediator.Request<CircuitBreakerRequestMessage, string>(
                new CircuitBreakerRequestMessage("fail"),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("Circuit open", circuitException.Message);
    }

    [Fact]
    public async Task TimeoutRequestBehavior_ShouldPassThrough_WhenTimeoutIsZero()
    {
        using var host = await CreateRequestHostAsync(services =>
        {
            services.Configure<TimeoutBehaviorOptions>(opts => { opts.RequestTimeout = TimeSpan.Zero; });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        var result = await mediator.Request<RetryRequestMessage, string>(
            new RetryRequestMessage("pass"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("pass", result);
    }

    [Fact]
    public async Task TimeoutNotificationBehavior_ShouldPassThrough_WhenTimeoutIsZero()
    {
        using var host = await CreateNotificationHostAsync(services =>
        {
            services.Configure<TimeoutBehaviorOptions>(opts => { opts.NotificationTimeout = TimeSpan.Zero; });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        await mediator.Notify(
            new TimeoutNotificationMessage("pass"),
            TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public async Task TimeoutNotificationBehavior_ShouldThrowTimeoutException_WhenNotificationExceedsTimeout()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(configure =>
        {
            configure.RegisterNotificationHandler<SlowTimeoutNotificationHandler, SlowTimeoutNotificationMessage>();
            // Timeout registered first → becomes outermost after Reverse(); SlowBehavior becomes inner
            configure.RegisterBehavior<TimeoutNotificationBehavior<SlowTimeoutNotificationMessage>, SlowTimeoutNotificationMessage, Task>();
            configure.RegisterBehavior<SlowPipelineBehavior<SlowTimeoutNotificationMessage>, SlowTimeoutNotificationMessage, Task>();
        });
        builder.Services.Configure<TimeoutBehaviorOptions>(opts => { opts.NotificationTimeout = TimeSpan.FromMilliseconds(20); });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await mediator.Notify(
                new SlowTimeoutNotificationMessage("slow"),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task CircuitBreakerNotificationBehavior_ShouldOpenAfterThreshold()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(configure =>
        {
            configure.RegisterNotificationHandler<ThrowingCbNotificationHandler2, ThrowingCbMessage>();
            // CircuitBreaker registered first → becomes outermost after Reverse(); Throwing becomes inner
            configure.RegisterBehavior<CircuitBreakerNotificationBehavior<ThrowingCbMessage>, ThrowingCbMessage, Task>();
            configure.RegisterBehavior<ThrowingPipelineBehavior<ThrowingCbMessage>, ThrowingCbMessage, Task>();
        });
        builder.Services.Configure<CircuitBreakerBehaviorOptions>(opts => { opts.FailureThreshold = 2; opts.OpenDuration = TimeSpan.FromSeconds(30); });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new ThrowingCbMessage(), TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new ThrowingCbMessage(), TestContext.Current.CancellationToken)
        );

        var circuitEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new ThrowingCbMessage(), TestContext.Current.CancellationToken)
        );
        Assert.Contains("Circuit open", circuitEx.Message);
    }

    [Fact]
    public async Task RetryNotificationBehavior_ShouldRetryOnPipelineBehaviorException()
    {
        CountingThrowBehavior<CountingThrowMessage>.Reset();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(configure =>
        {
            configure.RegisterNotificationHandler<CountingThrowNotificationHandler, CountingThrowMessage>();
            // Retry registered first → becomes outermost after Reverse(); CountingThrow becomes inner
            configure.RegisterBehavior<RetryNotificationBehavior<CountingThrowMessage>, CountingThrowMessage, Task>();
            configure.RegisterBehavior<CountingThrowBehavior<CountingThrowMessage>, CountingThrowMessage, Task>();
        });
        builder.Services.Configure<RetryBehaviorOptions>(opts => { opts.MaxRetryCount = 2; opts.Delay = TimeSpan.Zero; });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new CountingThrowMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal(3, CountingThrowBehavior<CountingThrowMessage>.Invocations);
    }

    // ── Load / throughput test ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestLoad_WithResilienceBehaviors_ShouldSustainMinimumThroughput()
    {
        if (!ShouldRunPerformanceTests())
        {
            Assert.Skip("Set NETMEDIATE_RUN_PERFORMANCE_TESTS=true to run performance tests.");
            return;
        }

        using var host = await CreateLoadHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;
        var tfm = AppContext.TargetFrameworkName ?? "unknown";
        const int operations = 10_000;

        var start = Stopwatch.GetTimestamp();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, operations),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
                CancellationToken = ct,
            },
            async (i, token) =>
            {
                var response = await mediator.Request<LoadRequest, int>(new(i), token);
                Assert.Equal(i + 1, response);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        var minimum = IsGitHubActions() ? 30_000d : 50_000d;
        Assert.True(
            throughput >= minimum,
            $"tfm={tfm} resilience throughput {throughput:F0} ops/s < minimum {minimum:F0} ops/s"
        );
    }

    // ── Host builders ───────────────────────────────────────────────────────────────────────

    private static async Task<IHost> CreateRequestHostAsync(Action<IServiceCollection> configureServices)
    {
        RetryRequestHandler.Reset();
        RetryNotificationViaMediatorHandler.Reset();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(configure =>
        {
            configure.RegisterRequestHandler<RetryRequestHandler, RetryRequestMessage, string>();
            configure.RegisterRequestHandler<TimeoutRequestHandler, TimeoutRequestMessage, string>();
            configure.RegisterRequestHandler<CircuitBreakerRequestHandler, CircuitBreakerRequestMessage, string>();
            configure.RegisterNotificationHandler<RetryNotificationViaMediatorHandler, RetryNotificationViaMediatorMessage>();

            configure.RegisterBehavior<RetryRequestBehavior<RetryRequestMessage, string>, RetryRequestMessage, Task<string>>();
            configure.RegisterBehavior<TimeoutRequestBehavior<RetryRequestMessage, string>, RetryRequestMessage, Task<string>>();
            configure.RegisterBehavior<CircuitBreakerRequestBehavior<RetryRequestMessage, string>, RetryRequestMessage, Task<string>>();

            configure.RegisterBehavior<RetryRequestBehavior<TimeoutRequestMessage, string>, TimeoutRequestMessage, Task<string>>();
            configure.RegisterBehavior<TimeoutRequestBehavior<TimeoutRequestMessage, string>, TimeoutRequestMessage, Task<string>>();
            configure.RegisterBehavior<CircuitBreakerRequestBehavior<TimeoutRequestMessage, string>, TimeoutRequestMessage, Task<string>>();

            configure.RegisterBehavior<RetryRequestBehavior<CircuitBreakerRequestMessage, string>, CircuitBreakerRequestMessage, Task<string>>();
            configure.RegisterBehavior<TimeoutRequestBehavior<CircuitBreakerRequestMessage, string>, CircuitBreakerRequestMessage, Task<string>>();
            configure.RegisterBehavior<CircuitBreakerRequestBehavior<CircuitBreakerRequestMessage, string>, CircuitBreakerRequestMessage, Task<string>>();

            configure.RegisterBehavior<RetryNotificationBehavior<RetryNotificationViaMediatorMessage>, RetryNotificationViaMediatorMessage, Task>();
            configure.RegisterBehavior<TimeoutNotificationBehavior<RetryNotificationViaMediatorMessage>, RetryNotificationViaMediatorMessage, Task>();
            configure.RegisterBehavior<CircuitBreakerNotificationBehavior<RetryNotificationViaMediatorMessage>, RetryNotificationViaMediatorMessage, Task>();
        });

        configureServices(builder.Services);

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static async Task<IHost> CreateNotificationHostAsync(Action<IServiceCollection> configureServices)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(configure =>
        {
            configure.RegisterNotificationHandler<TimeoutNotificationHandler, TimeoutNotificationMessage>();
            configure.RegisterNotificationHandler<CbNotificationHandler, CbNotificationMessage>();
            configure.RegisterNotificationHandler<RetryNotificationHandler, RetryNotificationMessage>();

            configure.RegisterBehavior<RetryNotificationBehavior<TimeoutNotificationMessage>, TimeoutNotificationMessage, Task>();
            configure.RegisterBehavior<TimeoutNotificationBehavior<TimeoutNotificationMessage>, TimeoutNotificationMessage, Task>();
            configure.RegisterBehavior<CircuitBreakerNotificationBehavior<TimeoutNotificationMessage>, TimeoutNotificationMessage, Task>();

            configure.RegisterBehavior<RetryNotificationBehavior<CbNotificationMessage>, CbNotificationMessage, Task>();
            configure.RegisterBehavior<TimeoutNotificationBehavior<CbNotificationMessage>, CbNotificationMessage, Task>();
            configure.RegisterBehavior<CircuitBreakerNotificationBehavior<CbNotificationMessage>, CbNotificationMessage, Task>();

            configure.RegisterBehavior<RetryNotificationBehavior<RetryNotificationMessage>, RetryNotificationMessage, Task>();
            configure.RegisterBehavior<TimeoutNotificationBehavior<RetryNotificationMessage>, RetryNotificationMessage, Task>();
            configure.RegisterBehavior<CircuitBreakerNotificationBehavior<RetryNotificationMessage>, RetryNotificationMessage, Task>();
        });

        configureServices(builder.Services);

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static async Task<IHost> CreateLoadHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(configure =>
        {
            configure.RegisterRequestHandler<LoadRequestHandler, LoadRequest, int>();
            configure.RegisterBehavior<RetryRequestBehavior<LoadRequest, int>, LoadRequest, Task<int>>();
            configure.RegisterBehavior<TimeoutRequestBehavior<LoadRequest, int>, LoadRequest, Task<int>>();
            configure.RegisterBehavior<CircuitBreakerRequestBehavior<LoadRequest, int>, LoadRequest, Task<int>>();
        });

        builder.Services.Configure<RetryBehaviorOptions>(opts => { opts.MaxRetryCount = 0; opts.Delay = TimeSpan.Zero; });
        builder.Services.Configure<TimeoutBehaviorOptions>(opts => { opts.RequestTimeout = TimeSpan.FromSeconds(30); });
        builder.Services.Configure<CircuitBreakerBehaviorOptions>(opts => { opts.FailureThreshold = 1000; opts.OpenDuration = TimeSpan.FromSeconds(1); });

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static async Task WaitForAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate()) return;
            await Task.Delay(10, cancellationToken);
        }

        Assert.Fail("Timed out waiting for notification processing.");
    }

    private static bool ShouldRunPerformanceTests() =>
        string.Equals(
            Environment.GetEnvironmentVariable("NETMEDIATE_RUN_PERFORMANCE_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    private static bool IsGitHubActions() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    // ── Message types ────────────────────────────────────────────────────────────────────────

    public sealed record RetryRequestMessage(string Value);
    public sealed record RetryNotificationViaMediatorMessage(string Value);
    public sealed record TimeoutRequestMessage(string Value);
    public sealed record CircuitBreakerRequestMessage(string Value);
    public sealed record TimeoutNotificationMessage(string Value = "");
    public sealed record CbNotificationMessage;
    public sealed record RetryNotificationMessage;
    public sealed record LoadRequest(int Value);
    // Unique message types for handler-based resilience tests (avoids static handler cache contamination)
    public sealed record SlowTimeoutNotificationMessage(string Value = "");
    public sealed record ThrowingCbMessage;
    public sealed record CountingThrowMessage;

    // ── Handlers ─────────────────────────────────────────────────────────────────────────────

    private sealed class RetryRequestHandler : IRequestHandler<RetryRequestMessage, string>
    {
        private static int s_attempts;
        public static int Attempts => Volatile.Read(ref s_attempts);
        public static void Reset() => Interlocked.Exchange(ref s_attempts, 0);

        public async Task<string> Handle(RetryRequestMessage query, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref s_attempts);
            if (attempt < 3)
                throw new InvalidOperationException($"failed attempt {attempt}");
            return query.Value;
        }
    }

    private sealed class TimeoutRequestHandler : IRequestHandler<TimeoutRequestMessage, string>
    {
        public async Task<string> Handle(TimeoutRequestMessage query, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            return query.Value;
        }
    }

    private sealed class CircuitBreakerRequestHandler : IRequestHandler<CircuitBreakerRequestMessage, string>
    {
        public Task<string> Handle(CircuitBreakerRequestMessage query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("request failure");
    }

    private sealed class RetryNotificationViaMediatorHandler : INotificationHandler<RetryNotificationViaMediatorMessage>
    {
        private static int s_attempts;
        public static int Attempts => Volatile.Read(ref s_attempts);
        public static void Reset() => Interlocked.Exchange(ref s_attempts, 0);

        public async Task Handle(RetryNotificationViaMediatorMessage notification, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref s_attempts);
            if (attempt < 3)
                throw new InvalidOperationException($"failed attempt {attempt}");
        }
    }

    private sealed class TimeoutNotificationHandler : INotificationHandler<TimeoutNotificationMessage>
    {
        public Task Handle(TimeoutNotificationMessage notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class CbNotificationHandler : INotificationHandler<CbNotificationMessage>
    {
        public Task Handle(CbNotificationMessage notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RetryNotificationHandler : INotificationHandler<RetryNotificationMessage>
    {
        public Task Handle(RetryNotificationMessage notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class LoadRequestHandler : IRequestHandler<LoadRequest, int>
    {
        public Task<int> Handle(LoadRequest query, CancellationToken cancellationToken = default)
            => Task.FromResult(query.Value + 1);
    }

    // ── Pipeline behaviors for notification resilience tests ─────────────────────────────────

    /// <summary>Delays 200 ms before calling next — used to trigger timeout behavior.</summary>
    private sealed class SlowPipelineBehavior<TMessage> : IPipelineBehavior<TMessage>
        where TMessage : notnull
    {
        public async Task Handle(TMessage message, PipelineBehaviorDelegate<TMessage, Task> next, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            await next(message, cancellationToken);
        }
    }

    /// <summary>Always throws — used to trigger circuit-breaker behavior.</summary>
    private sealed class ThrowingPipelineBehavior<TMessage> : IPipelineBehavior<TMessage>
        where TMessage : notnull
    {
        public Task Handle(TMessage message, PipelineBehaviorDelegate<TMessage, Task> next, CancellationToken cancellationToken)
            => throw new InvalidOperationException("behavior failure");
    }

    /// <summary>Always throws and counts invocations — used to test retry behavior.</summary>
    private sealed class CountingThrowBehavior<TMessage> : IPipelineBehavior<TMessage>
        where TMessage : notnull
    {
        private static int s_invocations;
        public static int Invocations => Volatile.Read(ref s_invocations);
        public static void Reset() => Interlocked.Exchange(ref s_invocations, 0);

        public Task Handle(TMessage message, PipelineBehaviorDelegate<TMessage, Task> next, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref s_invocations);
            throw new InvalidOperationException("retry trigger");
        }
    }

    // ── Notification handlers for the new message types ──────────────────────────────────────

    private sealed class SlowTimeoutNotificationHandler : INotificationHandler<SlowTimeoutNotificationMessage>
    {
        public Task Handle(SlowTimeoutNotificationMessage notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingCbNotificationHandler2 : INotificationHandler<ThrowingCbMessage>
    {
        public Task Handle(ThrowingCbMessage notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class CountingThrowNotificationHandler : INotificationHandler<CountingThrowMessage>
    {
        public Task Handle(CountingThrowMessage notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
