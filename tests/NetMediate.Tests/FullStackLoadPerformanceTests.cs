using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate.Adapters;
using NetMediate.Resilience;

namespace NetMediate.Tests;

/// <summary>
/// Load benchmarks with both <c>NetMediate.Resilience</c> and <c>NetMediate.Adapters</c> registered
/// simultaneously — measures the combined pipeline overhead of all three resilience behaviors plus
/// the adapter forwarding wrapper.
/// </summary>
public sealed class FullStackLoadPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RequestLoad_FullStack_ShouldSustainMinimumThroughputInParallel()
    {
        if (!ShouldRunPerformanceTests())
            return;

        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var targetFramework = AppContext.TargetFrameworkName ?? "unknown";

        const int operations = 10_000;
        var start = Stopwatch.GetTimestamp();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, operations),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
                CancellationToken = cancellationToken,
            },
            async (i, token) =>
            {
                var response = await mediator.Request<FullStackRequest, int>(new(i), token);
                Assert.Equal(i + 1, response);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT fullstack_request_parallel tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        var minimumThroughput = GetMinimumThroughput();
        Assert.True(
            throughput >= minimumThroughput,
            $"Unexpected full-stack request throughput: {throughput:F2} ops/s. Minimum expected: {minimumThroughput:F2} ops/s."
        );
    }

    [Fact]
    public async Task NotificationLoad_FullStack_ShouldSustainMinimumThroughput()
    {
        if (!ShouldRunPerformanceTests())
            return;

        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var targetFramework = AppContext.TargetFrameworkName ?? "unknown";

        const int operations = 20_000;
        var start = Stopwatch.GetTimestamp();

        for (var i = 0; i < operations; i++)
            await mediator.Notify(new FullStackNotification(i), cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT fullstack_notification tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected full-stack notification throughput: {throughput:F2} ops/s");
    }

    [Fact]
    public async Task NotificationLoad_FullStack_ShouldSustainMinimumThroughputInParallel()
    {
        if (!ShouldRunPerformanceTests())
            return;

        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var targetFramework = AppContext.TargetFrameworkName ?? "unknown";

        const int operations = 10_000;
        var start = Stopwatch.GetTimestamp();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, operations),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
                CancellationToken = cancellationToken,
            },
            async (i, token) =>
            {
                await mediator.Notify(new FullStackNotification(i), token);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT fullstack_notification_parallel tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected parallel full-stack notification throughput: {throughput:F2} ops/s");
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(configure =>
        {
            configure.RegisterHandler<IRequestHandler<FullStackRequest, int>, FullStackRequestHandler, FullStackRequest, Task<int>>();
            configure.RegisterHandler<INotificationHandler<FullStackNotification>, FullStackNotificationHandler, FullStackNotification, Task>();
        });
        builder.Services.AddNetMediateResilience(
            configureRetry: options =>
            {
                options.MaxRetryCount = 0;
                options.Delay = TimeSpan.Zero;
            },
            configureTimeout: options =>
            {
                options.RequestTimeout = TimeSpan.FromSeconds(30);
                options.NotificationTimeout = TimeSpan.FromSeconds(30);
            },
            configureCircuitBreaker: options =>
            {
                options.FailureThreshold = 1000;
                options.OpenDuration = TimeSpan.FromSeconds(1);
            }
        );
        builder.Services.AddNetMediateAdapters();

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static bool ShouldRunPerformanceTests() =>
        string.Equals(
            Environment.GetEnvironmentVariable("NETMEDIATE_RUN_PERFORMANCE_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    private static double GetMinimumThroughput() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase
        )
            ? 20_000d
            : 40_000d;

    public sealed record FullStackRequest(int Value);
    public sealed record FullStackNotification(int Value);

    private sealed class FullStackRequestHandler : IRequestHandler<FullStackRequest, int>
    {
        public Task<int> Handle(FullStackRequest query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Value + 1);
    }

    private sealed class FullStackNotificationHandler : INotificationHandler<FullStackNotification>
    {
        public Task Handle(FullStackNotification notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
