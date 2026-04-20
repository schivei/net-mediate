using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate.Resilience;

namespace NetMediate.Tests;

public sealed class ResilienceLoadPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RequestLoad_WithResiliencePackage_ShouldSustainMinimumThroughputInParallel()
    {
        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var cancellationToken = TestContext.Current.CancellationToken;

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
                var response = await mediator.Request<ResilienceLoadRequest, int>(
                    new ResilienceLoadRequest(i),
                    token
                );
                Assert.Equal(i + 1, response);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT resilience_request_parallel ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(
            throughput > 500,
            $"Unexpected low resilience request throughput: {throughput:F2} ops/s"
        );
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(typeof(ResilienceLoadPerformanceTests).Assembly);
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

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    public sealed record ResilienceLoadRequest(int Value);

    private sealed class ResilienceLoadRequestHandler : IRequestHandler<ResilienceLoadRequest, int>
    {
        public Task<int> Handle(
            ResilienceLoadRequest query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(query.Value + 1);
    }
}
