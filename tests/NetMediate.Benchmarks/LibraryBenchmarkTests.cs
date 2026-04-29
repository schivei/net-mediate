using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MediatR;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetMediate.Benchmarks;

/// <summary>
/// Throughput benchmarks comparing NetMediate, MediatR 14, and martinothamar/Mediator 3
/// across four registration + AOT modes.
/// <para>
/// TurboMediator (v0.9.*) benchmarks live in the companion project
/// <c>tests/NetMediate.Benchmarks.TurboMediator</c> (targets net8.0 only) because
/// its source generator emits code that does not compile on net10.0.  Running that
/// project with <c>NETMEDIATE_RUN_PERFORMANCE_TESTS=true</c> writes
/// <c>docs/.turbo-bench-results.json</c>; this test reads that file and folds
/// TurboMediator into the same <c>BENCHMARK_COMPARISON.md</c> table.
/// </para>
/// <para>Gated by <c>NETMEDIATE_RUN_PERFORMANCE_TESTS=true</c>.</para>
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

    // Sidecar written by tests/NetMediate.Benchmarks.TurboMediator on net8.0
    private static readonly string TurboResultsPath =
        Path.Combine(SolutionRoot, "docs", ".turbo-bench-results.json");

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
    // ── NetMediate: Mode 3 – Code Gen (explicit registration, AOT-safe)
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
    // ── MediatR 14: reflection only (no source gen, no AOT)
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
    // ── martinothamar/Mediator 3: source gen required
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
    // ── Comparison summary – writes unified BENCHMARK_COMPARISON.md
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Comparison_WritesBenchmarkDocs()
    {
        if (!ShouldRun()) return;

        var ct = TestContext.Current.CancellationToken;
        var tfm = AppContext.TargetFrameworkName ?? "net10.0";
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm UTC");

        // ── NetMediate – reflection (No Code Gen) ────────────────────────────
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

        // ── NetMediate – explicit / Code Gen ────────────────────────────────
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

        // ── MediatR 14 ───────────────────────────────────────────────────────
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

        // ── martinothamar/Mediator 3 ─────────────────────────────────────────
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

        // ── TurboMediator – read sidecar JSON (written by the net8.0 project) ─
        var turbo = TryReadTurboResults();

        WriteComparisonDocs(timestamp, tfm,
            nmRefCmd, nmRefReq,
            nmExpCmd, nmExpReq,
            mrCmd, mrReq,
            mtCmd, mtReq,
            turbo);

        output.WriteLine($"Benchmark docs written to {BenchmarkDocPath}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Host factories
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>NetMediate – Mode 1: reflection scan (No Code Gen, No AOT).</summary>
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

    /// <summary>NetMediate – Mode 3: explicit registration (Code Gen / AOT-safe).</summary>
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
    // TurboMediator sidecar JSON reader
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to read TurboMediator benchmark results from the sidecar JSON file
    /// produced by <c>tests/NetMediate.Benchmarks.TurboMediator</c>.
    /// Returns <see langword="null"/> if the file does not exist or cannot be parsed.
    /// </summary>
    private static TurboBenchResults? TryReadTurboResults()
    {
        if (!File.Exists(TurboResultsPath))
            return null;

        try
        {
            var json = File.ReadAllText(TurboResultsPath);
            return JsonSerializer.Deserialize<TurboBenchResults>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
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
        BenchResult mtCmd, BenchResult mtReq,
        TurboBenchResults? turbo)
    {
        const string ns = "NOT SUPPORTED";

        // TurboMediator cells: real numbers when sidecar is available, placeholder otherwise.
        var tmCmdCell = turbo is not null
            ? $"{turbo.CommandOpsPerSec:N0} *(net8.0)*"
            : $"{ns} *(run `dotnet test` on `tests/NetMediate.Benchmarks.TurboMediator` to populate)*";
        var tmReqCell = turbo is not null
            ? $"{turbo.RequestOpsPerSec:N0} *(net8.0)*"
            : $"{ns} *(run `dotnet test` on `tests/NetMediate.Benchmarks.TurboMediator` to populate)*";
        var tmAotCell = turbo is not null
            ? $"≈ Code Gen *(net8.0)*"
            : $"{ns}";
        var tmNote = turbo is not null
            ? $"TurboMediator benchmarked on **{turbo.TargetFramework}** at {turbo.Timestamp}."
            : "TurboMediator results not yet available.  " +
              "Run `NETMEDIATE_RUN_PERFORMANCE_TESTS=true dotnet test tests/NetMediate.Benchmarks.TurboMediator/` " +
              "then re-run this test to populate the TurboMediator column.";

        var sb = new StringBuilder();
        sb.AppendLine("# Benchmark Comparison: NetMediate · MediatR 14 · martinothamar/Mediator 3 · TurboMediator");
        sb.AppendLine();
        sb.AppendLine("> **Auto-generated** by `LibraryBenchmarkTests.Comparison_WritesBenchmarkDocs`.");
        sb.AppendLine("> Re-run with `NETMEDIATE_RUN_PERFORMANCE_TESTS=true` to refresh.");
        sb.AppendLine();
        sb.AppendLine($"**Last run:** {timestamp}  ");
        sb.AppendLine($"**Target framework (main run):** `{tfm}`");
        sb.AppendLine();
        sb.AppendLine($"> {tmNote}");
        sb.AppendLine();
        sb.AppendLine("## Benchmark Modes");
        sb.AppendLine();
        sb.AppendLine("| Mode | Description |");
        sb.AppendLine("|------|-------------|");
        sb.AppendLine("| **No Code Gen · No AOT** | Reflection-based assembly scan at startup, DI dispatch at runtime |");
        sb.AppendLine("| **Code Gen · No AOT** | Explicit / source-generated handler registration, DI or switch-gen dispatch |");
        sb.AppendLine("| **No Code Gen · AOT** | AOT publishing without a source generator — no library supports this |");
        sb.AppendLine("| **Code Gen · AOT** | Source-gen registration + Native AOT publishing; same runtime throughput as Code Gen |");
        sb.AppendLine();
        sb.AppendLine("## Command Dispatch Throughput (ops/s — higher is better)");
        sb.AppendLine();
        sb.AppendLine("| Library | No Code Gen · No AOT | Code Gen · No AOT | No Code Gen · AOT | Code Gen · AOT |");
        sb.AppendLine("|---------|:--------------------:|:-----------------:|:-----------------:|:--------------:|");
        sb.AppendLine($"| **NetMediate** | {nmRefCmd.OpsPerSec:N0} | {nmExpCmd.OpsPerSec:N0} | {ns} | ≈ Code Gen |");
        sb.AppendLine($"| **MediatR 14** | {mrCmd.OpsPerSec:N0} | {ns} | {ns} | {ns} |");
        sb.AppendLine($"| **martinothamar/Mediator 3** | {ns} | {mtCmd.OpsPerSec:N0} | {ns} | ≈ Code Gen |");
        sb.AppendLine($"| **TurboMediator** | {ns} | {tmCmdCell} | {ns} | {tmAotCell} |");
        sb.AppendLine();
        sb.AppendLine("## Request/Response Throughput (ops/s — higher is better)");
        sb.AppendLine();
        sb.AppendLine("| Library | No Code Gen · No AOT | Code Gen · No AOT | No Code Gen · AOT | Code Gen · AOT |");
        sb.AppendLine("|---------|:--------------------:|:-----------------:|:-----------------:|:--------------:|");
        sb.AppendLine($"| **NetMediate** | {nmRefReq.OpsPerSec:N0} | {nmExpReq.OpsPerSec:N0} | {ns} | ≈ Code Gen |");
        sb.AppendLine($"| **MediatR 14** | {mrReq.OpsPerSec:N0} | {ns} | {ns} | {ns} |");
        sb.AppendLine($"| **martinothamar/Mediator 3** | {ns} | {mtReq.OpsPerSec:N0} | {ns} | ≈ Code Gen |");
        sb.AppendLine($"| **TurboMediator** | {ns} | {tmReqCell} | {ns} | {tmAotCell} |");
        sb.AppendLine();
        sb.AppendLine("## Mode Support Matrix");
        sb.AppendLine();
        sb.AppendLine("| Library | No Code Gen · No AOT | Code Gen · No AOT | No Code Gen · AOT | Code Gen · AOT |");
        sb.AppendLine("|---------|:--------------------:|:-----------------:|:-----------------:|:--------------:|");
        sb.AppendLine("| **NetMediate** | ✅ | ✅ | ❌ | ✅ |");
        sb.AppendLine("| **MediatR 14** | ✅ | ❌ | ❌ | ❌ |");
        sb.AppendLine("| **martinothamar/Mediator 3** | ❌ (source gen required) | ✅ | ❌ | ✅ |");
        sb.AppendLine("| **TurboMediator** | ❌ (source gen required) | ✅ *(net8.0)* | ❌ | ✅ *(net8.0)* |");
        sb.AppendLine();
        sb.AppendLine("## Per-dispatch Feature Comparison");
        sb.AppendLine();
        sb.AppendLine("| Feature | NetMediate *(benchmarked)* | MediatR 14 | martinothamar/Mediator 3 | TurboMediator |");
        sb.AppendLine("|---|:---:|:---:|:---:|:---:|");
        sb.AppendLine("| New DI scope per dispatch | ✅ always | ❌ no | ❌ no | ❌ no |");
        sb.AppendLine("| Validation pipeline | ✅ disabled for bench | ❌ no | ❌ no | ✅ optional |");
        sb.AppendLine("| OpenTelemetry activity | ✅ disabled for bench | ❌ no | ❌ no | ✅ optional package |");
        sb.AppendLine("| Background async logging | ✅ channel-queued | varies | varies | varies |");
        sb.AppendLine("| Source-generated switch dispatch | ❌ DI-based | ❌ DI-based | ✅ | ✅ |");
        sb.AppendLine("| .NET 10 compatible | ✅ | ✅ | ✅ | ⚠️ issue v0.9.3 |");
        sb.AppendLine("| netstandard2.0 support | ✅ | ❌ | ❌ | ❌ |");
        sb.AppendLine();
        sb.AppendLine("## Measurement Details");
        sb.AppendLine();
        sb.AppendLine("| Metric | Command | Request |");
        sb.AppendLine("|--------|---------|---------|");
        sb.AppendLine($"| Operations per timed pass | {CommandOps:N0} | {RequestOps:N0} |");
        sb.AppendLine($"| NetMediate (No Code Gen) elapsed | {nmRefCmd.Elapsed.TotalMilliseconds:F1} ms | {nmRefReq.Elapsed.TotalMilliseconds:F1} ms |");
        sb.AppendLine($"| NetMediate (Code Gen) elapsed | {nmExpCmd.Elapsed.TotalMilliseconds:F1} ms | {nmExpReq.Elapsed.TotalMilliseconds:F1} ms |");
        sb.AppendLine($"| MediatR 14 elapsed | {mrCmd.Elapsed.TotalMilliseconds:F1} ms | {mrReq.Elapsed.TotalMilliseconds:F1} ms |");
        sb.AppendLine($"| martinothamar/Mediator elapsed | {mtCmd.Elapsed.TotalMilliseconds:F1} ms | {mtReq.Elapsed.TotalMilliseconds:F1} ms |");

        if (turbo is not null)
        {
            sb.AppendLine($"| TurboMediator elapsed *(net8.0)* | {turbo.CommandElapsedMs:F1} ms | {turbo.RequestElapsedMs:F1} ms |");
        }
        else
        {
            sb.AppendLine("| TurboMediator elapsed | — (not yet benchmarked) | — |");
        }

        sb.AppendLine();
        sb.AppendLine("## Test Environment");
        sb.AppendLine();
        sb.AppendLine($"| | Main run (net10.0) | TurboMediator run (net8.0) |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine($"| OS | {System.Runtime.InteropServices.RuntimeInformation.OSDescription} | {turbo?.Os ?? "—"} |");
        sb.AppendLine($"| Processors | {Environment.ProcessorCount} | — |");
        sb.AppendLine($"| Runtime | {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} | {turbo?.Runtime ?? "—"} |");
        sb.AppendLine();
        sb.AppendLine("## Methodology");
        sb.AppendLine();
        sb.AppendLine("One warm-up pass (JIT) followed by a single timed sequential pass.");
        sb.AppendLine("No-op handlers.  Logging set to `Warning` for all libraries.");
        sb.AppendLine("NetMediate benchmarks run with `DisableTelemetry() + DisableValidation()` for a fair baseline.");
        sb.AppendLine("TurboMediator is benchmarked in a separate `net8.0` project due to a source-generator");
        sb.AppendLine("incompatibility with net10.0 (v0.9.3); results are merged via a JSON sidecar file.");
        sb.AppendLine();
        sb.AppendLine("See `tests/NetMediate.Benchmarks/LibraryBenchmarkTests.cs` and");
        sb.AppendLine("`tests/NetMediate.Benchmarks.TurboMediator/TurboMediatorBenchmarkTests.cs` for the full source.");

        Directory.CreateDirectory(Path.GetDirectoryName(BenchmarkDocPath)!);
        File.WriteAllText(BenchmarkDocPath, sb.ToString());

        UpdateReadmePerformanceSection(timestamp, tfm,
            nmRefCmd, nmRefReq, nmExpCmd, nmExpReq,
            mrCmd, mrReq, mtCmd, mtReq, turbo);
    }

    private void UpdateReadmePerformanceSection(
        string timestamp, string tfm,
        BenchResult nmRefCmd, BenchResult nmRefReq,
        BenchResult nmExpCmd, BenchResult nmExpReq,
        BenchResult mrCmd, BenchResult mrReq,
        BenchResult mtCmd, BenchResult mtReq,
        TurboBenchResults? turbo)
    {
        if (!File.Exists(ReadMePath)) return;

        const string ns = "NOT SUPPORTED";
        var tmCmd = turbo is not null ? $"{turbo.CommandOpsPerSec:N0} *(net8.0)*" : ns;
        var tmReq = turbo is not null ? $"{turbo.RequestOpsPerSec:N0} *(net8.0)*" : ns;

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
        section.AppendLine($"| MediatR 14 | {mrCmd.OpsPerSec:N0} | {ns} | {ns} |");
        section.AppendLine($"| martinothamar/Mediator 3 | {ns} | {mtCmd.OpsPerSec:N0} | ≈ Code Gen |");
        section.AppendLine($"| TurboMediator | {ns} | {tmCmd} | {(turbo is not null ? "≈ Code Gen *(net8.0)*" : ns)} |");
        section.AppendLine();
        section.AppendLine("### Request ops/s (higher is better)");
        section.AppendLine();
        section.AppendLine("| Library | No Code Gen · No AOT | Code Gen · No AOT | Code Gen · AOT |");
        section.AppendLine("|---------|:--------------------:|:-----------------:|:--------------:|");
        section.AppendLine($"| NetMediate | {nmRefReq.OpsPerSec:N0} | {nmExpReq.OpsPerSec:N0} | ≈ Code Gen |");
        section.AppendLine($"| MediatR 14 | {mrReq.OpsPerSec:N0} | {ns} | {ns} |");
        section.AppendLine($"| martinothamar/Mediator 3 | {ns} | {mtReq.OpsPerSec:N0} | ≈ Code Gen |");
        section.AppendLine($"| TurboMediator | {ns} | {tmReq} | {(turbo is not null ? "≈ Code Gen *(net8.0)*" : ns)} |");
        section.AppendLine();
        section.AppendLine("> NetMediate benchmarks run with telemetry and validation disabled.");
        section.AppendLine("> TurboMediator *(net8.0)* — source generator incompatible with net10.0 (v0.9.3).");
        section.AppendLine();

        output.WriteLine("=== Performance Summary ===");
        output.WriteLine($"NetMediate (No CG):  cmd={nmRefCmd.OpsPerSec:N0}  req={nmRefReq.OpsPerSec:N0}");
        output.WriteLine($"NetMediate (CG):     cmd={nmExpCmd.OpsPerSec:N0}  req={nmExpReq.OpsPerSec:N0}");
        output.WriteLine($"MediatR:             cmd={mrCmd.OpsPerSec:N0}  req={mrReq.OpsPerSec:N0}");
        output.WriteLine($"martinMediator (CG): cmd={mtCmd.OpsPerSec:N0}  req={mtReq.OpsPerSec:N0}");
        if (turbo is not null)
            output.WriteLine($"TurboMediator (net8): cmd={turbo.CommandOpsPerSec:N0}  req={turbo.RequestOpsPerSec:N0}");

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
    // Result types
    // ─────────────────────────────────────────────────────────────────────────

    private readonly record struct BenchResult(int Ops, TimeSpan Elapsed)
    {
        public double OpsPerSec => Ops / Elapsed.TotalSeconds;
        public override string ToString() =>
            $"{OpsPerSec:N0} ops/s ({Ops:N0} ops in {Elapsed.TotalMilliseconds:F1} ms)";
    }

    /// <summary>
    /// Mirrors <c>TurboBenchResults</c> from the TurboMediator benchmark project.
    /// Defined here so the main project has no project reference to TurboMediator.
    /// </summary>
    internal sealed record TurboBenchResults(
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
    // martinothamar/Mediator 3 message definitions
    // Must be accessible (public) so the source generator can build the switch-gen.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fire-and-forget command for martinothamar/Mediator benchmarks.</summary>
    public sealed record MtCommand(int Value) : global::Mediator.ICommand;

    /// <summary>Query (request/response) for martinothamar/Mediator benchmarks.</summary>
    public sealed record MtQuery(int Value) : global::Mediator.IQuery<int>;
}

// ─────────────────────────────────────────────────────────────────────────────
// martinothamar/Mediator handler types must be at namespace level (not nested)
// so the source generator discovers them.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class MtCommandHandler
    : global::Mediator.ICommandHandler<NetMediate.Benchmarks.LibraryBenchmarkTests.MtCommand>
{
    public ValueTask<global::Mediator.Unit> Handle(
        NetMediate.Benchmarks.LibraryBenchmarkTests.MtCommand command, CancellationToken ct) =>
        ValueTask.FromResult(global::Mediator.Unit.Value);
}

internal sealed class MtQueryHandler
    : global::Mediator.IQueryHandler<NetMediate.Benchmarks.LibraryBenchmarkTests.MtQuery, int>
{
    public ValueTask<int> Handle(
        NetMediate.Benchmarks.LibraryBenchmarkTests.MtQuery query, CancellationToken ct) =>
        ValueTask.FromResult(query.Value + 1);
}
