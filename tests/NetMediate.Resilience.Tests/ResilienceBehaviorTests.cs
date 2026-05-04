using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NetMediate.Adapters;
using NetMediate.Resilience;

namespace NetMediate.Resilience.Tests;

public sealed class ResilienceBehaviorTests
{
    [Fact]
    public async Task RetryRequestBehavior_ShouldRetryUntilSuccess()
    {
        using var host = await CreateRequestHostAsync(services =>
        {
            services.AddSingleton(new RetryBehaviorOptions { MaxRetryCount = 3, Delay = TimeSpan.Zero });
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
            services.AddSingleton(new RetryBehaviorOptions { MaxRetryCount = 3, Delay = TimeSpan.Zero });
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
            services.AddSingleton(new TimeoutBehaviorOptions { RequestTimeout = TimeSpan.FromMilliseconds(20) });
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
            services.AddSingleton(new CircuitBreakerBehaviorOptions
            {
                FailureThreshold = 2,
                OpenDuration = TimeSpan.FromSeconds(30),
            });
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
            services.AddSingleton(new TimeoutBehaviorOptions { RequestTimeout = TimeSpan.Zero });
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
            services.AddSingleton(new TimeoutBehaviorOptions { NotificationTimeout = TimeSpan.Zero });
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
        using var host = await CreateNotificationHostAsync(services =>
        {
            services.AddSingleton(new TimeoutBehaviorOptions { NotificationTimeout = TimeSpan.FromMilliseconds(20) });
            services.AddNetMediateAdapters(opts => opts.ThrowOnAdapterFailure = true);
            services.AddSingleton<
                INotificationAdapter<TimeoutNotificationMessage>,
                SlowNotificationAdapter
            >();
        });

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
        using var host = await CreateNotificationHostAsync(services =>
        {
            services.AddSingleton(new CircuitBreakerBehaviorOptions
            {
                FailureThreshold = 2,
                OpenDuration = TimeSpan.FromSeconds(30),
            });
            services.AddNetMediateAdapters(opts => opts.ThrowOnAdapterFailure = true);
            services.AddSingleton<
                INotificationAdapter<CbNotificationMessage>,
                ThrowingNotificationAdapter
            >();
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new CbNotificationMessage(), TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new CbNotificationMessage(), TestContext.Current.CancellationToken)
        );

        var circuitEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new CbNotificationMessage(), TestContext.Current.CancellationToken)
        );
        Assert.Contains("Circuit open", circuitEx.Message);
    }

    [Fact]
    public async Task RetryNotificationBehavior_ShouldRetryViaAdapterException()
    {
        using var host = await CreateNotificationHostAsync(services =>
        {
            services.AddSingleton(new RetryBehaviorOptions { MaxRetryCount = 2, Delay = TimeSpan.Zero });
            services.AddNetMediateAdapters(opts => opts.ThrowOnAdapterFailure = true);
            services.AddSingleton<
                INotificationAdapter<RetryNotificationMessage>,
                CountingThrowAdapter
            >();
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new RetryNotificationMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal(3, CountingThrowAdapter.Invocations);
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
        builder.Services.AddNetMediate(configure =>
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

        // Register test-specific options first; fallback defaults come last via TryAddSingleton.
        configureServices(builder.Services);
        builder.Services.TryAddSingleton(new RetryBehaviorOptions());
        builder.Services.TryAddSingleton(new TimeoutBehaviorOptions());
        builder.Services.TryAddSingleton(new CircuitBreakerBehaviorOptions());

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
        builder.Services.TryAddSingleton(new RetryBehaviorOptions());
        builder.Services.TryAddSingleton(new TimeoutBehaviorOptions());
        builder.Services.TryAddSingleton(new CircuitBreakerBehaviorOptions());

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static async Task<IHost> CreateLoadHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(configure =>
        {
            configure.RegisterRequestHandler<LoadRequestHandler, LoadRequest, int>();
            configure.RegisterBehavior<RetryRequestBehavior<LoadRequest, int>, LoadRequest, Task<int>>();
            configure.RegisterBehavior<TimeoutRequestBehavior<LoadRequest, int>, LoadRequest, Task<int>>();
            configure.RegisterBehavior<CircuitBreakerRequestBehavior<LoadRequest, int>, LoadRequest, Task<int>>();
        });

        builder.Services.AddSingleton(new RetryBehaviorOptions { MaxRetryCount = 0, Delay = TimeSpan.Zero });
        builder.Services.AddSingleton(new TimeoutBehaviorOptions { RequestTimeout = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton(new CircuitBreakerBehaviorOptions
        {
            FailureThreshold = 1000,
            OpenDuration = TimeSpan.FromSeconds(1),
        });

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

    // ── Adapters ─────────────────────────────────────────────────────────────────────────────

    private sealed class SlowNotificationAdapter : INotificationAdapter<TimeoutNotificationMessage>
    {
        public async Task ForwardAsync(AdapterEnvelope<TimeoutNotificationMessage> envelope, CancellationToken cancellationToken = default)
            => await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
    }

    private sealed class ThrowingNotificationAdapter : INotificationAdapter<CbNotificationMessage>
    {
        public Task ForwardAsync(AdapterEnvelope<CbNotificationMessage> envelope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("adapter failure");
    }

    private sealed class CountingThrowAdapter : INotificationAdapter<RetryNotificationMessage>
    {
        private static int s_invocations;
        public static int Invocations => Volatile.Read(ref s_invocations);
        public static void Reset() => Interlocked.Exchange(ref s_invocations, 0);

        public Task ForwardAsync(AdapterEnvelope<RetryNotificationMessage> envelope, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref s_invocations);
            throw new InvalidOperationException("retry trigger");
        }
    }
}
