using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboMediator;
using TurboMediator.Generated;

namespace NetMediate.Benchmarks.TurboMediator;

/// <summary>
/// Throughput benchmarks for <b>TurboMediator</b> (v0.9.*) isolated in a dedicated
/// <c>net8.0</c> project because the TurboMediator source generator emits a
/// <c>ValueTask → ValueTask&lt;Unit&gt;</c> implicit conversion that was removed in
/// .NET 10 and therefore fails to compile in a multi-target assembly that also
/// contains .NET 10 targets.
/// <para>
/// Results are written to <c>docs/.turbo-bench-results.json</c> (relative to the
/// solution root).  The main <c>NetMediate.Benchmarks</c> project reads this file
/// when generating the unified <c>BENCHMARK_COMPARISON.md</c> document, so
/// TurboMediator numbers appear in the same table as all other libraries even though
/// they are produced by a separate test run.
/// </para>
/// <para>Gated by <c>NETMEDIATE_RUN_PERFORMANCE_TESTS=true</c>.</para>
/// </summary>
public sealed class TurboMediatorBenchmarkTests(ITestOutputHelper output)
{
    private const int CommandOps = 20_000;
    private const int RequestOps = 10_000;
    private const double MinThroughput = 500;

    private static readonly string SolutionRoot = FindSolutionRoot();

    // ─── Sidecar result file read by the main benchmark project ──────────────
    private static readonly string ResultsFilePath =
        Path.Combine(SolutionRoot, "docs", ".turbo-bench-results.json");

    // ─────────────────────────────────────────────────────────────────────────
    // Individual fact tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TurboMediator_Command_Throughput()
    {
        if (!ShouldRun()) return;
        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;
        var result = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++)
                await mediator.Send(new TmCommand(i), ct);
        }, CommandOps);
        output.WriteLine($"BENCH TurboMediator command: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"TurboMediator command throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    [Fact]
    public async Task TurboMediator_Request_Throughput()
    {
        if (!ShouldRun()) return;
        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;
        var result = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++)
            {
                var r = await mediator.Send(new TmQuery(i), ct);
                Assert.Equal(i + 1, r);
            }
        }, RequestOps);
        output.WriteLine($"BENCH TurboMediator request: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"TurboMediator request throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Summary test – writes sidecar JSON for the main comparison doc
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TurboMediator_WritesBenchmarkResults()
    {
        if (!ShouldRun()) return;

        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var cmdResult = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++)
                await mediator.Send(new TmCommand(i), ct);
        }, CommandOps);

        var reqResult = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++)
                await mediator.Send(new TmQuery(i), ct);
        }, RequestOps);

        var results = new TurboBenchResults(
            Timestamp: DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm UTC"),
            TargetFramework: AppContext.TargetFrameworkName ?? "net8.0",
            Os: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Runtime: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            CommandOps: cmdResult.Ops,
            CommandElapsedMs: cmdResult.Elapsed.TotalMilliseconds,
            CommandOpsPerSec: cmdResult.OpsPerSec,
            RequestOps: reqResult.Ops,
            RequestElapsedMs: reqResult.Elapsed.TotalMilliseconds,
            RequestOpsPerSec: reqResult.OpsPerSec
        );

        Directory.CreateDirectory(Path.GetDirectoryName(ResultsFilePath)!);
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ResultsFilePath, json, ct);

        output.WriteLine($"TurboMediator results written to {ResultsFilePath}");
        output.WriteLine($"  Command: {cmdResult}");
        output.WriteLine($"  Request: {reqResult}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Host factory
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddTurboMediator();
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<BenchResult> MeasureAsync(Func<Task> action, int ops)
    {
        await action(); // warm up
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        return new BenchResult(ops, sw.Elapsed);
    }

    private static bool ShouldRun() =>
        string.Equals(
            Environment.GetEnvironmentVariable("NETMEDIATE_RUN_PERFORMANCE_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }

    private readonly record struct BenchResult(int Ops, TimeSpan Elapsed)
    {
        public double OpsPerSec => Ops / Elapsed.TotalSeconds;
        public override string ToString() =>
            $"{OpsPerSec:N0} ops/s ({Ops:N0} ops in {Elapsed.TotalMilliseconds:F1} ms)";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// TurboMediator message and handler types.
// Must be at least internal (not nested private) so the source generator discovers them.
// Must be in their own project so the generator does NOT pick up handler types from
// other libraries (NetMediate, MediatR, martinothamar/Mediator) whose interface names
// share the same short-name pattern as TurboMediator's handler interfaces.
// ─────────────────────────────────────────────────────────────────────────────

// NOTE: TurboMediator v0.9.3 source generator emits a ValueTask → ValueTask<Unit>
// implicit conversion for ICommandHandler<T> (single type param) that is rejected
// by the .NET 10 C# compiler even when targeting net8.0.  As a workaround for the
// "command" benchmark we use IQuery<Unit> (whose handler returns ValueTask<Unit>
// directly, requiring no implicit conversion).  Dispatch overhead is identical.

/// <summary>No-op command (modelled as IQuery&lt;Unit&gt;) for TurboMediator benchmarks.</summary>
public sealed record TmCommand(int Value) : IQuery<Unit>;

/// <summary>Query (request/response) for TurboMediator benchmarks.</summary>
public sealed record TmQuery(int Value) : IQuery<int>;

// TURBO008 requires public visibility.
public sealed class TmCommandHandler : IQueryHandler<TmCommand, Unit>
{
    public ValueTask<Unit> Handle(TmCommand command, CancellationToken ct) =>
        ValueTask.FromResult(Unit.Value);
}

public sealed class TmQueryHandler : IQueryHandler<TmQuery, int>
{
    public ValueTask<int> Handle(TmQuery query, CancellationToken ct) =>
        ValueTask.FromResult(query.Value + 1);
}

// ─────────────────────────────────────────────────────────────────────────────
// Sidecar JSON model (read by the main NetMediate.Benchmarks project)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Serialised TurboMediator benchmark results written to
/// <c>docs/.turbo-bench-results.json</c> and consumed by the main comparison doc writer.
/// </summary>
public sealed record TurboBenchResults(
    string Timestamp,
    string TargetFramework,
    string Os,
    string Runtime,
    int CommandOps,
    double CommandElapsedMs,
    double CommandOpsPerSec,
    int RequestOps,
    double RequestElapsedMs,
    double RequestOpsPerSec
);
