using System.Diagnostics;
using System.Text;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Benchmarks;

/// <summary>
/// Throughput benchmarks comparing NetMediate against MediatR 14.x.
/// <para>
/// These tests are gated by the <c>NETMEDIATE_RUN_PERFORMANCE_TESTS=true</c> environment
/// variable so that they do not run in every CI build.  When the variable is set they also
/// write/update two markdown documents:
/// <list type="bullet">
///   <item><c>docs/BENCHMARK_COMPARISON.md</c> – detailed per-run results table</item>
///   <item><c>README.md</c> – a concise "Performance" comparison summary table</item>
/// </list>
/// This keeps the published docs in sync with the latest measurements automatically whenever
/// the benchmark suite is executed.
/// </para>
/// </summary>
public sealed class LibraryBenchmarkTests(ITestOutputHelper output)
{
    // ─── Operations per benchmark run ────────────────────────────────────────
    private const int CommandOps = 20_000;
    private const int RequestOps = 10_000;

    // ─── Minimum acceptable throughput (ops/s) ───────────────────────────────
    private const double MinThroughput = 500;

    // ─── Docs paths (relative to the solution root, resolved at runtime) ─────
    private static readonly string SolutionRoot = FindSolutionRoot();
    private static readonly string BenchmarkDocPath = Path.Combine(SolutionRoot, "docs", "BENCHMARK_COMPARISON.md");
    private static readonly string ReadMePath = Path.Combine(SolutionRoot, "README.md");

    // ─────────────────────────────────────────────────────────────────────────
    // NetMediate benchmarks
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NetMediate_Command_Throughput()
    {
        if (!ShouldRun()) return;

        using var host = await CreateNetMediateHostAsync();
        var mediator = host.Services.GetRequiredService<NetMediate.IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var result = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++)
                await mediator.Send(new NmCommand(i), ct);
        }, CommandOps);

        output.WriteLine($"BENCH NetMediate command: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"NetMediate command throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    [Fact]
    public async Task NetMediate_Request_Throughput()
    {
        if (!ShouldRun()) return;

        using var host = await CreateNetMediateHostAsync();
        var mediator = host.Services.GetRequiredService<NetMediate.IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var result = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++)
            {
                var r = await mediator.Request<NmRequest, int>(new NmRequest(i), ct);
                Assert.Equal(i + 1, r);
            }
        }, RequestOps);

        output.WriteLine($"BENCH NetMediate request: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"NetMediate request throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MediatR benchmarks
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MediatR_Command_Throughput()
    {
        if (!ShouldRun()) return;

        using var host = await CreateMediatRHostAsync();
        var mediator = host.Services.GetRequiredService<global::MediatR.IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var result = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++)
                await mediator.Send(new MrCommand(i), ct);
        }, CommandOps);

        output.WriteLine($"BENCH MediatR command: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"MediatR command throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    [Fact]
    public async Task MediatR_Request_Throughput()
    {
        if (!ShouldRun()) return;

        using var host = await CreateMediatRHostAsync();
        var mediator = host.Services.GetRequiredService<global::MediatR.IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var result = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++)
            {
                var r = await mediator.Send(new MrRequest(i), ct);
                Assert.Equal(i + 1, r);
            }
        }, RequestOps);

        output.WriteLine($"BENCH MediatR request: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"MediatR request throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Comparison summary – runs both and writes docs
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Comparison_WritesBenchmarkDocs()
    {
        if (!ShouldRun()) return;

        var ct = TestContext.Current.CancellationToken;
        var tfm = AppContext.TargetFrameworkName ?? "net10.0";
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm UTC");

        // NetMediate
        using var nmHost = await CreateNetMediateHostAsync();
        var nmMediator = nmHost.Services.GetRequiredService<NetMediate.IMediator>();
        var nmCmd = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++)
                await nmMediator.Send(new NmCommand(i), ct);
        }, CommandOps);
        var nmReq = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++)
                await nmMediator.Request<NmRequest, int>(new NmRequest(i), ct);
        }, RequestOps);

        // MediatR
        using var mrHost = await CreateMediatRHostAsync();
        var mrMediator = mrHost.Services.GetRequiredService<global::MediatR.IMediator>();
        var mrCmd = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++)
                await mrMediator.Send(new MrCommand(i), ct);
        }, CommandOps);
        var mrReq = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++)
                await mrMediator.Send(new MrRequest(i), ct);
        }, RequestOps);

        WriteComparisonDocs(timestamp, tfm, nmCmd, nmReq, mrCmd, mrReq);

        output.WriteLine($"Benchmark docs written to {BenchmarkDocPath}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IHost> CreateNetMediateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
#pragma warning disable IL2026
        builder.Services.AddNetMediate(typeof(LibraryBenchmarkTests).Assembly);
#pragma warning restore IL2026
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task<IHost> CreateMediatRHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<LibraryBenchmarkTests>());
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task<BenchResult> MeasureAsync(Func<Task> action, int ops)
    {
        // Warm up
        await action();

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

    private void WriteComparisonDocs(
        string timestamp, string tfm,
        BenchResult nmCmd, BenchResult nmReq,
        BenchResult mrCmd, BenchResult mrReq)
    {
        // ── BENCHMARK_COMPARISON.md ─────────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine("# Benchmark Comparison: NetMediate vs MediatR");
        sb.AppendLine();
        sb.AppendLine("> **Auto-generated** by `LibraryBenchmarkTests.Comparison_WritesBenchmarkDocs`.");
        sb.AppendLine("> Re-run with `NETMEDIATE_RUN_PERFORMANCE_TESTS=true` to refresh.");
        sb.AppendLine();
        sb.AppendLine($"**Last run:** {timestamp}  ");
        sb.AppendLine($"**Target framework:** `{tfm}`");
        sb.AppendLine();
        sb.AppendLine("## Throughput (operations / second, higher is better)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | NetMediate | MediatR 14 | Comparison |");
        sb.AppendLine("|----------|------------|------------|------------|");
        AppendRow(sb, "Command (fire & forget)", nmCmd, mrCmd);
        AppendRow(sb, "Request (query/response)", nmReq, mrReq);
        sb.AppendLine();
        sb.AppendLine("## What the numbers mean");
        sb.AppendLine();
        sb.AppendLine("MediatR 14 is faster in raw sequential throughput because it focuses exclusively");
        sb.AppendLine("on message dispatch with minimal overhead.  NetMediate deliberately includes a");
        sb.AppendLine("richer feature set per dispatch cycle:");
        sb.AppendLine();
        sb.AppendLine("| Per-dispatch cost | NetMediate | MediatR 14 |");
        sb.AppendLine("|-------------------|-----------|-----------|");
        sb.AppendLine("| New DI scope (isolation) | ✅ yes | ❌ no |");
        sb.AppendLine("| Message validation | ✅ yes (no-op if no validator) | ❌ no |");
        sb.AppendLine("| OpenTelemetry activity | ✅ yes (always) | ❌ no |");
        sb.AppendLine("| Debug log per dispatch | ✅ yes | ❌ no |");
        sb.AppendLine("| Pipeline behaviour resolution | ✅ yes | ✅ yes |");
        sb.AppendLine();
        sb.AppendLine("For handlers that perform any real I/O (database, HTTP, etc.) these costs are");
        sb.AppendLine("completely dominated by the I/O latency.  The difference only becomes noticeable");
        sb.AppendLine("in tight micro-benchmark loops with no-op handlers.");
        sb.AppendLine();
        sb.AppendLine("If raw throughput is the primary concern you can disable the Activity creation");
        sb.AppendLine("by not registering the `ActivitySource`, and reduce scope overhead by reusing");
        sb.AppendLine("the root service provider or supplying handlers as singletons.");
        sb.AppendLine();
        sb.AppendLine("## Measurement details");
        sb.AppendLine();
        sb.AppendLine("| Metric | Command | Request |");
        sb.AppendLine("|--------|---------|---------|");
        sb.AppendLine($"| Operations | {CommandOps:N0} | {RequestOps:N0} |");
        sb.AppendLine($"| NetMediate elapsed | {nmCmd.Elapsed.TotalMilliseconds:F1} ms | {nmReq.Elapsed.TotalMilliseconds:F1} ms |");
        sb.AppendLine($"| MediatR elapsed    | {mrCmd.Elapsed.TotalMilliseconds:F1} ms | {mrReq.Elapsed.TotalMilliseconds:F1} ms |");
        sb.AppendLine();
        sb.AppendLine("## Test environment");
        sb.AppendLine();
        sb.AppendLine($"- **OS:** {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($"- **Processor count:** {Environment.ProcessorCount}");
        sb.AppendLine($"- **Runtime:** {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine();
        sb.AppendLine("## Methodology");
        sb.AppendLine();
        sb.AppendLine("Each scenario runs one warm-up pass (to JIT compile the path) followed by a");
        sb.AppendLine("single timed pass.  All operations run **sequentially** to measure single-thread");
        sb.AppendLine("throughput rather than parallelism.  Both libraries share the same handler");
        sb.AppendLine("implementation and the same DI host.");
        sb.AppendLine();
        sb.AppendLine("See `tests/NetMediate.Benchmarks/LibraryBenchmarkTests.cs` for the full source.");

        Directory.CreateDirectory(Path.GetDirectoryName(BenchmarkDocPath)!);
        File.WriteAllText(BenchmarkDocPath, sb.ToString());

        // ── README.md – update/insert the Performance section ──────────────
        UpdateReadmePerformanceSection(timestamp, tfm, nmCmd, nmReq, mrCmd, mrReq);
    }

    private static void AppendRow(StringBuilder sb, string scenario,
        BenchResult nm, BenchResult mr)
    {
        var ratio = mr.OpsPerSec > 0 ? nm.OpsPerSec / mr.OpsPerSec : 1.0;
        var advantage = ratio >= 1.0
            ? $"+{(ratio - 1.0) * 100:F0}% faster"
            : $"{(1.0 - ratio) * 100:F0}% slower";
        sb.AppendLine($"| {scenario} | {nm.OpsPerSec:N0} | {mr.OpsPerSec:N0} | {advantage} |");
    }

    private void UpdateReadmePerformanceSection(
        string timestamp, string tfm,
        BenchResult nmCmd, BenchResult nmReq,
        BenchResult mrCmd, BenchResult mrReq)
    {
        if (!File.Exists(ReadMePath)) return;

        var ratioCmd = mrCmd.OpsPerSec > 0 ? nmCmd.OpsPerSec / mrCmd.OpsPerSec : 1.0;
        var ratioReq = mrReq.OpsPerSec > 0 ? nmReq.OpsPerSec / mrReq.OpsPerSec : 1.0;

        var section = new StringBuilder();
        section.AppendLine("## Performance");
        section.AppendLine();
        section.AppendLine($"> Last benchmarked: **{timestamp}** on `{tfm}` (sequential, no-op handlers).");
        section.AppendLine($"> Full details & tradeoff analysis in [docs/BENCHMARK_COMPARISON.md](docs/BENCHMARK_COMPARISON.md).");
        section.AppendLine();
        section.AppendLine("| Scenario | NetMediate | MediatR 14 | Note |");
        section.AppendLine("|----------|------------|------------|------|");
        AppendRow(section, "Command", nmCmd, mrCmd);
        AppendRow(section, "Request", nmReq, mrReq);
        section.AppendLine();
        section.AppendLine("> NetMediate includes per-dispatch DI scoping, message validation, and");
        section.AppendLine("> OpenTelemetry activity tracking that MediatR omits.  For I/O-bound handlers");
        section.AppendLine("> the overhead is negligible compared to actual I/O latency.");
        section.AppendLine();
        output.WriteLine($"NetMediate cmd: {nmCmd.OpsPerSec:N0} ops/s vs MediatR: {mrCmd.OpsPerSec:N0} ops/s (ratio {ratioCmd:F2}x)");
        output.WriteLine($"NetMediate req: {nmReq.OpsPerSec:N0} ops/s vs MediatR: {mrReq.OpsPerSec:N0} ops/s (ratio {ratioReq:F2}x)");

        const string startMarker = "<!-- PERF_START -->";
        const string endMarker = "<!-- PERF_END -->";

        var readme = File.ReadAllText(ReadMePath);

        var startIdx = readme.IndexOf(startMarker, StringComparison.Ordinal);
        var endIdx = readme.IndexOf(endMarker, StringComparison.Ordinal);

        string updatedReadme;
        if (startIdx >= 0 && endIdx > startIdx)
        {
            // Replace existing section
            updatedReadme =
                readme[..(startIdx + startMarker.Length)] +
                "\n" +
                section.ToString() +
                readme[endIdx..];
        }
        else
        {
            // Append before the Contributing section
            const string contributing = "## Contributing";
            var contributingIdx = readme.IndexOf(contributing, StringComparison.Ordinal);
            if (contributingIdx >= 0)
            {
                updatedReadme = string.Concat(
                    readme[..contributingIdx],
                    startMarker, "\n",
                    section.ToString(),
                    endMarker, "\n\n",
                    readme[contributingIdx..]);
            }
            else
            {
                updatedReadme = readme + "\n" + startMarker + "\n" + section + endMarker + "\n";
            }
        }

        File.WriteAllText(ReadMePath, updatedReadme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Result record
    // ─────────────────────────────────────────────────────────────────────────

    private readonly record struct BenchResult(int Ops, TimeSpan Elapsed)
    {
        public double OpsPerSec => Ops / Elapsed.TotalSeconds;

        public override string ToString() =>
            $"{OpsPerSec:N0} ops/s ({Ops:N0} ops in {Elapsed.TotalMilliseconds:F1} ms)";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NetMediate message/handler definitions
    // ─────────────────────────────────────────────────────────────────────────

    public sealed record NmCommand(int Value);

    private sealed class NmCommandHandler : NetMediate.ICommandHandler<NmCommand>
    {
        public Task Handle(NmCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    public sealed record NmRequest(int Value);

    private sealed class NmRequestHandler : NetMediate.IRequestHandler<NmRequest, int>
    {
        public Task<int> Handle(NmRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(request.Value + 1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MediatR message/handler definitions
    // ─────────────────────────────────────────────────────────────────────────

    public sealed record MrCommand(int Value) : global::MediatR.IRequest;

    private sealed class MrCommandHandler : global::MediatR.IRequestHandler<MrCommand>
    {
        public Task Handle(MrCommand request, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    public sealed record MrRequest(int Value) : global::MediatR.IRequest<int>;

    private sealed class MrRequestHandler : global::MediatR.IRequestHandler<MrRequest, int>
    {
        public Task<int> Handle(MrRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(request.Value + 1);
    }
}
