using System.Diagnostics;
using System.Text;
using MediatR;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetMediate.Benchmarks;

/// <summary>
/// Throughput benchmarks comparing NetMediate, MediatR 14, and martinothamar/Mediator 3.
/// <para>
/// Only features that are common across ALL included libraries are benchmarked:
/// command dispatch (fire-and-forget) and request/response dispatch.
/// No validation, behaviours, or notification fan-out – those are NetMediate-specific extras.
/// </para>
/// <para>
/// Gated by <c>NETMEDIATE_RUN_PERFORMANCE_TESTS=true</c>.  When the variable is set the tests
/// also update two markdown documents:
/// <list type="bullet">
///   <item><c>docs/BENCHMARK_COMPARISON.md</c> – detailed per-run results table</item>
///   <item><c>README.md</c> – concise "Performance" comparison summary table</item>
/// </list>
/// </para>
/// <para>
/// <b>TurboMediator</b> (v0.9.3) is included in the library comparison document but is excluded
/// from the live benchmarks because its source generator emits code that does not compile on
/// .NET 10 (implicit <c>ValueTask → ValueTask&lt;Unit&gt;</c> conversion missing).
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
    // MediatR 14 benchmarks
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
    // martinothamar/Mediator 3 benchmarks (source-generated, zero-reflection)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MartinMediator_Command_Throughput()
    {
        if (!ShouldRun()) return;

        using var host = await CreateMartinMediatorHostAsync();
        var mediator = host.Services.GetRequiredService<global::Mediator.IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var result = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++)
                await mediator.Send(new MtCommand(i), ct);
        }, CommandOps);

        output.WriteLine($"BENCH martinothamar/Mediator command: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"martinothamar/Mediator command throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    [Fact]
    public async Task MartinMediator_Request_Throughput()
    {
        if (!ShouldRun()) return;

        using var host = await CreateMartinMediatorHostAsync();
        var mediator = host.Services.GetRequiredService<global::Mediator.IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var result = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++)
            {
                var r = await mediator.Send(new MtQuery(i), ct);
                Assert.Equal(i + 1, r);
            }
        }, RequestOps);

        output.WriteLine($"BENCH martinothamar/Mediator request: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"martinothamar/Mediator request throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Comparison summary – runs all libraries and writes docs
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

        // martinothamar/Mediator
        using var mtHost = await CreateMartinMediatorHostAsync();
        var mtMediator = mtHost.Services.GetRequiredService<global::Mediator.IMediator>();
        var mtCmd = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++)
                await mtMediator.Send(new MtCommand(i), ct);
        }, CommandOps);
        var mtReq = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++)
                await mtMediator.Send(new MtQuery(i), ct);
        }, RequestOps);

        WriteComparisonDocs(timestamp, tfm, nmCmd, nmReq, mrCmd, mrReq, mtCmd, mtReq);

        output.WriteLine($"Benchmark docs written to {BenchmarkDocPath}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IHost> CreateNetMediateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
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
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<LibraryBenchmarkTests>());
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task<IHost> CreateMartinMediatorHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
        builder.Services.AddMediator(opt => opt.ServiceLifetime = ServiceLifetime.Singleton);
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
        BenchResult mrCmd, BenchResult mrReq,
        BenchResult mtCmd, BenchResult mtReq)
    {
        // ── BENCHMARK_COMPARISON.md ─────────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine("# Benchmark Comparison: NetMediate, MediatR 14 &amp; martinothamar/Mediator 3");
        sb.AppendLine();
        sb.AppendLine("> **Auto-generated** by `LibraryBenchmarkTests.Comparison_WritesBenchmarkDocs`.");
        sb.AppendLine("> Re-run with `NETMEDIATE_RUN_PERFORMANCE_TESTS=true` to refresh.");
        sb.AppendLine();
        sb.AppendLine($"**Last run:** {timestamp}  ");
        sb.AppendLine($"**Target framework:** `{tfm}`");
        sb.AppendLine();
        sb.AppendLine("## Throughput (operations / second — higher is better)");
        sb.AppendLine();
        sb.AppendLine("Only features that are **common to all included libraries** are benchmarked:");
        sb.AppendLine("fire-and-forget command dispatch and request/response dispatch with no-op handlers.");
        sb.AppendLine("Validation, pipeline behaviours, and notification fan-out are NetMediate extras");
        sb.AppendLine("and are deliberately omitted for a fair comparison.");
        sb.AppendLine();
        sb.AppendLine("| Scenario | NetMediate | MediatR 14 | martinothamar/Mediator 3 |");
        sb.AppendLine("|----------|:----------:|:----------:|:-----------------------:|");
        AppendRow3(sb, "Command (fire & forget)", nmCmd, mrCmd, mtCmd);
        AppendRow3(sb, "Request (query / response)", nmReq, mrReq, mtReq);
        sb.AppendLine();
        sb.AppendLine("> **TurboMediator** (v0.9.3) is listed in [LIBRARY_COMPARISON.md](LIBRARY_COMPARISON.md)");
        sb.AppendLine("> but excluded from live benchmarks: its source generator emits code that does not");
        sb.AppendLine("> compile on .NET 10 (`ValueTask → ValueTask<Unit>` implicit conversion removed).");
        sb.AppendLine();
        sb.AppendLine("## What the numbers mean");
        sb.AppendLine();
        sb.AppendLine("**martinothamar/Mediator** is typically the fastest because it generates a");
        sb.AppendLine("compile-time `switch`-expression dispatch with zero runtime reflection and no");
        sb.AppendLine("intermediate allocations — the generated code calls handlers directly.");
        sb.AppendLine();
        sb.AppendLine("**MediatR 14** is fast because it is intentionally minimal: no scoping, no");
        sb.AppendLine("validation, no tracing.  Its only overhead is a dictionary lookup per message type.");
        sb.AppendLine();
        sb.AppendLine("**NetMediate** pays for richer per-dispatch semantics that the other libraries");
        sb.AppendLine("do not offer:");
        sb.AppendLine();
        sb.AppendLine("| Per-dispatch cost | NetMediate | MediatR 14 | martinothamar/Mediator 3 |");
        sb.AppendLine("|---|:---:|:---:|:---:|");
        sb.AppendLine("| New DI scope (handler isolation) | ✅ yes | ❌ no | ❌ no |");
        sb.AppendLine("| Message validation pipeline | ✅ fast-path skip | ❌ no | ❌ no |");
        sb.AppendLine("| OpenTelemetry activity | ✅ listener-gated | ❌ no | ❌ no |");
        sb.AppendLine("| Background async logging | ✅ channel-based | ✅ varies | ✅ varies |");
        sb.AppendLine("| Source-generated dispatch | ❌ runtime DI | ❌ runtime DI | ✅ yes |");
        sb.AppendLine();
        sb.AppendLine("For handlers that perform any real I/O (database, HTTP, message broker, etc.)");
        sb.AppendLine("these per-dispatch costs are completely dominated by I/O latency and become");
        sb.AppendLine("irrelevant in practice.");
        sb.AppendLine();
        sb.AppendLine("## Measurement details");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Command | Request |");
        sb.AppendLine($"|--------|---------|---------|");
        sb.AppendLine($"| Operations | {CommandOps:N0} | {RequestOps:N0} |");
        sb.AppendLine($"| NetMediate elapsed | {nmCmd.Elapsed.TotalMilliseconds:F1} ms | {nmReq.Elapsed.TotalMilliseconds:F1} ms |");
        sb.AppendLine($"| MediatR elapsed | {mrCmd.Elapsed.TotalMilliseconds:F1} ms | {mrReq.Elapsed.TotalMilliseconds:F1} ms |");
        sb.AppendLine($"| martinothamar/Mediator elapsed | {mtCmd.Elapsed.TotalMilliseconds:F1} ms | {mtReq.Elapsed.TotalMilliseconds:F1} ms |");
        sb.AppendLine();
        sb.AppendLine("## Test environment");
        sb.AppendLine();
        sb.AppendLine($"- **OS:** {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($"- **Processor count:** {Environment.ProcessorCount}");
        sb.AppendLine($"- **Runtime:** {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine();
        sb.AppendLine("## Methodology");
        sb.AppendLine();
        sb.AppendLine("Each scenario runs one warm-up pass (JIT compile) followed by a single timed");
        sb.AppendLine("pass.  Operations run **sequentially** to measure single-thread throughput.");
        sb.AppendLine("All libraries share the same DI host infrastructure; handlers are no-op stubs.");
        sb.AppendLine("Logging is set to `Warning` level to avoid logger overhead in all libraries.");
        sb.AppendLine();
        sb.AppendLine("See `tests/NetMediate.Benchmarks/LibraryBenchmarkTests.cs` for the full source.");

        Directory.CreateDirectory(Path.GetDirectoryName(BenchmarkDocPath)!);
        File.WriteAllText(BenchmarkDocPath, sb.ToString());

        // ── README.md – update/insert the Performance section ──────────────
        UpdateReadmePerformanceSection(timestamp, tfm, nmCmd, nmReq, mrCmd, mrReq, mtCmd, mtReq);
    }

    private static void AppendRow3(StringBuilder sb, string scenario,
        BenchResult nm, BenchResult mr, BenchResult mt)
    {
        sb.AppendLine($"| {scenario} | {nm.OpsPerSec:N0} | {mr.OpsPerSec:N0} | {mt.OpsPerSec:N0} |");
    }

    private void UpdateReadmePerformanceSection(
        string timestamp, string tfm,
        BenchResult nmCmd, BenchResult nmReq,
        BenchResult mrCmd, BenchResult mrReq,
        BenchResult mtCmd, BenchResult mtReq)
    {
        if (!File.Exists(ReadMePath)) return;

        var section = new StringBuilder();
        section.AppendLine("## Performance");
        section.AppendLine();
        section.AppendLine($"> Last benchmarked: **{timestamp}** on `{tfm}` (sequential, no-op handlers, Warning log level).");
        section.AppendLine($"> Full details in [docs/BENCHMARK_COMPARISON.md](docs/BENCHMARK_COMPARISON.md).");
        section.AppendLine();
        section.AppendLine("| Scenario | NetMediate | MediatR 14 | martinothamar/Mediator 3 |");
        section.AppendLine("|----------|:----------:|:----------:|:-----------------------:|");
        AppendRow3(section, "Command", nmCmd, mrCmd, mtCmd);
        AppendRow3(section, "Request", nmReq, mrReq, mtReq);
        section.AppendLine();
        section.AppendLine("> NetMediate includes per-dispatch DI scoping, message validation fast-path,");
        section.AppendLine("> channel-based async logging, and OpenTelemetry activity tracking.");
        section.AppendLine("> For I/O-bound handlers the overhead is negligible vs actual I/O latency.");
        section.AppendLine();

        output.WriteLine($"NetMediate cmd: {nmCmd.OpsPerSec:N0} ops/s  MediatR: {mrCmd.OpsPerSec:N0} ops/s  martinMediator: {mtCmd.OpsPerSec:N0} ops/s");
        output.WriteLine($"NetMediate req: {nmReq.OpsPerSec:N0} ops/s  MediatR: {mrReq.OpsPerSec:N0} ops/s  martinMediator: {mtReq.OpsPerSec:N0} ops/s");

        const string startMarker = "<!-- PERF_START -->";
        const string endMarker = "<!-- PERF_END -->";

        var readme = File.ReadAllText(ReadMePath);

        var startIdx = readme.IndexOf(startMarker, StringComparison.Ordinal);
        var endIdx = readme.IndexOf(endMarker, StringComparison.Ordinal);

        string updatedReadme;
        if (startIdx >= 0 && endIdx > startIdx)
        {
            updatedReadme =
                readme[..(startIdx + startMarker.Length)] +
                "\n" +
                section.ToString() +
                readme[endIdx..];
        }
        else
        {
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
    // NetMediate message / handler definitions
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
    // MediatR 14 message / handler definitions
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

    // ─────────────────────────────────────────────────────────────────────────
    // martinothamar/Mediator 3 message / handler definitions
    // Must be internal (not nested private) so the source generator can access them.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fire-and-forget command for martinothamar/Mediator benchmarks.</summary>
    public sealed record MtCommand(int Value) : global::Mediator.ICommand;

    /// <summary>Query (request/response) for martinothamar/Mediator benchmarks.</summary>
    public sealed record MtQuery(int Value) : global::Mediator.IQuery<int>;
}

// martinothamar/Mediator handlers live outside the test class so the source generator
// (which requires at least internal visibility) can reference them.
internal sealed class MtCommandHandler : global::Mediator.ICommandHandler<NetMediate.Benchmarks.LibraryBenchmarkTests.MtCommand>
{
    public ValueTask<global::Mediator.Unit> Handle(
        NetMediate.Benchmarks.LibraryBenchmarkTests.MtCommand command, CancellationToken ct) =>
        ValueTask.FromResult(global::Mediator.Unit.Value);
}

internal sealed class MtQueryHandler : global::Mediator.IQueryHandler<NetMediate.Benchmarks.LibraryBenchmarkTests.MtQuery, int>
{
    public ValueTask<int> Handle(
        NetMediate.Benchmarks.LibraryBenchmarkTests.MtQuery query, CancellationToken ct) =>
        ValueTask.FromResult(query.Value + 1);
}
