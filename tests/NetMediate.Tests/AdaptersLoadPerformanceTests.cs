using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate.Adapters;

namespace NetMediate.Tests;

/// <summary>
/// Load benchmarks for the notification pipeline with the Adapters behavior registered
/// but no concrete adapters wired — measures the per-call overhead of the behavior wrapper
/// without any external I/O.
/// </summary>
public sealed class AdaptersLoadPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task NotificationLoad_WithAdaptersBehavior_ShouldSustainMinimumThroughput()
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
            await mediator.Notify(new AdapterLoadNotification(i), cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT adapters_notification tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected low adapters notification throughput: {throughput:F2} ops/s");
    }

    [Fact]
    public async Task NotificationLoad_WithAdaptersBehavior_ShouldSustainMinimumThroughputInParallel()
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
                await mediator.Notify(new AdapterLoadNotification(i), token);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT adapters_notification_parallel tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected low parallel adapters notification throughput: {throughput:F2} ops/s");
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(configure =>
        {
            configure.RegisterNotificationHandler<AdapterLoadNotificationHandler, AdapterLoadNotification>();
        });
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

    public sealed record AdapterLoadNotification(int Value);

    private sealed class AdapterLoadNotificationHandler : INotificationHandler<AdapterLoadNotification>
    {
        public Task Handle(AdapterLoadNotification notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
