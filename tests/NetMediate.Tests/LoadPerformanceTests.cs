using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Tests;

public sealed class LoadPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CommandLoad_ShouldSustainMinimumThroughput()
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
            await mediator.Send(new LoadCommand(i), cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT command tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected low command throughput: {throughput:F2} ops/s");
    }

    [Fact]
    public async Task RequestLoad_ShouldSustainMinimumThroughputInParallel()
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
                var response = await mediator.Request<LoadRequest, int>(new LoadRequest(i), token);
                Assert.Equal(i + 1, response);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT request_parallel tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected low request throughput: {throughput:F2} ops/s");
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(typeof(LoadPerformanceTests).Assembly);

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

    public sealed record LoadCommand(int Value);
    public sealed record LoadRequest(int Value);

    private sealed class LoadCommandHandler : ICommandHandler<LoadCommand>
    {
        public Task Handle(LoadCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class LoadRequestHandler : IRequestHandler<LoadRequest, int>
    {
        public Task<int> Handle(LoadRequest query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Value + 1);
    }
}
