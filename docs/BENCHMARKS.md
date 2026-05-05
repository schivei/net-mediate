# NetMediate Benchmark Results

This document describes the performance characteristics of NetMediate under the current implementation, which uses **explicit handler registration only** (no assembly scanning) and **closed-type pipeline executors** registered at startup.

---

## Reference benchmark environment

Results in this document were produced by running `CoreDispatchThroughputTests` and `BenchmarkSystemInfoTests` in Release mode with `NETMEDIATE_RUN_PERFORMANCE_TESTS=true`. System info is always printed automatically when running any benchmark test class.

| Key | Value |
|---|---|
| OS | Ubuntu 24.04.4 LTS |
| Architecture | X64 |
| Logical CPUs | 4 |
| Total RAM | ~15,989 MB |
| .NET version | 10.0.5 |
| Execution mode | JIT (CoreCLR) |
| TFM | .NETCoreApp,Version=v10.0 |

To capture the specs for your own machine, run:

```bash
dotnet test tests/NetMediate.Tests/ --configuration Release \
  --filter "FullyQualifiedName~BenchmarkSystemInfo" \
  --logger "console;verbosity=detailed"
```

Output lines are prefixed with `SYSTEM_INFO`.

---

## Core dispatch throughput

Measured with `CoreDispatchThroughputTests` — no behaviors, no resilience, no adapters registered. Each test type uses its own unique message type so handler and behavior caches are never shared across tests. A warm-up phase runs before the timed window to prime DI resolution and the handler/behavior caches.

| Message type | Method | Operations | Elapsed (ms) | Throughput |
|---|---|---:|---:|---:|
| Command | `IMediator.Send<TMsg>` | 50,000 | 40.13 | **~1,246,000 msgs/s** |
| Notification | `IMediator.Notify<TMsg>` | 50,000 | 33.36 | **~1,499,000 msgs/s** |
| Request | `IMediator.Request<TMsg, TResp>` | 50,000 | 26.49 | **~1,888,000 msgs/s** |
| Stream | `IMediator.RequestStream<TMsg, TResp>` | 10,000 | 38.51 | **~260,000 invocations/s** ¹ |

¹ Stream throughput is measured as complete stream invocations per second, not individual items. Each invocation yields 3 items, giving ~780,000 total items/s.

> **Note on stream vs other types:** Stream invocations are inherently more expensive because each call allocates a new `IAsyncEnumerator<T>` and drives it through multiple `MoveNextAsync` cycles with `Task.Yield()` inside the handler. The per-invocation cost is higher by design.

---

## BenchmarkDotNet project

For artifact-reproducible, statistically rigorous benchmarks including allocation data and GC gen0/1/2 counts, use the dedicated `NetMediate.Benchmarks` project:

```bash
# Standard JIT run (produces BenchmarkDotNet HTML/CSV artifacts in BenchmarkDotNet.Artifacts/)
dotnet run -c Release --project tests/NetMediate.Benchmarks/

# Quick dry-run to verify benchmark classes compile and can execute (no statistical warming)
dotnet run -c Release --project tests/NetMediate.Benchmarks/ -- --job Dry

# NativeAOT comparison — publish a native binary then run it
dotnet publish tests/NetMediate.Benchmarks/ -c Release -p:AotBenchmark=true -o /tmp/bench-aot
/tmp/bench-aot/NetMediate.Benchmarks
```

`CoreDispatchBenchmarks` covers the four core message types:

| Benchmark | Description |
|---|---|
| `Command Send` | `IMediator.Send<BenchCommand>()` — no pipeline behaviors |
| `Notification Notify` | `IMediator.Notify<BenchNotification>()` — no pipeline behaviors |
| `Request Request` | `IMediator.Request<BenchRequest, BenchResponse>()` — no pipeline behaviors |
| `Stream RequestStream (3 items/call)` | `IMediator.RequestStream<BenchStreamRequest, BenchStreamItem>()` — drains 3 items per invocation |

BenchmarkDotNet output columns: `Method`, `Mean`, `Error`, `StdDev`, `Gen0`, `Allocated`.  The `--job Short` flag adds a short statistical run (3 warmup + 3 measured iterations) alongside the default full job.

---



### Hot-path throughput

Once warm, **JIT and NativeAOT produce identical throughput**. The handler cache (`ConcurrentDictionary<Type, Lazy<T[]>>`) and behavior cache eliminate DI resolution on the hot path. NativeAOT has no advantage or disadvantage in per-message throughput.

| Aspect | JIT (CoreCLR) | NativeAOT |
|---|---|---|
| Warm throughput | Baseline | Same ¹ |
| Cold-start (first dispatch) | JIT compiles on first call | Pre-compiled binary; no JIT overhead |
| Startup overhead | None (explicit registration only) | None |
| Binary size | Standard | Larger (trimmed single-file) |
| Compatible registration | All | Explicit registration + source generator only |

¹ Identical because the hot path makes no reflection, no `MakeGenericType`, and no dynamic IL calls — all resolved types are closed generics fixed at compile time.

### How to run the comparison

**JIT (standard `dotnet test`):**

```bash
NETMEDIATE_RUN_PERFORMANCE_TESTS=true \
dotnet test tests/NetMediate.Tests/ --configuration Release \
  --filter "FullyQualifiedName~CoreDispatchThroughput OR FullyQualifiedName~BenchmarkSystemInfo" \
  --logger "console;verbosity=detailed"
```

**NativeAOT (publish then run the native binary):**

```bash
# 1. Publish NativeAOT test host
dotnet publish tests/NetMediate.Tests/ \
  --configuration Release \
  -p:PublishAot=true \
  -p:TrimmerRootAssembly=NetMediate.Tests \
  --output /tmp/nativeaot-bench

# 2. Run the native binary with the performance flag
NETMEDIATE_RUN_PERFORMANCE_TESTS=true \
/tmp/nativeaot-bench/NetMediate.Tests \
  --filter "CoreDispatchThroughput|BenchmarkSystemInfo"
```

Look for `execution_mode=jit` vs `execution_mode=nativeaot` in the output to confirm which runtime produced each result line.

### Trimming without NativeAOT

Publishing with `--self-contained -p:PublishTrimmed=true` reduces binary size but does **not** change dispatch throughput. The caches and closed-type registration model are trimmer-safe by design.

---

## Implementation model

All handlers are registered explicitly via `IMediatorServiceBuilder` methods or the source generator:

```csharp
builder.Services.UseNetMediate(configure =>
{
    configure.RegisterCommandHandler<MyCommandHandler, MyCommand>();
    configure.RegisterRequestHandler<MyRequestHandler, MyRequest, MyResponse>();
    configure.RegisterNotificationHandler<MyNotificationHandler, MyNotification>();
    configure.RegisterStreamHandler<MyStreamHandler, MyStream, MyItem>();
});

// Or via source generator (identical registrations, generated at compile time)
builder.Services.AddNetMediate();
```

At startup each `Register*Handler<>` call performs two `TryAddSingleton<>` / `TryAddTransient<>` registrations:

| Handler kind | Executor registered |
|---|---|
| `RegisterCommandHandler<THandler, TMsg>` | `PipelineExecutor<TMsg, Task, ICommandHandler<TMsg>>` |
| `RegisterNotificationHandler<THandler, TMsg>` | `NotificationPipelineExecutor<TMsg>` |
| `RegisterRequestHandler<THandler, TMsg, TResp>` | `RequestPipelineExecutor<TMsg, TResp>` |
| `RegisterStreamHandler<THandler, TMsg, TResp>` | `StreamPipelineExecutor<TMsg, TResp>` |

No `MakeGenericType`, no `typeof(TResult) switch`, no assembly scanning — fully NativeAOT-compatible.

---

## Dispatch semantics

| Operation | Method | Semantics |
|---|---|---|
| `Send` | `IMediator.Send<TMsg>` | All `ICommandHandler<TMsg>` instances iterated sequentially |
| `Request` | `IMediator.Request<TMsg, TResp>` | Single `IRequestHandler<TMsg, TResp>` (first registered) |
| `Notify` | `IMediator.Notify<TMsg>` | Fire-and-forget per handler; all `INotificationHandler<TMsg>` instances started individually; exceptions logged |
| `RequestStream` | `IMediator.RequestStream<TMsg, TResp>` | Single `IStreamHandler<TMsg, TResp>`; yields items lazily |

---

## Pipeline behavior resolution

Behaviors are registered via `RegisterBehavior<TBehavior, TMessage, TResult>()` — closed types only. The resolved behavior arrays are cached per message-result type in the same `ConcurrentDictionary<Type, Lazy<T[]>>` as handlers, so no DI enumeration occurs on the hot path after the first dispatch of a given message type.

### Command pipeline (`PipelineExecutor<TMsg, Task, ICommandHandler<TMsg>>`)

Resolves `IPipelineBehavior<TMsg, Task>` — two-parameter closed-type lookup, cached.

### Notification pipeline (`NotificationPipelineExecutor<TMsg>`)

Resolves both, then concatenates:
1. `IPipelineBehavior<TMsg, Task>` — two-parameter closed-type lookup, cached
2. `IPipelineBehavior<TMsg>` — one-parameter closed-type lookup, cached (notification-specific behaviors)

No runtime type switches — the two-lookup pattern is fixed at compile time inside the executor.

### Request pipeline (`RequestPipelineExecutor<TMsg, TResp>`)

Resolves both, then concatenates:
1. `IPipelineBehavior<TMsg, Task<TResp>>` — two-parameter closed-type lookup, cached
2. `IPipelineRequestBehavior<TMsg, TResp>` — closed-type shorthand lookup, cached

### Stream pipeline (`StreamPipelineExecutor<TMsg, TResp>`)

Resolves both, then concatenates:
1. `IPipelineBehavior<TMsg, IAsyncEnumerable<TResp>>` — two-parameter closed-type lookup, cached
2. `IPipelineStreamBehavior<TMsg, TResp>` — closed-type shorthand lookup, cached

---

## Handler and behavior caches

Resolved handler arrays are cached permanently per service type using a global `ConcurrentDictionary<Type, Lazy<T[]>>` (`s_handlerCache`). Handlers are registered as Singletons, so their resolved arrays never change for the lifetime of the application — a single global cache is correct.

Resolved behavior arrays use a **per-service-provider** cache: a `ConditionalWeakTable<IServiceProvider, ConcurrentDictionary<Type, Lazy<T[]>>>` (`s_behaviorCacheByProvider`). Each DI container gets its own isolated behavior dictionary, preventing cache contamination between containers (e.g., different test suites or multi-tenant hosts). When the provider is garbage-collected its cache entry is automatically released — no memory leak.

```
First call for TMsg in a given provider  →  DI resolution + cache fill  →  O(n) one-time cost
All subsequent calls                     →  cache read                  →  O(1)
```

---

## How to reproduce benchmarks

### Core dispatch throughput (per message type)

```bash
NETMEDIATE_RUN_PERFORMANCE_TESTS=true \
dotnet test tests/NetMediate.Tests/ --configuration Release \
  --filter "FullyQualifiedName~CoreDispatchThroughput OR FullyQualifiedName~BenchmarkSystemInfo" \
  --logger "console;verbosity=detailed"
```

Output lines of interest:

```
SYSTEM_INFO execution_mode=<jit|nativeaot>
SYSTEM_INFO logical_cpus=<n>
SYSTEM_INFO total_ram_mb=<mb>
CORE_THROUGHPUT <type> tfm=<tfm> execution_mode=<mode> ops=<n> elapsed_ms=<ms> msgs_per_second=<n>
LOAD_RESULT <scenario> tfm=<tfm> execution_mode=<mode> ops=<n> elapsed_ms=<ms> throughput_ops_s=<n>
```

### Full benchmark suite

```bash
NETMEDIATE_RUN_PERFORMANCE_TESTS=true \
dotnet test tests/NetMediate.Tests/ --configuration Release \
  --filter "FullyQualifiedName~LoadPerformance OR FullyQualifiedName~PipelineVariants OR FullyQualifiedName~ExplicitRegistration OR FullyQualifiedName~CoreDispatchThroughput OR FullyQualifiedName~BenchmarkSystemInfo" \
  --logger "console;verbosity=detailed"
```

---

## Minimum CI assertions

| Test class | Scenario | Threshold |
|---|---|---:|
| `CoreDispatchThroughputTests` | `core_command` | `> 500 msgs/s` |
| `CoreDispatchThroughputTests` | `core_notification` | `> 500 msgs/s` |
| `CoreDispatchThroughputTests` | `core_request` | `> 500 msgs/s` |
| `CoreDispatchThroughputTests` | `core_stream` | `> 500 msgs/s` |
| `LoadPerformanceTests` | all | `> 500 ops/s` |
| `CoreExplicitRegistrationLoadTests` | all | `> 500 ops/s` |
| `ResilienceLoadPerformanceTests` | `resilience_request_parallel` | `≥ 30,000 ops/s` |
| `FullStackLoadPerformanceTests` | `fullstack_request_parallel` | `≥ 20,000 ops/s` |
| `PipelineVariantsLoadTests` | all | `> 500 ops/s` |

Thresholds are deliberately lenient to remain green on any CI hardware. Local developer machines and production servers typically produce 10–100× higher throughput than the minimum assertion.

---

## See Also

- [RESILIENCE.md](RESILIENCE.md) — resilience package guide
- [AOT.md](AOT.md) — AOT/NativeAOT compatibility guide
- [SOURCE_GENERATION.md](SOURCE_GENERATION.md) — source generator guide

---

## Latest CI Benchmark Run

Run: 2026-05-05 10:57 UTC | Branch: copilot/implementar-long-term | Commit: 924aa9e

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error     | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|----------:|---------:|-------:|----------:|
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  67.11 ns |  0.110 ns | 0.092 ns | 0.0018 |      32 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 115.40 ns |  0.645 ns | 0.572 ns | 0.0162 |     272 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  73.33 ns |  0.150 ns | 0.140 ns | 0.0061 |     104 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 160.34 ns |  0.780 ns | 0.692 ns | 0.0117 |     200 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  65.76 ns |  4.394 ns | 0.241 ns | 0.0018 |      32 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 114.07 ns | 36.764 ns | 2.015 ns | 0.0162 |     272 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  76.09 ns |  1.542 ns | 0.085 ns | 0.0061 |     104 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 163.29 ns |  3.897 ns | 0.214 ns | 0.0117 |     200 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.91 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 12.58 sec and exited with 0
// ***** Done, took 00:00:14 (14.55 sec)   *****
// Found 8 benchmarks:
//   CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
//   CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
//   CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
//   CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
//   CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
//   CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
//   CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
//   CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

// **************************
// Benchmark: CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Command --job RunStrategy=Throughput --benchmarkId 0 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 315775.00 ns, 315.7750 ns/op
WorkloadJitting  1: 1000 op, 1045814.00 ns, 1.0458 us/op

OverheadJitting  2: 16000 op, 198859.00 ns, 12.4287 ns/op
WorkloadJitting  2: 16000 op, 6760961.00 ns, 422.5601 ns/op

WorkloadPilot    1: 16000 op, 5145311.00 ns, 321.5819 ns/op
WorkloadPilot    2: 32000 op, 9559412.00 ns, 298.7316 ns/op
WorkloadPilot    3: 64000 op, 18861036.00 ns, 294.7037 ns/op
WorkloadPilot    4: 128000 op, 43008296.00 ns, 336.0023 ns/op
WorkloadPilot    5: 256000 op, 76631090.00 ns, 299.3402 ns/op
WorkloadPilot    6: 512000 op, 35755574.00 ns, 69.8351 ns/op
WorkloadPilot    7: 1024000 op, 68993489.00 ns, 67.3765 ns/op
WorkloadPilot    8: 2048000 op, 137910595.00 ns, 67.3392 ns/op
WorkloadPilot    9: 4096000 op, 275949581.00 ns, 67.3705 ns/op
WorkloadPilot   10: 8192000 op, 550673118.00 ns, 67.2208 ns/op

OverheadWarmup   1: 8192000 op, 33020.00 ns, 0.0040 ns/op
OverheadWarmup   2: 8192000 op, 21322.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 21352.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 21202.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 28203.00 ns, 0.0034 ns/op
OverheadWarmup   6: 8192000 op, 28853.00 ns, 0.0035 ns/op
OverheadWarmup   7: 8192000 op, 28903.00 ns, 0.0035 ns/op
OverheadWarmup   8: 8192000 op, 28533.00 ns, 0.0035 ns/op

OverheadActual   1: 8192000 op, 31598.00 ns, 0.0039 ns/op
OverheadActual   2: 8192000 op, 25258.00 ns, 0.0031 ns/op
OverheadActual   3: 8192000 op, 48994.00 ns, 0.0060 ns/op
OverheadActual   4: 8192000 op, 26179.00 ns, 0.0032 ns/op
OverheadActual   5: 8192000 op, 26930.00 ns, 0.0033 ns/op
OverheadActual   6: 8192000 op, 26850.00 ns, 0.0033 ns/op
OverheadActual   7: 8192000 op, 26710.00 ns, 0.0033 ns/op
OverheadActual   8: 8192000 op, 26520.00 ns, 0.0032 ns/op
OverheadActual   9: 8192000 op, 31968.00 ns, 0.0039 ns/op
OverheadActual  10: 8192000 op, 30747.00 ns, 0.0038 ns/op
OverheadActual  11: 8192000 op, 28383.00 ns, 0.0035 ns/op
OverheadActual  12: 8192000 op, 33981.00 ns, 0.0041 ns/op
OverheadActual  13: 8192000 op, 31166.00 ns, 0.0038 ns/op
OverheadActual  14: 8192000 op, 31057.00 ns, 0.0038 ns/op
OverheadActual  15: 8192000 op, 25899.00 ns, 0.0032 ns/op
OverheadActual  16: 8192000 op, 25970.00 ns, 0.0032 ns/op
OverheadActual  17: 8192000 op, 47982.00 ns, 0.0059 ns/op
OverheadActual  18: 8192000 op, 28483.00 ns, 0.0035 ns/op
OverheadActual  19: 8192000 op, 25909.00 ns, 0.0032 ns/op
OverheadActual  20: 8192000 op, 25969.00 ns, 0.0032 ns/op

WorkloadWarmup   1: 8192000 op, 561168469.00 ns, 68.5020 ns/op
WorkloadWarmup   2: 8192000 op, 558873195.00 ns, 68.2218 ns/op
WorkloadWarmup   3: 8192000 op, 553328508.00 ns, 67.5450 ns/op
WorkloadWarmup   4: 8192000 op, 550257089.00 ns, 67.1701 ns/op
WorkloadWarmup   5: 8192000 op, 550100735.00 ns, 67.1510 ns/op
WorkloadWarmup   6: 8192000 op, 551833309.00 ns, 67.3625 ns/op
WorkloadWarmup   7: 8192000 op, 549219239.00 ns, 67.0434 ns/op
WorkloadWarmup   8: 8192000 op, 550379082.00 ns, 67.1849 ns/op
WorkloadWarmup   9: 8192000 op, 551238936.00 ns, 67.2899 ns/op
WorkloadWarmup  10: 8192000 op, 549489474.00 ns, 67.0764 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 554606640.00 ns, 67.7010 ns/op
WorkloadActual   2: 8192000 op, 548733938.00 ns, 66.9841 ns/op
WorkloadActual   3: 8192000 op, 548804595.00 ns, 66.9927 ns/op
WorkloadActual   4: 8192000 op, 550210630.00 ns, 67.1644 ns/op
WorkloadActual   5: 8192000 op, 550448287.00 ns, 67.1934 ns/op
WorkloadActual   6: 8192000 op, 556477473.00 ns, 67.9294 ns/op
WorkloadActual   7: 8192000 op, 550002029.00 ns, 67.1389 ns/op
WorkloadActual   8: 8192000 op, 550371461.00 ns, 67.1840 ns/op
WorkloadActual   9: 8192000 op, 550689068.00 ns, 67.2228 ns/op
WorkloadActual  10: 8192000 op, 551124894.00 ns, 67.2760 ns/op
WorkloadActual  11: 8192000 op, 549342773.00 ns, 67.0584 ns/op
WorkloadActual  12: 8192000 op, 549365848.00 ns, 67.0613 ns/op
WorkloadActual  13: 8192000 op, 549177955.00 ns, 67.0383 ns/op
WorkloadActual  14: 8192000 op, 550019651.00 ns, 67.1411 ns/op
WorkloadActual  15: 8192000 op, 549239198.00 ns, 67.0458 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 548706281.50 ns, 66.9807 ns/op
WorkloadResult   2: 8192000 op, 548776938.50 ns, 66.9894 ns/op
WorkloadResult   3: 8192000 op, 550182973.50 ns, 67.1610 ns/op
WorkloadResult   4: 8192000 op, 550420630.50 ns, 67.1900 ns/op
WorkloadResult   5: 8192000 op, 549974372.50 ns, 67.1355 ns/op
WorkloadResult   6: 8192000 op, 550343804.50 ns, 67.1806 ns/op
WorkloadResult   7: 8192000 op, 550661411.50 ns, 67.2194 ns/op
WorkloadResult   8: 8192000 op, 551097237.50 ns, 67.2726 ns/op
WorkloadResult   9: 8192000 op, 549315116.50 ns, 67.0551 ns/op
WorkloadResult  10: 8192000 op, 549338191.50 ns, 67.0579 ns/op
WorkloadResult  11: 8192000 op, 549150298.50 ns, 67.0349 ns/op
WorkloadResult  12: 8192000 op, 549991994.50 ns, 67.1377 ns/op
WorkloadResult  13: 8192000 op, 549211541.50 ns, 67.0424 ns/op
// GC:  15 0 0 262144000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4637 has exited with code 0.

Mean = 67.112 ns, StdErr = 0.025 ns (0.04%), N = 13, StdDev = 0.092 ns
Min = 66.981 ns, Q1 = 67.042 ns, Median = 67.136 ns, Q3 = 67.181 ns, Max = 67.273 ns
IQR = 0.138 ns, LowerFence = 66.835 ns, UpperFence = 67.388 ns
ConfidenceInterval = [67.002 ns; 67.222 ns] (CI 99.9%), Margin = 0.110 ns (0.16% of Mean)
Skewness = 0.11, Kurtosis = 1.59, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-05 10:57 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Notification --job RunStrategy=Throughput --benchmarkId 1 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 203476.00 ns, 203.4760 ns/op
WorkloadJitting  1: 1000 op, 1289429.00 ns, 1.2894 us/op

OverheadJitting  2: 16000 op, 205489.00 ns, 12.8431 ns/op
WorkloadJitting  2: 16000 op, 12182253.00 ns, 761.3908 ns/op

WorkloadPilot    1: 16000 op, 10190888.00 ns, 636.9305 ns/op
WorkloadPilot    2: 32000 op, 18150970.00 ns, 567.2178 ns/op
WorkloadPilot    3: 64000 op, 31577404.00 ns, 493.3969 ns/op
WorkloadPilot    4: 128000 op, 57996541.00 ns, 453.0980 ns/op
WorkloadPilot    5: 256000 op, 60386756.00 ns, 235.8858 ns/op
WorkloadPilot    6: 512000 op, 58891797.00 ns, 115.0230 ns/op
WorkloadPilot    7: 1024000 op, 117796334.00 ns, 115.0355 ns/op
WorkloadPilot    8: 2048000 op, 236178080.00 ns, 115.3213 ns/op
WorkloadPilot    9: 4096000 op, 475648421.00 ns, 116.1251 ns/op
WorkloadPilot   10: 8192000 op, 943610411.00 ns, 115.1868 ns/op

OverheadWarmup   1: 8192000 op, 23305.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 26770.00 ns, 0.0033 ns/op
OverheadWarmup   3: 8192000 op, 26229.00 ns, 0.0032 ns/op
OverheadWarmup   4: 8192000 op, 26881.00 ns, 0.0033 ns/op
OverheadWarmup   5: 8192000 op, 26370.00 ns, 0.0032 ns/op

OverheadActual   1: 8192000 op, 26600.00 ns, 0.0032 ns/op
OverheadActual   2: 8192000 op, 26680.00 ns, 0.0033 ns/op
OverheadActual   3: 8192000 op, 23806.00 ns, 0.0029 ns/op
OverheadActual   4: 8192000 op, 22284.00 ns, 0.0027 ns/op
OverheadActual   5: 8192000 op, 18407.00 ns, 0.0022 ns/op
OverheadActual   6: 8192000 op, 18307.00 ns, 0.0022 ns/op
OverheadActual   7: 8192000 op, 18308.00 ns, 0.0022 ns/op
OverheadActual   8: 8192000 op, 18277.00 ns, 0.0022 ns/op
OverheadActual   9: 8192000 op, 18268.00 ns, 0.0022 ns/op
OverheadActual  10: 8192000 op, 18418.00 ns, 0.0022 ns/op
OverheadActual  11: 8192000 op, 18278.00 ns, 0.0022 ns/op
OverheadActual  12: 8192000 op, 21693.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 18368.00 ns, 0.0022 ns/op
OverheadActual  14: 8192000 op, 18398.00 ns, 0.0022 ns/op
OverheadActual  15: 8192000 op, 18678.00 ns, 0.0023 ns/op
OverheadActual  16: 8192000 op, 42114.00 ns, 0.0051 ns/op
OverheadActual  17: 8192000 op, 18447.00 ns, 0.0023 ns/op
OverheadActual  18: 8192000 op, 18498.00 ns, 0.0023 ns/op
OverheadActual  19: 8192000 op, 18468.00 ns, 0.0023 ns/op
OverheadActual  20: 8192000 op, 21682.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 952216986.00 ns, 116.2374 ns/op
WorkloadWarmup   2: 8192000 op, 952691024.00 ns, 116.2953 ns/op
WorkloadWarmup   3: 8192000 op, 945541810.00 ns, 115.4226 ns/op
WorkloadWarmup   4: 8192000 op, 948953312.00 ns, 115.8390 ns/op
WorkloadWarmup   5: 8192000 op, 948642254.00 ns, 115.8011 ns/op
WorkloadWarmup   6: 8192000 op, 940829782.00 ns, 114.8474 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 962169914.00 ns, 117.4524 ns/op
WorkloadActual   2: 8192000 op, 942814859.00 ns, 115.0897 ns/op
WorkloadActual   3: 8192000 op, 945494137.00 ns, 115.4168 ns/op
WorkloadActual   4: 8192000 op, 944957373.00 ns, 115.3512 ns/op
WorkloadActual   5: 8192000 op, 945595162.00 ns, 115.4291 ns/op
WorkloadActual   6: 8192000 op, 943958711.00 ns, 115.2293 ns/op
WorkloadActual   7: 8192000 op, 938648952.00 ns, 114.5812 ns/op
WorkloadActual   8: 8192000 op, 936934242.00 ns, 114.3719 ns/op
WorkloadActual   9: 8192000 op, 942863222.00 ns, 115.0956 ns/op
WorkloadActual  10: 8192000 op, 945838188.00 ns, 115.4588 ns/op
WorkloadActual  11: 8192000 op, 944671413.00 ns, 115.3163 ns/op
WorkloadActual  12: 8192000 op, 949791262.00 ns, 115.9413 ns/op
WorkloadActual  13: 8192000 op, 955089941.00 ns, 116.5881 ns/op
WorkloadActual  14: 8192000 op, 951202775.00 ns, 116.1136 ns/op
WorkloadActual  15: 8192000 op, 947692325.00 ns, 115.6851 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 942796401.50 ns, 115.0875 ns/op
WorkloadResult   2: 8192000 op, 945475679.50 ns, 115.4145 ns/op
WorkloadResult   3: 8192000 op, 944938915.50 ns, 115.3490 ns/op
WorkloadResult   4: 8192000 op, 945576704.50 ns, 115.4268 ns/op
WorkloadResult   5: 8192000 op, 943940253.50 ns, 115.2271 ns/op
WorkloadResult   6: 8192000 op, 938630494.50 ns, 114.5789 ns/op
WorkloadResult   7: 8192000 op, 936915784.50 ns, 114.3696 ns/op
WorkloadResult   8: 8192000 op, 942844764.50 ns, 115.0934 ns/op
WorkloadResult   9: 8192000 op, 945819730.50 ns, 115.4565 ns/op
WorkloadResult  10: 8192000 op, 944652955.50 ns, 115.3141 ns/op
WorkloadResult  11: 8192000 op, 949772804.50 ns, 115.9391 ns/op
WorkloadResult  12: 8192000 op, 955071483.50 ns, 116.5859 ns/op
WorkloadResult  13: 8192000 op, 951184317.50 ns, 116.1114 ns/op
WorkloadResult  14: 8192000 op, 947673867.50 ns, 115.6828 ns/op
// GC:  133 0 0 2228224000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4649 has exited with code 0.

Mean = 115.403 ns, StdErr = 0.153 ns (0.13%), N = 14, StdDev = 0.572 ns
Min = 114.370 ns, Q1 = 115.127 ns, Median = 115.382 ns, Q3 = 115.626 ns, Max = 116.586 ns
IQR = 0.499 ns, LowerFence = 114.378 ns, UpperFence = 116.375 ns
ConfidenceInterval = [114.757 ns; 116.048 ns] (CI 99.9%), Margin = 0.645 ns (0.56% of Mean)
Skewness = 0.18, Kurtosis = 2.61, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-05 10:57 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Request --job RunStrategy=Throughput --benchmarkId 2 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 228944.00 ns, 228.9440 ns/op
WorkloadJitting  1: 1000 op, 1154987.00 ns, 1.1550 us/op

OverheadJitting  2: 16000 op, 201052.00 ns, 12.5658 ns/op
WorkloadJitting  2: 16000 op, 9326177.00 ns, 582.8861 ns/op

WorkloadPilot    1: 16000 op, 8055977.00 ns, 503.4986 ns/op
WorkloadPilot    2: 32000 op, 14070203.00 ns, 439.6938 ns/op
WorkloadPilot    3: 64000 op, 28124713.00 ns, 439.4486 ns/op
WorkloadPilot    4: 128000 op, 55588061.00 ns, 434.2817 ns/op
WorkloadPilot    5: 256000 op, 52065122.00 ns, 203.3794 ns/op
WorkloadPilot    6: 512000 op, 37609389.00 ns, 73.4558 ns/op
WorkloadPilot    7: 1024000 op, 74792757.00 ns, 73.0398 ns/op
WorkloadPilot    8: 2048000 op, 149632814.00 ns, 73.0629 ns/op
WorkloadPilot    9: 4096000 op, 299844048.00 ns, 73.2041 ns/op
WorkloadPilot   10: 8192000 op, 601696798.00 ns, 73.4493 ns/op

OverheadWarmup   1: 8192000 op, 24427.00 ns, 0.0030 ns/op
OverheadWarmup   2: 8192000 op, 26810.00 ns, 0.0033 ns/op
OverheadWarmup   3: 8192000 op, 26871.00 ns, 0.0033 ns/op
OverheadWarmup   4: 8192000 op, 25388.00 ns, 0.0031 ns/op
OverheadWarmup   5: 8192000 op, 25899.00 ns, 0.0032 ns/op
OverheadWarmup   6: 8192000 op, 25469.00 ns, 0.0031 ns/op

OverheadActual   1: 8192000 op, 26159.00 ns, 0.0032 ns/op
OverheadActual   2: 8192000 op, 26610.00 ns, 0.0032 ns/op
OverheadActual   3: 8192000 op, 33250.00 ns, 0.0041 ns/op
OverheadActual   4: 8192000 op, 31387.00 ns, 0.0038 ns/op
OverheadActual   5: 8192000 op, 25268.00 ns, 0.0031 ns/op
OverheadActual   6: 8192000 op, 25558.00 ns, 0.0031 ns/op
OverheadActual   7: 8192000 op, 26740.00 ns, 0.0033 ns/op
OverheadActual   8: 8192000 op, 26310.00 ns, 0.0032 ns/op
OverheadActual   9: 8192000 op, 26240.00 ns, 0.0032 ns/op
OverheadActual  10: 8192000 op, 26519.00 ns, 0.0032 ns/op
OverheadActual  11: 8192000 op, 31036.00 ns, 0.0038 ns/op
OverheadActual  12: 8192000 op, 28463.00 ns, 0.0035 ns/op
OverheadActual  13: 8192000 op, 25839.00 ns, 0.0032 ns/op
OverheadActual  14: 8192000 op, 24997.00 ns, 0.0031 ns/op
OverheadActual  15: 8192000 op, 25268.00 ns, 0.0031 ns/op

WorkloadWarmup   1: 8192000 op, 612001749.00 ns, 74.7072 ns/op
WorkloadWarmup   2: 8192000 op, 612898358.00 ns, 74.8167 ns/op
WorkloadWarmup   3: 8192000 op, 606285951.00 ns, 74.0095 ns/op
WorkloadWarmup   4: 8192000 op, 599909327.00 ns, 73.2311 ns/op
WorkloadWarmup   5: 8192000 op, 595525453.00 ns, 72.6960 ns/op
WorkloadWarmup   6: 8192000 op, 597728185.00 ns, 72.9649 ns/op
WorkloadWarmup   7: 8192000 op, 600237891.00 ns, 73.2712 ns/op
WorkloadWarmup   8: 8192000 op, 599400321.00 ns, 73.1690 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 601510324.00 ns, 73.4266 ns/op
WorkloadActual   2: 8192000 op, 602438761.00 ns, 73.5399 ns/op
WorkloadActual   3: 8192000 op, 601789194.00 ns, 73.4606 ns/op
WorkloadActual   4: 8192000 op, 601203773.00 ns, 73.3891 ns/op
WorkloadActual   5: 8192000 op, 601534761.00 ns, 73.4295 ns/op
WorkloadActual   6: 8192000 op, 601540400.00 ns, 73.4302 ns/op
WorkloadActual   7: 8192000 op, 600405032.00 ns, 73.2916 ns/op
WorkloadActual   8: 8192000 op, 597900254.00 ns, 72.9859 ns/op
WorkloadActual   9: 8192000 op, 599451557.00 ns, 73.1752 ns/op
WorkloadActual  10: 8192000 op, 601312605.00 ns, 73.4024 ns/op
WorkloadActual  11: 8192000 op, 599711868.00 ns, 73.2070 ns/op
WorkloadActual  12: 8192000 op, 599787050.00 ns, 73.2162 ns/op
WorkloadActual  13: 8192000 op, 600632752.00 ns, 73.3194 ns/op
WorkloadActual  14: 8192000 op, 600263627.00 ns, 73.2744 ns/op
WorkloadActual  15: 8192000 op, 601096240.00 ns, 73.3760 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 601484014.00 ns, 73.4233 ns/op
WorkloadResult   2: 8192000 op, 602412451.00 ns, 73.5367 ns/op
WorkloadResult   3: 8192000 op, 601762884.00 ns, 73.4574 ns/op
WorkloadResult   4: 8192000 op, 601177463.00 ns, 73.3859 ns/op
WorkloadResult   5: 8192000 op, 601508451.00 ns, 73.4263 ns/op
WorkloadResult   6: 8192000 op, 601514090.00 ns, 73.4270 ns/op
WorkloadResult   7: 8192000 op, 600378722.00 ns, 73.2884 ns/op
WorkloadResult   8: 8192000 op, 597873944.00 ns, 72.9827 ns/op
WorkloadResult   9: 8192000 op, 599425247.00 ns, 73.1720 ns/op
WorkloadResult  10: 8192000 op, 601286295.00 ns, 73.3992 ns/op
WorkloadResult  11: 8192000 op, 599685558.00 ns, 73.2038 ns/op
WorkloadResult  12: 8192000 op, 599760740.00 ns, 73.2130 ns/op
WorkloadResult  13: 8192000 op, 600606442.00 ns, 73.3162 ns/op
WorkloadResult  14: 8192000 op, 600237317.00 ns, 73.2712 ns/op
WorkloadResult  15: 8192000 op, 601069930.00 ns, 73.3728 ns/op
// GC:  50 0 0 851968000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4666 has exited with code 0.

Mean = 73.325 ns, StdErr = 0.036 ns (0.05%), N = 15, StdDev = 0.140 ns
Min = 72.983 ns, Q1 = 73.242 ns, Median = 73.373 ns, Q3 = 73.425 ns, Max = 73.537 ns
IQR = 0.183 ns, LowerFence = 72.968 ns, UpperFence = 73.699 ns
ConfidenceInterval = [73.175 ns; 73.475 ns] (CI 99.9%), Margin = 0.150 ns (0.20% of Mean)
Skewness = -0.76, Kurtosis = 2.97, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-05 10:57 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Stream --job RunStrategy=Throughput --benchmarkId 3 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 235985.00 ns, 235.9850 ns/op
WorkloadJitting  1: 1000 op, 1622580.00 ns, 1.6226 us/op

OverheadJitting  2: 16000 op, 234082.00 ns, 14.6301 ns/op
WorkloadJitting  2: 16000 op, 16970174.00 ns, 1.0606 us/op

WorkloadPilot    1: 16000 op, 18906057.00 ns, 1.1816 us/op
WorkloadPilot    2: 32000 op, 26388271.00 ns, 824.6335 ns/op
WorkloadPilot    3: 64000 op, 52414757.00 ns, 818.9806 ns/op
WorkloadPilot    4: 128000 op, 95470115.00 ns, 745.8603 ns/op
WorkloadPilot    5: 256000 op, 80979831.00 ns, 316.3275 ns/op
WorkloadPilot    6: 512000 op, 82143551.00 ns, 160.4366 ns/op
WorkloadPilot    7: 1024000 op, 162055054.00 ns, 158.2569 ns/op
WorkloadPilot    8: 2048000 op, 325988033.00 ns, 159.1738 ns/op
WorkloadPilot    9: 4096000 op, 653698869.00 ns, 159.5945 ns/op

OverheadWarmup   1: 4096000 op, 12789.00 ns, 0.0031 ns/op
OverheadWarmup   2: 4096000 op, 9475.00 ns, 0.0023 ns/op
OverheadWarmup   3: 4096000 op, 9435.00 ns, 0.0023 ns/op
OverheadWarmup   4: 4096000 op, 9454.00 ns, 0.0023 ns/op
OverheadWarmup   5: 4096000 op, 9525.00 ns, 0.0023 ns/op
OverheadWarmup   6: 4096000 op, 9434.00 ns, 0.0023 ns/op
OverheadWarmup   7: 4096000 op, 9475.00 ns, 0.0023 ns/op
OverheadWarmup   8: 4096000 op, 9635.00 ns, 0.0024 ns/op
OverheadWarmup   9: 4096000 op, 9475.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9614.00 ns, 0.0023 ns/op
OverheadActual   2: 4096000 op, 9595.00 ns, 0.0023 ns/op
OverheadActual   3: 4096000 op, 9644.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9524.00 ns, 0.0023 ns/op
OverheadActual   5: 4096000 op, 9474.00 ns, 0.0023 ns/op
OverheadActual   6: 4096000 op, 9455.00 ns, 0.0023 ns/op
OverheadActual   7: 4096000 op, 13571.00 ns, 0.0033 ns/op
OverheadActual   8: 4096000 op, 12238.00 ns, 0.0030 ns/op
OverheadActual   9: 4096000 op, 9454.00 ns, 0.0023 ns/op
OverheadActual  10: 4096000 op, 9485.00 ns, 0.0023 ns/op
OverheadActual  11: 4096000 op, 9484.00 ns, 0.0023 ns/op
OverheadActual  12: 4096000 op, 9454.00 ns, 0.0023 ns/op
OverheadActual  13: 4096000 op, 13611.00 ns, 0.0033 ns/op
OverheadActual  14: 4096000 op, 9435.00 ns, 0.0023 ns/op
OverheadActual  15: 4096000 op, 9444.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 4096000 op, 666640533.00 ns, 162.7540 ns/op
WorkloadWarmup   2: 4096000 op, 663359839.00 ns, 161.9531 ns/op
WorkloadWarmup   3: 4096000 op, 658818338.00 ns, 160.8443 ns/op
WorkloadWarmup   4: 4096000 op, 652991857.00 ns, 159.4218 ns/op
WorkloadWarmup   5: 4096000 op, 649723363.00 ns, 158.6239 ns/op
WorkloadWarmup   6: 4096000 op, 648380684.00 ns, 158.2961 ns/op
WorkloadWarmup   7: 4096000 op, 651017559.00 ns, 158.9398 ns/op
WorkloadWarmup   8: 4096000 op, 648935198.00 ns, 158.4314 ns/op
WorkloadWarmup   9: 4096000 op, 648896950.00 ns, 158.4221 ns/op
WorkloadWarmup  10: 4096000 op, 649628891.00 ns, 158.6008 ns/op
WorkloadWarmup  11: 4096000 op, 649846839.00 ns, 158.6540 ns/op
WorkloadWarmup  12: 4096000 op, 649108108.00 ns, 158.4737 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 657584152.00 ns, 160.5430 ns/op
WorkloadActual   2: 4096000 op, 655125745.00 ns, 159.9428 ns/op
WorkloadActual   3: 4096000 op, 656651187.00 ns, 160.3152 ns/op
WorkloadActual   4: 4096000 op, 670213620.00 ns, 163.6264 ns/op
WorkloadActual   5: 4096000 op, 656791485.00 ns, 160.3495 ns/op
WorkloadActual   6: 4096000 op, 658969300.00 ns, 160.8812 ns/op
WorkloadActual   7: 4096000 op, 656032394.00 ns, 160.1642 ns/op
WorkloadActual   8: 4096000 op, 651339065.00 ns, 159.0183 ns/op
WorkloadActual   9: 4096000 op, 655533283.00 ns, 160.0423 ns/op
WorkloadActual  10: 4096000 op, 660956118.00 ns, 161.3662 ns/op
WorkloadActual  11: 4096000 op, 660722266.00 ns, 161.3091 ns/op
WorkloadActual  12: 4096000 op, 659653620.00 ns, 161.0482 ns/op
WorkloadActual  13: 4096000 op, 654858569.00 ns, 159.8776 ns/op
WorkloadActual  14: 4096000 op, 652492830.00 ns, 159.3000 ns/op
WorkloadActual  15: 4096000 op, 657870912.00 ns, 160.6130 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 657574667.00 ns, 160.5407 ns/op
WorkloadResult   2: 4096000 op, 655116260.00 ns, 159.9405 ns/op
WorkloadResult   3: 4096000 op, 656641702.00 ns, 160.3129 ns/op
WorkloadResult   4: 4096000 op, 656782000.00 ns, 160.3472 ns/op
WorkloadResult   5: 4096000 op, 658959815.00 ns, 160.8789 ns/op
WorkloadResult   6: 4096000 op, 656022909.00 ns, 160.1618 ns/op
WorkloadResult   7: 4096000 op, 651329580.00 ns, 159.0160 ns/op
WorkloadResult   8: 4096000 op, 655523798.00 ns, 160.0400 ns/op
WorkloadResult   9: 4096000 op, 660946633.00 ns, 161.3639 ns/op
WorkloadResult  10: 4096000 op, 660712781.00 ns, 161.3068 ns/op
WorkloadResult  11: 4096000 op, 659644135.00 ns, 161.0459 ns/op
WorkloadResult  12: 4096000 op, 654849084.00 ns, 159.8753 ns/op
WorkloadResult  13: 4096000 op, 652483345.00 ns, 159.2977 ns/op
WorkloadResult  14: 4096000 op, 657861427.00 ns, 160.6107 ns/op
// GC:  48 0 0 819200032 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4680 has exited with code 0.

Mean = 160.338 ns, StdErr = 0.185 ns (0.12%), N = 14, StdDev = 0.692 ns
Min = 159.016 ns, Q1 = 159.965 ns, Median = 160.330 ns, Q3 = 160.812 ns, Max = 161.364 ns
IQR = 0.846 ns, LowerFence = 158.696 ns, UpperFence = 162.082 ns
ConfidenceInterval = [159.558 ns; 161.119 ns] (CI 99.9%), Margin = 0.780 ns (0.49% of Mean)
Skewness = -0.24, Kurtosis = 2.07, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-05 10:57 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Command --job ShortRun --benchmarkId 4 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 207963.00 ns, 207.9630 ns/op
WorkloadJitting  1: 1000 op, 1005602.00 ns, 1.0056 us/op

OverheadJitting  2: 16000 op, 206380.00 ns, 12.8988 ns/op
WorkloadJitting  2: 16000 op, 6586636.00 ns, 411.6648 ns/op

WorkloadPilot    1: 16000 op, 5268713.00 ns, 329.2946 ns/op
WorkloadPilot    2: 32000 op, 9626227.00 ns, 300.8196 ns/op
WorkloadPilot    3: 64000 op, 23543698.00 ns, 367.8703 ns/op
WorkloadPilot    4: 128000 op, 39184831.00 ns, 306.1315 ns/op
WorkloadPilot    5: 256000 op, 70438123.00 ns, 275.1489 ns/op
WorkloadPilot    6: 512000 op, 37509662.00 ns, 73.2611 ns/op
WorkloadPilot    7: 1024000 op, 67999466.00 ns, 66.4057 ns/op
WorkloadPilot    8: 2048000 op, 135231976.00 ns, 66.0312 ns/op
WorkloadPilot    9: 4096000 op, 269890641.00 ns, 65.8913 ns/op
WorkloadPilot   10: 8192000 op, 539696616.00 ns, 65.8809 ns/op

OverheadWarmup   1: 8192000 op, 25939.00 ns, 0.0032 ns/op
OverheadWarmup   2: 8192000 op, 21092.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 31918.00 ns, 0.0039 ns/op
OverheadWarmup   4: 8192000 op, 30766.00 ns, 0.0038 ns/op
OverheadWarmup   5: 8192000 op, 31418.00 ns, 0.0038 ns/op
OverheadWarmup   6: 8192000 op, 31537.00 ns, 0.0038 ns/op
OverheadWarmup   7: 8192000 op, 30886.00 ns, 0.0038 ns/op

OverheadActual   1: 8192000 op, 31867.00 ns, 0.0039 ns/op
OverheadActual   2: 8192000 op, 35503.00 ns, 0.0043 ns/op
OverheadActual   3: 8192000 op, 47051.00 ns, 0.0057 ns/op
OverheadActual   4: 8192000 op, 28323.00 ns, 0.0035 ns/op
OverheadActual   5: 8192000 op, 28613.00 ns, 0.0035 ns/op
OverheadActual   6: 8192000 op, 28583.00 ns, 0.0035 ns/op
OverheadActual   7: 8192000 op, 27682.00 ns, 0.0034 ns/op
OverheadActual   8: 8192000 op, 28212.00 ns, 0.0034 ns/op
OverheadActual   9: 8192000 op, 28422.00 ns, 0.0035 ns/op
OverheadActual  10: 8192000 op, 32378.00 ns, 0.0040 ns/op
OverheadActual  11: 8192000 op, 31348.00 ns, 0.0038 ns/op
OverheadActual  12: 8192000 op, 28352.00 ns, 0.0035 ns/op
OverheadActual  13: 8192000 op, 29104.00 ns, 0.0036 ns/op
OverheadActual  14: 8192000 op, 28322.00 ns, 0.0035 ns/op
OverheadActual  15: 8192000 op, 28513.00 ns, 0.0035 ns/op
OverheadActual  16: 8192000 op, 26390.00 ns, 0.0032 ns/op
OverheadActual  17: 8192000 op, 28303.00 ns, 0.0035 ns/op
OverheadActual  18: 8192000 op, 33410.00 ns, 0.0041 ns/op
OverheadActual  19: 8192000 op, 28273.00 ns, 0.0035 ns/op
OverheadActual  20: 8192000 op, 28302.00 ns, 0.0035 ns/op

WorkloadWarmup   1: 8192000 op, 550696108.00 ns, 67.2236 ns/op
WorkloadWarmup   2: 8192000 op, 546945998.00 ns, 66.7659 ns/op
WorkloadWarmup   3: 8192000 op, 539358286.00 ns, 65.8396 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 541036020.00 ns, 66.0444 ns/op
WorkloadActual   2: 8192000 op, 537517868.00 ns, 65.6150 ns/op
WorkloadActual   3: 8192000 op, 537729888.00 ns, 65.6409 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 541007552.50 ns, 66.0410 ns/op
WorkloadResult   2: 8192000 op, 537489400.50 ns, 65.6115 ns/op
WorkloadResult   3: 8192000 op, 537701420.50 ns, 65.6374 ns/op
// GC:  15 0 0 262144000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4695 has exited with code 0.

Mean = 65.763 ns, StdErr = 0.139 ns (0.21%), N = 3, StdDev = 0.241 ns
Min = 65.611 ns, Q1 = 65.624 ns, Median = 65.637 ns, Q3 = 65.839 ns, Max = 66.041 ns
IQR = 0.215 ns, LowerFence = 65.302 ns, UpperFence = 66.161 ns
ConfidenceInterval = [61.370 ns; 70.157 ns] (CI 99.9%), Margin = 4.394 ns (6.68% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-05 10:57 (0h 0m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Notification --job ShortRun --benchmarkId 5 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 198038.00 ns, 198.0380 ns/op
WorkloadJitting  1: 1000 op, 1245023.00 ns, 1.2450 us/op

OverheadJitting  2: 16000 op, 211729.00 ns, 13.2331 ns/op
WorkloadJitting  2: 16000 op, 11745915.00 ns, 734.1197 ns/op

WorkloadPilot    1: 16000 op, 9754851.00 ns, 609.6782 ns/op
WorkloadPilot    2: 32000 op, 17684896.00 ns, 552.6530 ns/op
WorkloadPilot    3: 64000 op, 30813250.00 ns, 481.4570 ns/op
WorkloadPilot    4: 128000 op, 53720100.00 ns, 419.6883 ns/op
WorkloadPilot    5: 256000 op, 63391214.00 ns, 247.6219 ns/op
WorkloadPilot    6: 512000 op, 59451760.00 ns, 116.1167 ns/op
WorkloadPilot    7: 1024000 op, 119161788.00 ns, 116.3689 ns/op
WorkloadPilot    8: 2048000 op, 235322194.00 ns, 114.9034 ns/op
WorkloadPilot    9: 4096000 op, 480065355.00 ns, 117.2035 ns/op
WorkloadPilot   10: 8192000 op, 935366665.00 ns, 114.1805 ns/op

OverheadWarmup   1: 8192000 op, 26340.00 ns, 0.0032 ns/op
OverheadWarmup   2: 8192000 op, 39209.00 ns, 0.0048 ns/op
OverheadWarmup   3: 8192000 op, 20971.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 20931.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 20992.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 20922.00 ns, 0.0026 ns/op

OverheadActual   1: 8192000 op, 21092.00 ns, 0.0026 ns/op
OverheadActual   2: 8192000 op, 21052.00 ns, 0.0026 ns/op
OverheadActual   3: 8192000 op, 24848.00 ns, 0.0030 ns/op
OverheadActual   4: 8192000 op, 21002.00 ns, 0.0026 ns/op
OverheadActual   5: 8192000 op, 21182.00 ns, 0.0026 ns/op
OverheadActual   6: 8192000 op, 21001.00 ns, 0.0026 ns/op
OverheadActual   7: 8192000 op, 21072.00 ns, 0.0026 ns/op
OverheadActual   8: 8192000 op, 20952.00 ns, 0.0026 ns/op
OverheadActual   9: 8192000 op, 20911.00 ns, 0.0026 ns/op
OverheadActual  10: 8192000 op, 20972.00 ns, 0.0026 ns/op
OverheadActual  11: 8192000 op, 24376.00 ns, 0.0030 ns/op
OverheadActual  12: 8192000 op, 20952.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 20981.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 20992.00 ns, 0.0026 ns/op
OverheadActual  15: 8192000 op, 20941.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 965217565.00 ns, 117.8244 ns/op
WorkloadWarmup   2: 8192000 op, 952462868.00 ns, 116.2674 ns/op
WorkloadWarmup   3: 8192000 op, 939314808.00 ns, 114.6625 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 953482721.00 ns, 116.3919 ns/op
WorkloadActual   2: 8192000 op, 924077118.00 ns, 112.8024 ns/op
WorkloadActual   3: 8192000 op, 925779117.00 ns, 113.0101 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 953461720.00 ns, 116.3894 ns/op
WorkloadResult   2: 8192000 op, 924056117.00 ns, 112.7998 ns/op
WorkloadResult   3: 8192000 op, 925758116.00 ns, 113.0076 ns/op
// GC:  133 0 0 2228224000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4706 has exited with code 0.

Mean = 114.066 ns, StdErr = 1.163 ns (1.02%), N = 3, StdDev = 2.015 ns
Min = 112.800 ns, Q1 = 112.904 ns, Median = 113.008 ns, Q3 = 114.698 ns, Max = 116.389 ns
IQR = 1.795 ns, LowerFence = 110.212 ns, UpperFence = 117.391 ns
ConfidenceInterval = [77.302 ns; 150.829 ns] (CI 99.9%), Margin = 36.764 ns (32.23% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-05 10:57 (0h 0m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Request --job ShortRun --benchmarkId 6 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 270217.00 ns, 270.2170 ns/op
WorkloadJitting  1: 1000 op, 1088046.00 ns, 1.0880 us/op

OverheadJitting  2: 16000 op, 210146.00 ns, 13.1341 ns/op
WorkloadJitting  2: 16000 op, 9144189.00 ns, 571.5118 ns/op

WorkloadPilot    1: 16000 op, 7858876.00 ns, 491.1798 ns/op
WorkloadPilot    2: 32000 op, 13778657.00 ns, 430.5830 ns/op
WorkloadPilot    3: 64000 op, 27757876.00 ns, 433.7168 ns/op
WorkloadPilot    4: 128000 op, 52959098.00 ns, 413.7430 ns/op
WorkloadPilot    5: 256000 op, 70695986.00 ns, 276.1562 ns/op
WorkloadPilot    6: 512000 op, 38871345.00 ns, 75.9206 ns/op
WorkloadPilot    7: 1024000 op, 76792663.00 ns, 74.9928 ns/op
WorkloadPilot    8: 2048000 op, 154254441.00 ns, 75.3196 ns/op
WorkloadPilot    9: 4096000 op, 309604900.00 ns, 75.5871 ns/op
WorkloadPilot   10: 8192000 op, 616884807.00 ns, 75.3033 ns/op

OverheadWarmup   1: 8192000 op, 26060.00 ns, 0.0032 ns/op
OverheadWarmup   2: 8192000 op, 21211.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 21191.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 21142.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 21222.00 ns, 0.0026 ns/op
OverheadWarmup   7: 8192000 op, 21182.00 ns, 0.0026 ns/op
OverheadWarmup   8: 8192000 op, 21242.00 ns, 0.0026 ns/op
OverheadWarmup   9: 8192000 op, 24888.00 ns, 0.0030 ns/op
OverheadWarmup  10: 8192000 op, 24066.00 ns, 0.0029 ns/op

OverheadActual   1: 8192000 op, 21252.00 ns, 0.0026 ns/op
OverheadActual   2: 8192000 op, 21312.00 ns, 0.0026 ns/op
OverheadActual   3: 8192000 op, 21342.00 ns, 0.0026 ns/op
OverheadActual   4: 8192000 op, 21242.00 ns, 0.0026 ns/op
OverheadActual   5: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadActual   6: 8192000 op, 21272.00 ns, 0.0026 ns/op
OverheadActual   7: 8192000 op, 24947.00 ns, 0.0030 ns/op
OverheadActual   8: 8192000 op, 24106.00 ns, 0.0029 ns/op
OverheadActual   9: 8192000 op, 24106.00 ns, 0.0029 ns/op
OverheadActual  10: 8192000 op, 24037.00 ns, 0.0029 ns/op
OverheadActual  11: 8192000 op, 24166.00 ns, 0.0029 ns/op
OverheadActual  12: 8192000 op, 24106.00 ns, 0.0029 ns/op
OverheadActual  13: 8192000 op, 21192.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 24226.00 ns, 0.0030 ns/op
OverheadActual  15: 8192000 op, 24647.00 ns, 0.0030 ns/op
OverheadActual  16: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadActual  17: 8192000 op, 21312.00 ns, 0.0026 ns/op
OverheadActual  18: 8192000 op, 37286.00 ns, 0.0046 ns/op
OverheadActual  19: 8192000 op, 21222.00 ns, 0.0026 ns/op
OverheadActual  20: 8192000 op, 21222.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 652041958.00 ns, 79.5950 ns/op
WorkloadWarmup   2: 8192000 op, 625382836.00 ns, 76.3407 ns/op
WorkloadWarmup   3: 8192000 op, 619530175.00 ns, 75.6262 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 622589536.00 ns, 75.9997 ns/op
WorkloadActual   2: 8192000 op, 623962621.00 ns, 76.1673 ns/op
WorkloadActual   3: 8192000 op, 623430231.00 ns, 76.1023 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 622568209.00 ns, 75.9971 ns/op
WorkloadResult   2: 8192000 op, 623941294.00 ns, 76.1647 ns/op
WorkloadResult   3: 8192000 op, 623408904.00 ns, 76.0997 ns/op
// GC:  50 0 0 851968000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4716 has exited with code 0.

Mean = 76.087 ns, StdErr = 0.049 ns (0.06%), N = 3, StdDev = 0.085 ns
Min = 75.997 ns, Q1 = 76.048 ns, Median = 76.100 ns, Q3 = 76.132 ns, Max = 76.165 ns
IQR = 0.084 ns, LowerFence = 75.923 ns, UpperFence = 76.258 ns
ConfidenceInterval = [74.545 ns; 77.629 ns] (CI 99.9%), Margin = 1.542 ns (2.03% of Mean)
Skewness = -0.15, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-05 10:57 (0h 0m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Stream --job ShortRun --benchmarkId 7 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 203506.00 ns, 203.5060 ns/op
WorkloadJitting  1: 1000 op, 1502540.00 ns, 1.5025 us/op

OverheadJitting  2: 16000 op, 204197.00 ns, 12.7623 ns/op
WorkloadJitting  2: 16000 op, 16525552.00 ns, 1.0328 us/op

WorkloadPilot    1: 16000 op, 14625485.00 ns, 914.0928 ns/op
WorkloadPilot    2: 32000 op, 26444349.00 ns, 826.3859 ns/op
WorkloadPilot    3: 64000 op, 52673784.00 ns, 823.0279 ns/op
WorkloadPilot    4: 128000 op, 64840182.00 ns, 506.5639 ns/op
WorkloadPilot    5: 256000 op, 47546054.00 ns, 185.7268 ns/op
WorkloadPilot    6: 512000 op, 83460554.00 ns, 163.0089 ns/op
WorkloadPilot    7: 1024000 op, 221401921.00 ns, 216.2128 ns/op
WorkloadPilot    8: 2048000 op, 364233761.00 ns, 177.8485 ns/op
WorkloadPilot    9: 4096000 op, 672885064.00 ns, 164.2786 ns/op

OverheadWarmup   1: 4096000 op, 14431.00 ns, 0.0035 ns/op
OverheadWarmup   2: 4096000 op, 12268.00 ns, 0.0030 ns/op
OverheadWarmup   3: 4096000 op, 12158.00 ns, 0.0030 ns/op
OverheadWarmup   4: 4096000 op, 12188.00 ns, 0.0030 ns/op
OverheadWarmup   5: 4096000 op, 12218.00 ns, 0.0030 ns/op
OverheadWarmup   6: 4096000 op, 12218.00 ns, 0.0030 ns/op
OverheadWarmup   7: 4096000 op, 12168.00 ns, 0.0030 ns/op
OverheadWarmup   8: 4096000 op, 12188.00 ns, 0.0030 ns/op

OverheadActual   1: 4096000 op, 10797.00 ns, 0.0026 ns/op
OverheadActual   2: 4096000 op, 10806.00 ns, 0.0026 ns/op
OverheadActual   3: 4096000 op, 10786.00 ns, 0.0026 ns/op
OverheadActual   4: 4096000 op, 10786.00 ns, 0.0026 ns/op
OverheadActual   5: 4096000 op, 10716.00 ns, 0.0026 ns/op
OverheadActual   6: 4096000 op, 10746.00 ns, 0.0026 ns/op
OverheadActual   7: 4096000 op, 10716.00 ns, 0.0026 ns/op
OverheadActual   8: 4096000 op, 10706.00 ns, 0.0026 ns/op
OverheadActual   9: 4096000 op, 12940.00 ns, 0.0032 ns/op
OverheadActual  10: 4096000 op, 10717.00 ns, 0.0026 ns/op
OverheadActual  11: 4096000 op, 10746.00 ns, 0.0026 ns/op
OverheadActual  12: 4096000 op, 10836.00 ns, 0.0026 ns/op
OverheadActual  13: 4096000 op, 10777.00 ns, 0.0026 ns/op
OverheadActual  14: 4096000 op, 10706.00 ns, 0.0026 ns/op
OverheadActual  15: 4096000 op, 10787.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 4096000 op, 680901088.00 ns, 166.2356 ns/op
WorkloadWarmup   2: 4096000 op, 673244427.00 ns, 164.3663 ns/op
WorkloadWarmup   3: 4096000 op, 665375167.00 ns, 162.4451 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 669025154.00 ns, 163.3362 ns/op
WorkloadActual   2: 4096000 op, 669628662.00 ns, 163.4836 ns/op
WorkloadActual   3: 4096000 op, 667904570.00 ns, 163.0626 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 669014377.00 ns, 163.3336 ns/op
WorkloadResult   2: 4096000 op, 669617885.00 ns, 163.4809 ns/op
WorkloadResult   3: 4096000 op, 667893793.00 ns, 163.0600 ns/op
// GC:  48 0 0 819200000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4724 has exited with code 0.

Mean = 163.292 ns, StdErr = 0.123 ns (0.08%), N = 3, StdDev = 0.214 ns
Min = 163.060 ns, Q1 = 163.197 ns, Median = 163.334 ns, Q3 = 163.407 ns, Max = 163.481 ns
IQR = 0.210 ns, LowerFence = 162.881 ns, UpperFence = 163.723 ns
ConfidenceInterval = [159.395 ns; 167.188 ns] (CI 99.9%), Margin = 3.897 ns (2.39% of Mean)
Skewness = -0.19, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-05 10:57 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 67.112 ns, StdErr = 0.025 ns (0.04%), N = 13, StdDev = 0.092 ns
Min = 66.981 ns, Q1 = 67.042 ns, Median = 67.136 ns, Q3 = 67.181 ns, Max = 67.273 ns
IQR = 0.138 ns, LowerFence = 66.835 ns, UpperFence = 67.388 ns
ConfidenceInterval = [67.002 ns; 67.222 ns] (CI 99.9%), Margin = 0.110 ns (0.16% of Mean)
Skewness = 0.11, Kurtosis = 1.59, MValue = 2
-------------------- Histogram --------------------
[66.930 ns ; 67.324 ns) | @@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 115.403 ns, StdErr = 0.153 ns (0.13%), N = 14, StdDev = 0.572 ns
Min = 114.370 ns, Q1 = 115.127 ns, Median = 115.382 ns, Q3 = 115.626 ns, Max = 116.586 ns
IQR = 0.499 ns, LowerFence = 114.378 ns, UpperFence = 116.375 ns
ConfidenceInterval = [114.757 ns; 116.048 ns] (CI 99.9%), Margin = 0.645 ns (0.56% of Mean)
Skewness = 0.18, Kurtosis = 2.61, MValue = 2
-------------------- Histogram --------------------
[114.231 ns ; 116.898 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 73.325 ns, StdErr = 0.036 ns (0.05%), N = 15, StdDev = 0.140 ns
Min = 72.983 ns, Q1 = 73.242 ns, Median = 73.373 ns, Q3 = 73.425 ns, Max = 73.537 ns
IQR = 0.183 ns, LowerFence = 72.968 ns, UpperFence = 73.699 ns
ConfidenceInterval = [73.175 ns; 73.475 ns] (CI 99.9%), Margin = 0.150 ns (0.20% of Mean)
Skewness = -0.76, Kurtosis = 2.97, MValue = 2
-------------------- Histogram --------------------
[72.908 ns ; 73.612 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 160.338 ns, StdErr = 0.185 ns (0.12%), N = 14, StdDev = 0.692 ns
Min = 159.016 ns, Q1 = 159.965 ns, Median = 160.330 ns, Q3 = 160.812 ns, Max = 161.364 ns
IQR = 0.846 ns, LowerFence = 158.696 ns, UpperFence = 162.082 ns
ConfidenceInterval = [159.558 ns; 161.119 ns] (CI 99.9%), Margin = 0.780 ns (0.49% of Mean)
Skewness = -0.24, Kurtosis = 2.07, MValue = 2
-------------------- Histogram --------------------
[158.639 ns ; 161.741 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 65.763 ns, StdErr = 0.139 ns (0.21%), N = 3, StdDev = 0.241 ns
Min = 65.611 ns, Q1 = 65.624 ns, Median = 65.637 ns, Q3 = 65.839 ns, Max = 66.041 ns
IQR = 0.215 ns, LowerFence = 65.302 ns, UpperFence = 66.161 ns
ConfidenceInterval = [61.370 ns; 70.157 ns] (CI 99.9%), Margin = 4.394 ns (6.68% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[65.607 ns ; 66.045 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 114.066 ns, StdErr = 1.163 ns (1.02%), N = 3, StdDev = 2.015 ns
Min = 112.800 ns, Q1 = 112.904 ns, Median = 113.008 ns, Q3 = 114.698 ns, Max = 116.389 ns
IQR = 1.795 ns, LowerFence = 110.212 ns, UpperFence = 117.391 ns
ConfidenceInterval = [77.302 ns; 150.829 ns] (CI 99.9%), Margin = 36.764 ns (32.23% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[112.761 ns ; 116.428 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 76.087 ns, StdErr = 0.049 ns (0.06%), N = 3, StdDev = 0.085 ns
Min = 75.997 ns, Q1 = 76.048 ns, Median = 76.100 ns, Q3 = 76.132 ns, Max = 76.165 ns
IQR = 0.084 ns, LowerFence = 75.923 ns, UpperFence = 76.258 ns
ConfidenceInterval = [74.545 ns; 77.629 ns] (CI 99.9%), Margin = 1.542 ns (2.03% of Mean)
Skewness = -0.15, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[75.920 ns ; 76.242 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 163.292 ns, StdErr = 0.123 ns (0.08%), N = 3, StdDev = 0.214 ns
Min = 163.060 ns, Q1 = 163.197 ns, Median = 163.334 ns, Q3 = 163.407 ns, Max = 163.481 ns
IQR = 0.210 ns, LowerFence = 162.881 ns, UpperFence = 163.723 ns
ConfidenceInterval = [159.395 ns; 167.188 ns] (CI 99.9%), Margin = 3.897 ns (2.39% of Mean)
Skewness = -0.19, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[162.866 ns ; 163.675 ns) | @@@
---------------------------------------------------

// * Summary *

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error     | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|----------:|---------:|-------:|----------:|
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  67.11 ns |  0.110 ns | 0.092 ns | 0.0018 |      32 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 115.40 ns |  0.645 ns | 0.572 ns | 0.0162 |     272 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  73.33 ns |  0.150 ns | 0.140 ns | 0.0061 |     104 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 160.34 ns |  0.780 ns | 0.692 ns | 0.0117 |     200 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  65.76 ns |  4.394 ns | 0.241 ns | 0.0018 |      32 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 114.07 ns | 36.764 ns | 2.015 ns | 0.0162 |     272 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  76.09 ns |  1.542 ns | 0.085 ns | 0.0061 |     104 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 163.29 ns |  3.897 ns | 0.214 ns | 0.0117 |     200 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput                        -> 2 outliers were removed (67.70 ns, 67.93 ns)
  CoreDispatchBenchmarks.'Notification  Notify': RunStrategy=Throughput                 -> 1 outlier  was  removed (117.45 ns)
  CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': RunStrategy=Throughput -> 1 outlier  was  removed (163.63 ns)
// * Config Issues *

// * Warnings *
Configuration
  Summary -> The exporter MarkdownExporter-github is already present in configuration. There may be unexpected results.

// * Legends *
  Mean      : Arithmetic mean of all measurements
  Error     : Half of 99.9% confidence interval
  StdDev    : Standard deviation of all measurements
  Gen0      : GC Generation 0 collects per 1000 operations
  Allocated : Allocated memory per single operation (managed only, inclusive, 1KB = 1024B)
  1 ns      : 1 Nanosecond (0.000000001 sec)

// * Diagnostic Output - MemoryDiagnoser *


// ***** BenchmarkRunner: End *****
Run time: 00:01:42 (102.03 sec), executed benchmarks: 8

Global total time: 00:01:56 (116.69 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
