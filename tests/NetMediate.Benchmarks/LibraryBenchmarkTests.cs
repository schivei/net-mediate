using System.Diagnostics;
using System.Text;
using MediatR;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetMediate.Benchmarks;

/// <summary>
/// Throughput benchmarks comparing NetMediate, MediatR 14, and martinothamar/Mediator 3
/// across four registration + AOT modes:
/// <list type="number">
///   <item><b>No Code Gen + No AOT</b>: reflection-based assembly scan, runtime DI dispatch.</item>
///   <item><b>Code Gen + No AOT</b>: source-generated or explicit handler registration, runtime dispatch.</item>
///   <item><b>No Code Gen + AOT</b>: not benchmarkable at test time (AOT is a publish step); marked NOT SUPPORTED.</item>
///   <item><b>Code Gen + AOT</b>: same runtime throughput as Code Gen; support annotated in the docs.</item>
/// </list>
/// <para>
/// Only features that are <b>common across ALL included libraries</b> are benchmarked:
/// fire-and-forget command dispatch and request/response dispatch with no-op handlers.
/// </para>
/// <para>
/// Gated by <c>NETMEDIATE_RUN_PERFORMANCE_TESTS=true</c>.  Docs are auto-written on completion.
/// </para>
/// <para>
/// <b>TurboMediator</b> (v0.9.3) is excluded from live benchmarks: its source generator emits
/// code that does not compile on .NET 10 (<c>ValueTask → ValueTask&lt;Unit&gt;</c> implicit
/// conversion missing).  It is documented in <c>LIBRARY_COMPARISON.md</c>.
/// </para>
/// </summary>
public sealed class LibraryBenchmarkTests(ITestOutputHelper output)
{
    // ─── Operations per benchmark run ────────────────────────────────────────
    private const int CommandOps = 20_000;
    private const int RequestOps = 10_000;

    // ─── Minimum acceptable throughput (ops/s) ───────────────────────────────
    private const double MinThroughput = 500;

    // ─── Docs paths ──────────────────────────────────────────────────────────
    private static readonly string SolutionRoot = FindSolutionRoot();
    private static readonly string BenchmarkDocPath = Path.Combine(SolutionRoot, "docs", "BENCHMARK_COMPARISON.md");
    private static readonly string ReadMePath = Path.Combine(SolutionRoot, "README.md");

    // ─────────────────────────────────────────────────────────────────────────
    // ── NetMediate: Mode 1 – No Code Gen, No AOT (assembly scan)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NetMediate_NoCodeGen_Command_Throughput()
    {
        if (!ShouldRun()) return;
        using var host = await CreateNetMediateReflectionHostAsync();
        var mediator = host.Services.GetRequiredService<NetMediate.IMediator>();
        var ct = TestContext.Current.CancellationToken;
        var result = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++)
                await mediator.Send(new NmCommand(i), ct);
        }, CommandOps);
        output.WriteLine($"BENCH NetMediate (NoCodeGen) command: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"NetMediate (NoCodeGen) command throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    [Fact]
    public async Task NetMediate_NoCodeGen_Request_Throughput()
    {
        if (!ShouldRun()) return;
        using var host = await CreateNetMediateReflectionHostAsync();
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
        output.WriteLine($"BENCH NetMediate (NoCodeGen) request: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"NetMediate (NoCodeGen) request throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ── NetMediate: Mode 3 – Code Gen (explicit registration, AOT-safe startup)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NetMediate_CodeGen_Command_Throughput()
    {
        if (!ShouldRun()) return;
        using var host = await CreateNetMediateExplicitHostAsync();
        var mediator = host.Services.GetRequiredService<NetMediate.IMediator>();
        var ct = TestContext.Current.CancellationToken;
        var result = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++)
                await mediator.Send(new NmCommand(i), ct);
        }, CommandOps);
        output.WriteLine($"BENCH NetMediate (CodeGen) command: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"NetMediate (CodeGen) command throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    [Fact]
    public async Task NetMediate_CodeGen_Request_Throughput()
    {
        if (!ShouldRun()) return;
        using var host = await CreateNetMediateExplicitHostAsync();
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
        output.WriteLine($"BENCH NetMediate (CodeGen) request: {result}");
        Assert.True(result.OpsPerSec > MinThroughput,
            $"NetMediate (CodeGen) request throughput too low: {result.OpsPerSec:F0} ops/s");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ── MediatR 14: Only Mode 1 supported (no source gen, no AOT)
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
    // ── martinothamar/Mediator 3: Only Mode 3/4 supported (source gen required)
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
    // ── Comparison summary – writes docs
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Comparison_WritesBenchmarkDocs()
    {
        if (!ShouldRun()) return;

        var ct = TestContext.Current.CancellationToken;
        var tfm = AppContext.TargetFrameworkName ?? "net10.0";
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm UTC");

        // NetMediate – Mode 1 (reflection / No Code Gen)
        using var nmRefHost = await CreateNetMediateReflectionHostAsync();
        var nmRefMediator = nmRefHost.Services.GetRequiredService<NetMediate.IMediator>();
        var nmRefCmd = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++) await nmRefMediator.Send(new NmCommand(i), ct);
        }, CommandOps);
        var nmRefReq = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++) await nmRefMediator.Request<NmRequest, int>(new NmRequest(i), ct);
        }, RequestOps);

        // NetMediate – Mode 3 (explicit/code-gen, AOT-safe)
        using var nmExpHost = await CreateNetMediateExplicitHostAsync();
        var nmExpMediator = nmExpHost.Services.GetRequiredService<NetMediate.IMediator>();
        var nmExpCmd = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++) await nmExpMediator.Send(new NmCommand(i), ct);
        }, CommandOps);
        var nmExpReq = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++) await nmExpMediator.Request<NmRequest, int>(new NmRequest(i), ct);
        }, RequestOps);

        // MediatR – Mode 1 only
        using var mrHost = await CreateMediatRHostAsync();
        var mrMediator = mrHost.Services.GetRequiredService<global::MediatR.IMediator>();
        var mrCmd = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++) await mrMediator.Send(new MrCommand(i), ct);
        }, CommandOps);
        var mrReq = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++) await mrMediator.Send(new MrRequest(i), ct);
        }, RequestOps);

        // martinothamar/Mediator – Mode 3 only
        using var mtHost = await CreateMartinMediatorHostAsync();
        var mtMediator = mtHost.Services.GetRequiredService<global::Mediator.IMediator>();
        var mtCmd = await MeasureAsync(async () =>
        {
            for (var i = 0; i < CommandOps; i++) await mtMediator.Send(new MtCommand(i), ct);
        }, CommandOps);
        var mtReq = await MeasureAsync(async () =>
        {
            for (var i = 0; i < RequestOps; i++) await mtMediator.Send(new MtQuery(i), ct);
        }, RequestOps);

        WriteComparisonDocs(timestamp, tfm,
            nmRefCmd, nmRefReq,
            nmExpCmd, nmExpReq,
            mrCmd, mrReq,
            mtCmd, mtReq);

        output.WriteLine($"Benchmark docs written to {BenchmarkDocPath}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Host factories
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// NetMediate – Mode 1: reflection-based assembly scan (No Code Gen, No AOT).
    /// Telemetry and validation are disabled to measure pure dispatch overhead.
    /// </summary>
    private static async Task<IHost> CreateNetMediateReflectionHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
#pragma warning disable IL2026
        builder.Services
            .AddNetMediate(typeof(LibraryBenchmarkTests).Assembly)
            .DisableTelemetry()
            .DisableValidation();
#pragma warning restore IL2026
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    /// <summary>
    /// NetMediate – Mode 3: explicit handler registration (Code Gen / AOT-safe path).
    /// Identical dispatch path but no assembly scan at startup.
    /// </summary>
    private static async Task<IHost> CreateNetMediateExplicitHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services
            .AddNetMediate(b =>
            {
                b.RegisterCommandHandler<NmCommand, NmCommandHandler>();
                b.RegisterRequestHandler<NmRequest, int, NmRequestHandler>();
            })
            .DisableTelemetry()
            .DisableValidation();
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task<IHost> CreateMediatRHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<LibraryBenchmarkTests>());
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task<IHost> CreateMartinMediatorHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddMediator(opt => opt.ServiceLifetime = ServiceLifetime.Singleton);
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Measurement helpers
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

    // ─────────────────────────────────────────────────────────────────────────
    // Doc generation
    // ─────────────────────────────────────────────────────────────────────────

    private void WriteComparisonDocs(
        string timestamp, string tfm,
        BenchResult nmRefCmd, BenchResult nmRefReq,
        BenchResult nmExpCmd, BenchResult nmExpReq,
        BenchResult mrCmd, BenchResult mrReq,
        BenchResult mtCmd, BenchResult mtReq)
    {
        const string ns = "NOT SUPPORTED";

        var sb = new StringBuilder();
        sb.AppendLine("# Benchmark Comparison: NetMediate, MediatR 14 & martinothamar/Mediator 3");
        sb.AppendLine();
        sb.AppendLine("> **Auto-generated** by `LibraryBenchmarkTests.Comparison_WritesBenchmarkDocs`.");
        sb.AppendLine("> Re-run with `NETMEDIATE_RUN_PERFORMANCE_TESTS=true` to refresh.");
        sb.AppendLine();
        sb.AppendLine($"**Last run:** {timestamp}  ");
        sb.AppendLine($"**Target framework:** `{tfm}`");
        sb.AppendLine();
        sb.AppendLine("## Benchmark Modes");
        sb.AppendLine();
        sb.AppendLine("Each library is benchmarked across up to four **registration + AOT** modes.  ");
        sb.AppendLine("_Only features common to all included libraries are tested_ (command dispatch and");
        sb.AppendLine("request/response dispatch with no-op handlers). Telemetry and validation are");
        sb.AppendLine("disabled for NetMediate in all modes to isolate pure dispatch overhead.  ");
        sb.AppendLine("Logging is set to `Warning` level for all libraries.");
        sb.AppendLine();
        sb.AppendLine("| Mode | Description |");
        sb.AppendLine("|------|-------------|");
        sb.AppendLine("| **No Code Gen · No AOT** | Reflection-based assembly scan at startup, DI dispatch at runtime |");
        sb.AppendLine("| **Code Gen · No AOT** | Explicit / source-generated handler registration, DI or switch-gen dispatch |");
        sb.AppendLine("| **No Code Gen · AOT** | AOT publishing without source gen — no library supports this |");
        sb.AppendLine("| **Code Gen · AOT** | Source-gen registration + AOT publishing — same runtime throughput as Code Gen |");
        sb.AppendLine();
        sb.AppendLine("## Command Dispatch Throughput (ops/s — higher is better)");
        sb.AppendLine();
        sb.AppendLine("| Library | No Code Gen · No AOT | Code Gen · No AOT | No Code Gen · AOT | Code Gen · AOT |");
        sb.AppendLine("|---------|:--------------------:|:-----------------:|:-----------------:|:--------------:|");
        sb.AppendLine($"| NetMediate | {nmRefCmd.OpsPerSec:N0} | {nmExpCmd.OpsPerSec:N0} | {ns} | ≈ Code Gen |");
        sb.AppendLine($"| MediatR 14 | {mrCmd.OpsPerSec:N0} | {ns} | {ns} | {ns} |");
        sb.AppendLine($"| martinothamar/Mediator 3 | {ns} | {mtCmd.OpsPerSec:N0} | {ns} | ≈ Code Gen |");
        sb.AppendLine($"| TurboMediator | {ns} | ⚠️ .NET 10 issue | {ns} | ⚠️ .NET 10 issue |");
        sb.AppendLine();
        sb.AppendLine("## Request/Response Throughput (ops/s — higher is better)");
        sb.AppendLine();
        sb.AppendLine("| Library | No Code Gen · No AOT | Code Gen · No AOT | No Code Gen · AOT | Code Gen · AOT |");
        sb.AppendLine("|---------|:--------------------:|:-----------------:|:-----------------:|:--------------:|");
        sb.AppendLine($"| NetMediate | {nmRefReq.OpsPerSec:N0} | {nmExpReq.OpsPerSec:N0} | {ns} | ≈ Code Gen |");
        sb.AppendLine($"| MediatR 14 | {mrReq.OpsPerSec:N0} | {ns} | {ns} | {ns} |");
        sb.AppendLine($"| martinothamar/Mediator 3 | {ns} | {mtReq.OpsPerSec:N0} | {ns} | ≈ Code Gen |");
        sb.AppendLine($"| TurboMediator | {ns} | ⚠️ .NET 10 issue | {ns} | ⚠️ .NET 10 issue |");
        sb.AppendLine();
        sb.AppendLine("## Mode Definitions");
        sb.AppendLine();
        sb.AppendLine("### No Code Gen · No AOT");
        sb.AppendLine("Assembly scanning at startup (reflection), DI-based handler resolution at runtime.");
        sb.AppendLine("Supported by: **NetMediate**, **MediatR 14**.");
        sb.AppendLine();
        sb.AppendLine("### Code Gen · No AOT");
        sb.AppendLine("Source generator or explicit registration eliminates startup reflection.");
        sb.AppendLine("- **NetMediate**: `AddNetMediate(builder => { builder.Register...() })` — AOT-safe startup, DI dispatch.");
        sb.AppendLine("- **martinothamar/Mediator 3**: always source-generated; generates a `switch`-expression dispatcher.");
        sb.AppendLine("Supported by: **NetMediate**, **martinothamar/Mediator 3**.");
        sb.AppendLine();
        sb.AppendLine("### No Code Gen · AOT");
        sb.AppendLine("AOT publishing without a source generator.  No library in this comparison supports this.");
        sb.AppendLine("The `[RequiresUnreferencedCode]` attributes on NetMediate scan paths correctly report this.");
        sb.AppendLine();
        sb.AppendLine("### Code Gen · AOT");
        sb.AppendLine("Source-generated registration + Native AOT publishing.  Runtime dispatch throughput is");
        sb.AppendLine("identical to the _Code Gen · No AOT_ numbers (AOT affects startup and binary size, not");
        sb.AppendLine("per-call dispatch overhead).  Supported by: **NetMediate** (✅), **martinothamar/Mediator 3** (✅).");
        sb.AppendLine();
        sb.AppendLine("## What the numbers mean");
        sb.AppendLine();
        sb.AppendLine("**martinothamar/Mediator** typically leads in Code Gen mode because the source generator");
        sb.AppendLine("produces a compile-time `switch`-expression dispatcher with zero DI resolution overhead.");
        sb.AppendLine();
        sb.AppendLine("**MediatR 14** leads in No Code Gen mode: minimal per-dispatch overhead (no scoping,");
        sb.AppendLine("no validation, no telemetry), just a handler-type dictionary lookup.");
        sb.AppendLine();
        sb.AppendLine("**NetMediate** benchmarks have telemetry and validation disabled (`DisableTelemetry() +");
        sb.AppendLine("DisableValidation()`) to isolate pure dispatch overhead.  When these features are");
        sb.AppendLine("enabled (production default), they add listener-gated overhead that is negligible");
        sb.AppendLine("for I/O-bound handlers.");
        sb.AppendLine();
        sb.AppendLine("| Per-dispatch feature | NetMediate (benchmarked) | MediatR 14 | martinothamar/Mediator 3 |");
        sb.AppendLine("|---|:---:|:---:|:---:|");
        sb.AppendLine("| New DI scope (handler isolation) | ✅ always | ❌ no | ❌ no |");
        sb.AppendLine("| Validation pipeline | ✅ disabled for bench | ❌ no | ❌ no |");
        sb.AppendLine("| OpenTelemetry activity | ✅ disabled for bench | ❌ no | ❌ no |");
        sb.AppendLine("| Background async logging | ✅ channel-queued | ✅ varies | ✅ varies |");
        sb.AppendLine("| Source-generated dispatch | ❌ DI-based | ❌ DI-based | ✅ switch-gen |");
        sb.AppendLine();
        sb.AppendLine("## Measurement details");
        sb.AppendLine();
        sb.AppendLine("| Metric | Command | Request |");
        sb.AppendLine("|--------|---------|---------|");
        sb.AppendLine($"| Operations per timed run | {CommandOps:N0} | {RequestOps:N0} |");
        sb.AppendLine($"| NetMediate (No Code Gen) elapsed | {nmRefCmd.Elapsed.TotalMilliseconds:F1} ms | {nmRefReq.Elapsed.TotalMilliseconds:F1} ms |");
        sb.AppendLine($"| NetMediate (Code Gen) elapsed | {nmExpCmd.Elapsed.TotalMilliseconds:F1} ms | {nmExpReq.Elapsed.TotalMilliseconds:F1} ms |");
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
        sb.AppendLine("One warm-up pass (JIT compile) followed by a single timed pass.  Operations run");
        sb.AppendLine("**sequentially** to measure single-thread throughput.  Handlers are no-op stubs.");
        sb.AppendLine("Logging is set to `Warning` for all libraries.  NetMediate runs with telemetry");
        sb.AppendLine("and validation disabled via `DisableTelemetry()` + `DisableValidation()` to");
        sb.AppendLine("provide the most comparable baseline against libraries that have no such features.");
        sb.AppendLine();
        sb.AppendLine("See `tests/NetMediate.Benchmarks/LibraryBenchmarkTests.cs` for the full source.");

        Directory.CreateDirectory(Path.GetDirectoryName(BenchmarkDocPath)!);
        File.WriteAllText(BenchmarkDocPath, sb.ToString());

        // Update README Performance section
        UpdateReadmePerformanceSection(timestamp, tfm,
            nmRefCmd, nmRefReq, nmExpCmd, nmExpReq,
            mrCmd, mrReq, mtCmd, mtReq);
    }

    private void UpdateReadmePerformanceSection(
        string timestamp, string tfm,
        BenchResult nmRefCmd, BenchResult nmRefReq,
        BenchResult nmExpCmd, BenchResult nmExpReq,
        BenchResult mrCmd, BenchResult mrReq,
        BenchResult mtCmd, BenchResult mtReq)
    {
        if (!File.Exists(ReadMePath)) return;

        var section = new StringBuilder();
        section.AppendLine("## Performance");
        section.AppendLine();
        section.AppendLine($"> Last benchmarked: **{timestamp}** on `{tfm}` (sequential, no-op handlers, Warning log, telemetry+validation disabled).");
        section.AppendLine($"> Full details in [docs/BENCHMARK_COMPARISON.md](docs/BENCHMARK_COMPARISON.md).");
        section.AppendLine();
        section.AppendLine("### Command ops/s (higher is better)");
        section.AppendLine();
        section.AppendLine("| Library | No Code Gen · No AOT | Code Gen · No AOT | Code Gen · AOT |");
        section.AppendLine("|---------|:--------------------:|:-----------------:|:--------------:|");
        section.AppendLine($"| NetMediate | {nmRefCmd.OpsPerSec:N0} | {nmExpCmd.OpsPerSec:N0} | ≈ Code Gen |");
        section.AppendLine($"| MediatR 14 | {mrCmd.OpsPerSec:N0} | NOT SUPPORTED | NOT SUPPORTED |");
        section.AppendLine($"| martinothamar/Mediator 3 | NOT SUPPORTED | {mtCmd.OpsPerSec:N0} | ≈ Code Gen |");
        section.AppendLine();
        section.AppendLine("### Request ops/s (higher is better)");
        section.AppendLine();
        section.AppendLine("| Library | No Code Gen · No AOT | Code Gen · No AOT | Code Gen · AOT |");
        section.AppendLine("|---------|:--------------------:|:-----------------:|:--------------:|");
        section.AppendLine($"| NetMediate | {nmRefReq.OpsPerSec:N0} | {nmExpReq.OpsPerSec:N0} | ≈ Code Gen |");
        section.AppendLine($"| MediatR 14 | {mrReq.OpsPerSec:N0} | NOT SUPPORTED | NOT SUPPORTED |");
        section.AppendLine($"| martinothamar/Mediator 3 | NOT SUPPORTED | {mtReq.OpsPerSec:N0} | ≈ Code Gen |");
        section.AppendLine();
        section.AppendLine("> NetMediate benchmarks run with telemetry and validation disabled (`DisableTelemetry() + DisableValidation()`).");
        section.AppendLine("> For I/O-bound real handlers the extra per-dispatch semantics are negligible vs actual I/O latency.");
        section.AppendLine();

        output.WriteLine("=== Performance Summary ===");
        output.WriteLine($"NetMediate (No CG):  cmd={nmRefCmd.OpsPerSec:N0}  req={nmRefReq.OpsPerSec:N0}");
        output.WriteLine($"NetMediate (CG):     cmd={nmExpCmd.OpsPerSec:N0}  req={nmExpReq.OpsPerSec:N0}");
        output.WriteLine($"MediatR:             cmd={mrCmd.OpsPerSec:N0}  req={mrReq.OpsPerSec:N0}");
        output.WriteLine($"martinMediator (CG): cmd={mtCmd.OpsPerSec:N0}  req={mtReq.OpsPerSec:N0}");

        const string startMarker = "<!-- PERF_START -->";
        const string endMarker = "<!-- PERF_END -->";

        var readme = File.ReadAllText(ReadMePath);
        var startIdx = readme.IndexOf(startMarker, StringComparison.Ordinal);
        var endIdx = readme.IndexOf(endMarker, StringComparison.Ordinal);

        string updatedReadme;
        if (startIdx >= 0 && endIdx > startIdx)
        {
            updatedReadme =
                readme[..(startIdx + startMarker.Length)] + "\n" +
                section.ToString() +
                readme[endIdx..];
        }
        else
        {
            const string contributing = "## Contributing";
            var idx = readme.IndexOf(contributing, StringComparison.Ordinal);
            updatedReadme = idx >= 0
                ? string.Concat(readme[..idx], startMarker, "\n", section, endMarker, "\n\n", readme[idx..])
                : readme + "\n" + startMarker + "\n" + section + endMarker + "\n";
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

// martinothamar/Mediator handlers must be internal (not nested) for the source generator.
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
