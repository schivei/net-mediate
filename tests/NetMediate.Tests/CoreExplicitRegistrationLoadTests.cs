using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Tests;

/// <summary>
/// Load benchmarks using explicit (AOT-safe) registration — the same code path produced by the
/// source generator.  No assembly scanning is performed.
/// </summary>
public sealed class CoreExplicitRegistrationLoadTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CommandLoad_ExplicitRegistration_ShouldSustainMinimumThroughput()
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
            await mediator.Send(new ExplicitLoadCommand(i), cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT explicit_command tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected low explicit command throughput: {throughput:F2} ops/s");
    }

    [Fact]
    public async Task CommandLoad_ExplicitRegistration_ShouldSustainMinimumThroughputInParallel()
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
                await mediator.Send(new ExplicitLoadCommand(i), token);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT explicit_command_parallel tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected low parallel explicit command throughput: {throughput:F2} ops/s");
    }

    [Fact]
    public async Task RequestLoad_ExplicitRegistration_ShouldSustainMinimumThroughputInParallel()
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
                var response = await mediator.Request<ExplicitLoadRequest, int>(new(i), token);
                Assert.Equal(i + 1, response);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT explicit_request_parallel tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected low parallel explicit request throughput: {throughput:F2} ops/s");
    }

    [Fact]
    public async Task NotificationLoad_ExplicitRegistration_ShouldSustainMinimumThroughput()
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
            await mediator.Notify(new ExplicitLoadNotification(i), cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT explicit_notification tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected low explicit notification throughput: {throughput:F2} ops/s");
    }

    [Fact]
    public async Task NotificationLoad_ExplicitRegistration_ShouldSustainMinimumThroughputInParallel()
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
                await mediator.Notify(new ExplicitLoadNotification(i), token);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT explicit_notification_parallel tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected low parallel explicit notification throughput: {throughput:F2} ops/s");
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        // Explicit (AOT-safe) registration — no assembly scanning.
        // This is the same code path emitted by NetMediate.SourceGeneration.
        builder.Services.UseNetMediate(configure =>
        {
            configure.RegisterCommandHandler<ExplicitLoadCommandHandler, ExplicitLoadCommand>();
            configure.RegisterRequestHandler<ExplicitLoadRequestHandler, ExplicitLoadRequest, int>();
            configure.RegisterNotificationHandler<ExplicitLoadNotificationHandler, ExplicitLoadNotification>();
        });

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

    public sealed record ExplicitLoadCommand(int Value);
    public sealed record ExplicitLoadRequest(int Value);
    public sealed record ExplicitLoadNotification(int Value);

    private sealed class ExplicitLoadCommandHandler : ICommandHandler<ExplicitLoadCommand>
    {
        public Task Handle(ExplicitLoadCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ExplicitLoadRequestHandler : IRequestHandler<ExplicitLoadRequest, int>
    {
        public Task<int> Handle(ExplicitLoadRequest query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Value + 1);
    }

    private sealed class ExplicitLoadNotificationHandler : INotificationHandler<ExplicitLoadNotification>
    {
        public Task Handle(ExplicitLoadNotification notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
