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
    public async Task CommandLoad_ShouldSustainMinimumThroughputInParallel()
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
                await mediator.Send(new LoadCommand(i), token);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT command_parallel tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(
            throughput > 500,
            $"Unexpected low parallel command throughput: {throughput:F2} ops/s"
        );
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
                var response = await mediator.Request<LoadRequest, int>(new(i), token);
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

    [Fact]
    public async Task NotificationLoad_ShouldSustainMinimumThroughput()
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
            await mediator.Notify(new LoadNotification(i), cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT notification tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(
            throughput > 500,
            $"Unexpected low notification throughput: {throughput:F2} ops/s"
        );
    }

    [Fact]
    public async Task NotificationLoad_ShouldSustainMinimumThroughputInParallel()
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
                await mediator.Notify(new LoadNotification(i), token);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT notification_parallel tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(
            throughput > 500,
            $"Unexpected low parallel notification throughput: {throughput:F2} ops/s"
        );
    }

    [Fact]
    public async Task StreamLoad_ShouldSustainMinimumThroughput()
    {
        if (!ShouldRunPerformanceTests())
            return;

        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var targetFramework = AppContext.TargetFrameworkName ?? "unknown";

        const int operations = 5_000;
        var start = Stopwatch.GetTimestamp();

        for (var i = 0; i < operations; i++)
        {
            await foreach (
                var _ in mediator.RequestStream<LoadStreamRequest, int>(new(i), cancellationToken)
            )
            { }
        }

        var elapsed = Stopwatch.GetElapsedTime(start);
        var throughput = operations / elapsed.TotalSeconds;

        output.WriteLine(
            $"LOAD_RESULT stream tfm={targetFramework} ops={operations} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );

        Assert.True(throughput > 500, $"Unexpected low stream throughput: {throughput:F2} ops/s");
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(configure =>
        {
            configure.RegisterCommandHandler<LoadCommandHandler, LoadCommand>();
            configure.RegisterRequestHandler<LoadRequestHandler, LoadRequest, int>();
            configure.RegisterNotificationHandler<LoadNotificationHandler, LoadNotification>();
            configure.RegisterStreamHandler<LoadStreamHandler, LoadStreamRequest, int>();
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

    public sealed record LoadCommand(int Value);

    public sealed record LoadRequest(int Value);

    public sealed record LoadNotification(int Value);

    public sealed record LoadStreamRequest(int Value);

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

    private sealed class LoadNotificationHandler : INotificationHandler<LoadNotification>
    {
        public Task Handle(
            LoadNotification notification,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    private sealed class LoadStreamHandler : IStreamHandler<LoadStreamRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(
            LoadStreamRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            for (var i = 0; i < 5; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return request.Value + i;
                await Task.Yield();
            }
        }
    }
}
