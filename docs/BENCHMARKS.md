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

Run: 2026-05-04 20:11 UTC | Branch: copilot/fix-pipeline-failures-and-warnings | Commit: 42c6e5e

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error      | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|-----------:|---------:|-------:|----------:|
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  65.15 ns |   0.098 ns | 0.086 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 111.62 ns |   0.615 ns | 0.575 ns | 0.0157 |     264 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  69.76 ns |   0.150 ns | 0.133 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 153.32 ns |   0.451 ns | 0.377 ns | 0.0115 |     192 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  65.27 ns |   2.678 ns | 0.147 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 108.90 ns |  17.540 ns | 0.961 ns | 0.0157 |     264 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  70.63 ns |   8.367 ns | 0.459 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 157.78 ns | 114.838 ns | 6.295 ns | 0.0115 |     192 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.88 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 12.54 sec and exited with 0
// ***** Done, took 00:00:14 (14.49 sec)   *****
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

OverheadJitting  1: 1000 op, 187868.00 ns, 187.8680 ns/op
WorkloadJitting  1: 1000 op, 964147.00 ns, 964.1470 ns/op

OverheadJitting  2: 16000 op, 194979.00 ns, 12.1862 ns/op
WorkloadJitting  2: 16000 op, 6324073.00 ns, 395.2546 ns/op

WorkloadPilot    1: 16000 op, 7435208.00 ns, 464.7005 ns/op
WorkloadPilot    2: 32000 op, 10066384.00 ns, 314.5745 ns/op
WorkloadPilot    3: 64000 op, 18911349.00 ns, 295.4898 ns/op
WorkloadPilot    4: 128000 op, 40702872.00 ns, 317.9912 ns/op
WorkloadPilot    5: 256000 op, 69511825.00 ns, 271.5306 ns/op
WorkloadPilot    6: 512000 op, 35022081.00 ns, 68.4025 ns/op
WorkloadPilot    7: 1024000 op, 66810155.00 ns, 65.2443 ns/op
WorkloadPilot    8: 2048000 op, 133162423.00 ns, 65.0207 ns/op
WorkloadPilot    9: 4096000 op, 266863514.00 ns, 65.1522 ns/op
WorkloadPilot   10: 8192000 op, 533992631.00 ns, 65.1846 ns/op

OverheadWarmup   1: 8192000 op, 33750.00 ns, 0.0041 ns/op
OverheadWarmup   2: 8192000 op, 18568.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18458.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18477.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18588.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 25458.00 ns, 0.0031 ns/op
OverheadWarmup   7: 8192000 op, 26709.00 ns, 0.0033 ns/op
OverheadWarmup   8: 8192000 op, 26550.00 ns, 0.0032 ns/op
OverheadWarmup   9: 8192000 op, 31457.00 ns, 0.0038 ns/op
OverheadWarmup  10: 8192000 op, 26439.00 ns, 0.0032 ns/op

OverheadActual   1: 8192000 op, 26439.00 ns, 0.0032 ns/op
OverheadActual   2: 8192000 op, 25788.00 ns, 0.0031 ns/op
OverheadActual   3: 8192000 op, 25858.00 ns, 0.0032 ns/op
OverheadActual   4: 8192000 op, 23484.00 ns, 0.0029 ns/op
OverheadActual   5: 8192000 op, 26750.00 ns, 0.0033 ns/op
OverheadActual   6: 8192000 op, 26729.00 ns, 0.0033 ns/op
OverheadActual   7: 8192000 op, 31757.00 ns, 0.0039 ns/op
OverheadActual   8: 8192000 op, 26359.00 ns, 0.0032 ns/op
OverheadActual   9: 8192000 op, 18597.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 24957.00 ns, 0.0030 ns/op
OverheadActual  11: 8192000 op, 25267.00 ns, 0.0031 ns/op
OverheadActual  12: 8192000 op, 26189.00 ns, 0.0032 ns/op
OverheadActual  13: 8192000 op, 26669.00 ns, 0.0033 ns/op
OverheadActual  14: 8192000 op, 25948.00 ns, 0.0032 ns/op
OverheadActual  15: 8192000 op, 31767.00 ns, 0.0039 ns/op
OverheadActual  16: 8192000 op, 25508.00 ns, 0.0031 ns/op
OverheadActual  17: 8192000 op, 25077.00 ns, 0.0031 ns/op
OverheadActual  18: 8192000 op, 41161.00 ns, 0.0050 ns/op
OverheadActual  19: 8192000 op, 25247.00 ns, 0.0031 ns/op
OverheadActual  20: 8192000 op, 18738.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 544445220.00 ns, 66.4606 ns/op
WorkloadWarmup   2: 8192000 op, 541819622.00 ns, 66.1401 ns/op
WorkloadWarmup   3: 8192000 op, 540905338.00 ns, 66.0285 ns/op
WorkloadWarmup   4: 8192000 op, 532974879.00 ns, 65.0604 ns/op
WorkloadWarmup   5: 8192000 op, 534373068.00 ns, 65.2311 ns/op
WorkloadWarmup   6: 8192000 op, 532166091.00 ns, 64.9617 ns/op
WorkloadWarmup   7: 8192000 op, 534430433.00 ns, 65.2381 ns/op
WorkloadWarmup   8: 8192000 op, 533342593.00 ns, 65.1053 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 537624021.00 ns, 65.6279 ns/op
WorkloadActual   2: 8192000 op, 533125521.00 ns, 65.0788 ns/op
WorkloadActual   3: 8192000 op, 532774081.00 ns, 65.0359 ns/op
WorkloadActual   4: 8192000 op, 534393288.00 ns, 65.2336 ns/op
WorkloadActual   5: 8192000 op, 533450432.00 ns, 65.1185 ns/op
WorkloadActual   6: 8192000 op, 534213982.00 ns, 65.2117 ns/op
WorkloadActual   7: 8192000 op, 533921880.00 ns, 65.1760 ns/op
WorkloadActual   8: 8192000 op, 534426624.00 ns, 65.2376 ns/op
WorkloadActual   9: 8192000 op, 533518498.00 ns, 65.1268 ns/op
WorkloadActual  10: 8192000 op, 533951379.00 ns, 65.1796 ns/op
WorkloadActual  11: 8192000 op, 533426030.00 ns, 65.1155 ns/op
WorkloadActual  12: 8192000 op, 535499656.00 ns, 65.3686 ns/op
WorkloadActual  13: 8192000 op, 533219353.00 ns, 65.0903 ns/op
WorkloadActual  14: 8192000 op, 533172405.00 ns, 65.0845 ns/op
WorkloadActual  15: 8192000 op, 533441910.00 ns, 65.1174 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 533099618.00 ns, 65.0756 ns/op
WorkloadResult   2: 8192000 op, 532748178.00 ns, 65.0327 ns/op
WorkloadResult   3: 8192000 op, 534367385.00 ns, 65.2304 ns/op
WorkloadResult   4: 8192000 op, 533424529.00 ns, 65.1153 ns/op
WorkloadResult   5: 8192000 op, 534188079.00 ns, 65.2085 ns/op
WorkloadResult   6: 8192000 op, 533895977.00 ns, 65.1728 ns/op
WorkloadResult   7: 8192000 op, 534400721.00 ns, 65.2345 ns/op
WorkloadResult   8: 8192000 op, 533492595.00 ns, 65.1236 ns/op
WorkloadResult   9: 8192000 op, 533925476.00 ns, 65.1764 ns/op
WorkloadResult  10: 8192000 op, 533400127.00 ns, 65.1123 ns/op
WorkloadResult  11: 8192000 op, 535473753.00 ns, 65.3654 ns/op
WorkloadResult  12: 8192000 op, 533193450.00 ns, 65.0871 ns/op
WorkloadResult  13: 8192000 op, 533146502.00 ns, 65.0814 ns/op
WorkloadResult  14: 8192000 op, 533416007.00 ns, 65.1143 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4415 has exited with code 0.

Mean = 65.152 ns, StdErr = 0.023 ns (0.04%), N = 14, StdDev = 0.086 ns
Min = 65.033 ns, Q1 = 65.093 ns, Median = 65.119 ns, Q3 = 65.200 ns, Max = 65.365 ns
IQR = 0.107 ns, LowerFence = 64.933 ns, UpperFence = 65.361 ns
ConfidenceInterval = [65.055 ns; 65.250 ns] (CI 99.9%), Margin = 0.098 ns (0.15% of Mean)
Skewness = 0.88, Kurtosis = 3.14, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-04 20:11 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 178043.00 ns, 178.0430 ns/op
WorkloadJitting  1: 1000 op, 1168130.00 ns, 1.1681 us/op

OverheadJitting  2: 16000 op, 200447.00 ns, 12.5279 ns/op
WorkloadJitting  2: 16000 op, 11643410.00 ns, 727.7131 ns/op

WorkloadPilot    1: 16000 op, 9584792.00 ns, 599.0495 ns/op
WorkloadPilot    2: 32000 op, 19739208.00 ns, 616.8503 ns/op
WorkloadPilot    3: 64000 op, 43226205.00 ns, 675.4095 ns/op
WorkloadPilot    4: 128000 op, 59720979.00 ns, 466.5701 ns/op
WorkloadPilot    5: 256000 op, 48631889.00 ns, 189.9683 ns/op
WorkloadPilot    6: 512000 op, 58337461.00 ns, 113.9404 ns/op
WorkloadPilot    7: 1024000 op, 115396498.00 ns, 112.6919 ns/op
WorkloadPilot    8: 2048000 op, 229523086.00 ns, 112.0718 ns/op
WorkloadPilot    9: 4096000 op, 460329913.00 ns, 112.3852 ns/op
WorkloadPilot   10: 8192000 op, 912354078.00 ns, 111.3713 ns/op

OverheadWarmup   1: 8192000 op, 23395.00 ns, 0.0029 ns/op
OverheadWarmup   2: 8192000 op, 18328.00 ns, 0.0022 ns/op
OverheadWarmup   3: 8192000 op, 18287.00 ns, 0.0022 ns/op
OverheadWarmup   4: 8192000 op, 32678.00 ns, 0.0040 ns/op
OverheadWarmup   5: 8192000 op, 18298.00 ns, 0.0022 ns/op
OverheadWarmup   6: 8192000 op, 18327.00 ns, 0.0022 ns/op
OverheadWarmup   7: 8192000 op, 18407.00 ns, 0.0022 ns/op
OverheadWarmup   8: 8192000 op, 18567.00 ns, 0.0023 ns/op
OverheadWarmup   9: 8192000 op, 21862.00 ns, 0.0027 ns/op
OverheadWarmup  10: 8192000 op, 18337.00 ns, 0.0022 ns/op

OverheadActual   1: 8192000 op, 18347.00 ns, 0.0022 ns/op
OverheadActual   2: 8192000 op, 18387.00 ns, 0.0022 ns/op
OverheadActual   3: 8192000 op, 18347.00 ns, 0.0022 ns/op
OverheadActual   4: 8192000 op, 18368.00 ns, 0.0022 ns/op
OverheadActual   5: 8192000 op, 18298.00 ns, 0.0022 ns/op
OverheadActual   6: 8192000 op, 18507.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 22113.00 ns, 0.0027 ns/op
OverheadActual   8: 8192000 op, 18257.00 ns, 0.0022 ns/op
OverheadActual   9: 8192000 op, 18507.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 18778.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 18347.00 ns, 0.0022 ns/op
OverheadActual  12: 8192000 op, 18267.00 ns, 0.0022 ns/op
OverheadActual  13: 8192000 op, 18307.00 ns, 0.0022 ns/op
OverheadActual  14: 8192000 op, 18328.00 ns, 0.0022 ns/op
OverheadActual  15: 8192000 op, 22403.00 ns, 0.0027 ns/op

WorkloadWarmup   1: 8192000 op, 937308367.00 ns, 114.4175 ns/op
WorkloadWarmup   2: 8192000 op, 929443791.00 ns, 113.4575 ns/op
WorkloadWarmup   3: 8192000 op, 918506978.00 ns, 112.1224 ns/op
WorkloadWarmup   4: 8192000 op, 920964506.00 ns, 112.4224 ns/op
WorkloadWarmup   5: 8192000 op, 916075539.00 ns, 111.8256 ns/op
WorkloadWarmup   6: 8192000 op, 918818960.00 ns, 112.1605 ns/op
WorkloadWarmup   7: 8192000 op, 927900924.00 ns, 113.2692 ns/op
WorkloadWarmup   8: 8192000 op, 927606699.00 ns, 113.2332 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 920834614.00 ns, 112.4066 ns/op
WorkloadActual   2: 8192000 op, 916022009.00 ns, 111.8191 ns/op
WorkloadActual   3: 8192000 op, 909454269.00 ns, 111.0174 ns/op
WorkloadActual   4: 8192000 op, 911512218.00 ns, 111.2686 ns/op
WorkloadActual   5: 8192000 op, 910020844.00 ns, 111.0865 ns/op
WorkloadActual   6: 8192000 op, 925435936.00 ns, 112.9683 ns/op
WorkloadActual   7: 8192000 op, 917873576.00 ns, 112.0451 ns/op
WorkloadActual   8: 8192000 op, 912820605.00 ns, 111.4283 ns/op
WorkloadActual   9: 8192000 op, 910407954.00 ns, 111.1338 ns/op
WorkloadActual  10: 8192000 op, 918678899.00 ns, 112.1434 ns/op
WorkloadActual  11: 8192000 op, 911041250.00 ns, 111.2111 ns/op
WorkloadActual  12: 8192000 op, 910181375.00 ns, 111.1061 ns/op
WorkloadActual  13: 8192000 op, 912007818.00 ns, 111.3291 ns/op
WorkloadActual  14: 8192000 op, 917010773.00 ns, 111.9398 ns/op
WorkloadActual  15: 8192000 op, 912775484.00 ns, 111.4228 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 920816267.00 ns, 112.4043 ns/op
WorkloadResult   2: 8192000 op, 916003662.00 ns, 111.8169 ns/op
WorkloadResult   3: 8192000 op, 909435922.00 ns, 111.0151 ns/op
WorkloadResult   4: 8192000 op, 911493871.00 ns, 111.2663 ns/op
WorkloadResult   5: 8192000 op, 910002497.00 ns, 111.0843 ns/op
WorkloadResult   6: 8192000 op, 925417589.00 ns, 112.9660 ns/op
WorkloadResult   7: 8192000 op, 917855229.00 ns, 112.0429 ns/op
WorkloadResult   8: 8192000 op, 912802258.00 ns, 111.4261 ns/op
WorkloadResult   9: 8192000 op, 910389607.00 ns, 111.1315 ns/op
WorkloadResult  10: 8192000 op, 918660552.00 ns, 112.1412 ns/op
WorkloadResult  11: 8192000 op, 911022903.00 ns, 111.2089 ns/op
WorkloadResult  12: 8192000 op, 910163028.00 ns, 111.1039 ns/op
WorkloadResult  13: 8192000 op, 911989471.00 ns, 111.3268 ns/op
WorkloadResult  14: 8192000 op, 916992426.00 ns, 111.9376 ns/op
WorkloadResult  15: 8192000 op, 912757137.00 ns, 111.4205 ns/op
// GC:  129 0 0 2162688000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4430 has exited with code 0.

Mean = 111.619 ns, StdErr = 0.149 ns (0.13%), N = 15, StdDev = 0.575 ns
Min = 111.015 ns, Q1 = 111.170 ns, Median = 111.421 ns, Q3 = 111.990 ns, Max = 112.966 ns
IQR = 0.820 ns, LowerFence = 109.940 ns, UpperFence = 113.220 ns
ConfidenceInterval = [111.005 ns; 112.234 ns] (CI 99.9%), Margin = 0.615 ns (0.55% of Mean)
Skewness = 0.84, Kurtosis = 2.55, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-04 20:12 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 190072.00 ns, 190.0720 ns/op
WorkloadJitting  1: 1000 op, 1104555.00 ns, 1.1046 us/op

OverheadJitting  2: 16000 op, 239804.00 ns, 14.9878 ns/op
WorkloadJitting  2: 16000 op, 9111137.00 ns, 569.4461 ns/op

WorkloadPilot    1: 16000 op, 7377561.00 ns, 461.0976 ns/op
WorkloadPilot    2: 32000 op, 15547274.00 ns, 485.8523 ns/op
WorkloadPilot    3: 64000 op, 26068709.00 ns, 407.3236 ns/op
WorkloadPilot    4: 128000 op, 52262212.00 ns, 408.2985 ns/op
WorkloadPilot    5: 256000 op, 63960069.00 ns, 249.8440 ns/op
WorkloadPilot    6: 512000 op, 37323028.00 ns, 72.8965 ns/op
WorkloadPilot    7: 1024000 op, 71592436.00 ns, 69.9145 ns/op
WorkloadPilot    8: 2048000 op, 143311039.00 ns, 69.9761 ns/op
WorkloadPilot    9: 4096000 op, 286147326.00 ns, 69.8602 ns/op
WorkloadPilot   10: 8192000 op, 573000045.00 ns, 69.9463 ns/op

OverheadWarmup   1: 8192000 op, 23165.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 21191.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 21181.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 21191.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 21211.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 21091.00 ns, 0.0026 ns/op
OverheadWarmup   7: 8192000 op, 21201.00 ns, 0.0026 ns/op
OverheadWarmup   8: 8192000 op, 21192.00 ns, 0.0026 ns/op

OverheadActual   1: 8192000 op, 21983.00 ns, 0.0027 ns/op
OverheadActual   2: 8192000 op, 18327.00 ns, 0.0022 ns/op
OverheadActual   3: 8192000 op, 18377.00 ns, 0.0022 ns/op
OverheadActual   4: 8192000 op, 18438.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18547.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18437.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18347.00 ns, 0.0022 ns/op
OverheadActual   8: 8192000 op, 18337.00 ns, 0.0022 ns/op
OverheadActual   9: 8192000 op, 21712.00 ns, 0.0027 ns/op
OverheadActual  10: 8192000 op, 21221.00 ns, 0.0026 ns/op
OverheadActual  11: 8192000 op, 21171.00 ns, 0.0026 ns/op
OverheadActual  12: 8192000 op, 21231.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 21121.00 ns, 0.0026 ns/op
OverheadActual  15: 8192000 op, 21242.00 ns, 0.0026 ns/op
OverheadActual  16: 8192000 op, 21261.00 ns, 0.0026 ns/op
OverheadActual  17: 8192000 op, 21732.00 ns, 0.0027 ns/op
OverheadActual  18: 8192000 op, 18848.00 ns, 0.0023 ns/op
OverheadActual  19: 8192000 op, 18307.00 ns, 0.0022 ns/op
OverheadActual  20: 8192000 op, 18318.00 ns, 0.0022 ns/op

WorkloadWarmup   1: 8192000 op, 606971132.00 ns, 74.0932 ns/op
WorkloadWarmup   2: 8192000 op, 580386689.00 ns, 70.8480 ns/op
WorkloadWarmup   3: 8192000 op, 572171999.00 ns, 69.8452 ns/op
WorkloadWarmup   4: 8192000 op, 572640277.00 ns, 69.9024 ns/op
WorkloadWarmup   5: 8192000 op, 572677483.00 ns, 69.9069 ns/op
WorkloadWarmup   6: 8192000 op, 571816538.00 ns, 69.8018 ns/op
WorkloadWarmup   7: 8192000 op, 570819412.00 ns, 69.6801 ns/op
WorkloadWarmup   8: 8192000 op, 575186287.00 ns, 70.2132 ns/op
WorkloadWarmup   9: 8192000 op, 572287445.00 ns, 69.8593 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 577470770.00 ns, 70.4920 ns/op
WorkloadActual   2: 8192000 op, 572417188.00 ns, 69.8751 ns/op
WorkloadActual   3: 8192000 op, 571187317.00 ns, 69.7250 ns/op
WorkloadActual   4: 8192000 op, 572444648.00 ns, 69.8785 ns/op
WorkloadActual   5: 8192000 op, 572052327.00 ns, 69.8306 ns/op
WorkloadActual   6: 8192000 op, 571537665.00 ns, 69.7678 ns/op
WorkloadActual   7: 8192000 op, 570624143.00 ns, 69.6563 ns/op
WorkloadActual   8: 8192000 op, 570452782.00 ns, 69.6353 ns/op
WorkloadActual   9: 8192000 op, 571647805.00 ns, 69.7812 ns/op
WorkloadActual  10: 8192000 op, 571312169.00 ns, 69.7403 ns/op
WorkloadActual  11: 8192000 op, 569637018.00 ns, 69.5358 ns/op
WorkloadActual  12: 8192000 op, 571536360.00 ns, 69.7676 ns/op
WorkloadActual  13: 8192000 op, 572198130.00 ns, 69.8484 ns/op
WorkloadActual  14: 8192000 op, 570401671.00 ns, 69.6291 ns/op
WorkloadActual  15: 8192000 op, 574006407.00 ns, 70.0691 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 572397203.50 ns, 69.8727 ns/op
WorkloadResult   2: 8192000 op, 571167332.50 ns, 69.7226 ns/op
WorkloadResult   3: 8192000 op, 572424663.50 ns, 69.8761 ns/op
WorkloadResult   4: 8192000 op, 572032342.50 ns, 69.8282 ns/op
WorkloadResult   5: 8192000 op, 571517680.50 ns, 69.7653 ns/op
WorkloadResult   6: 8192000 op, 570604158.50 ns, 69.6538 ns/op
WorkloadResult   7: 8192000 op, 570432797.50 ns, 69.6329 ns/op
WorkloadResult   8: 8192000 op, 571627820.50 ns, 69.7788 ns/op
WorkloadResult   9: 8192000 op, 571292184.50 ns, 69.7378 ns/op
WorkloadResult  10: 8192000 op, 569617033.50 ns, 69.5333 ns/op
WorkloadResult  11: 8192000 op, 571516375.50 ns, 69.7652 ns/op
WorkloadResult  12: 8192000 op, 572178145.50 ns, 69.8460 ns/op
WorkloadResult  13: 8192000 op, 570381686.50 ns, 69.6267 ns/op
WorkloadResult  14: 8192000 op, 573986422.50 ns, 70.0667 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4445 has exited with code 0.

Mean = 69.765 ns, StdErr = 0.036 ns (0.05%), N = 14, StdDev = 0.133 ns
Min = 69.533 ns, Q1 = 69.671 ns, Median = 69.765 ns, Q3 = 69.842 ns, Max = 70.067 ns
IQR = 0.171 ns, LowerFence = 69.415 ns, UpperFence = 70.097 ns
ConfidenceInterval = [69.614 ns; 69.915 ns] (CI 99.9%), Margin = 0.150 ns (0.22% of Mean)
Skewness = 0.37, Kurtosis = 2.8, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-04 20:12 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 222600.00 ns, 222.6000 ns/op
WorkloadJitting  1: 1000 op, 2082743.00 ns, 2.0827 us/op

OverheadJitting  2: 16000 op, 283139.00 ns, 17.6962 ns/op
WorkloadJitting  2: 16000 op, 22348139.00 ns, 1.3968 us/op

WorkloadPilot    1: 16000 op, 14879180.00 ns, 929.9488 ns/op
WorkloadPilot    2: 32000 op, 25105981.00 ns, 784.5619 ns/op
WorkloadPilot    3: 64000 op, 49993939.00 ns, 781.1553 ns/op
WorkloadPilot    4: 128000 op, 92716403.00 ns, 724.3469 ns/op
WorkloadPilot    5: 256000 op, 67993820.00 ns, 265.6009 ns/op
WorkloadPilot    6: 512000 op, 78571651.00 ns, 153.4603 ns/op
WorkloadPilot    7: 1024000 op, 156790850.00 ns, 153.1161 ns/op
WorkloadPilot    8: 2048000 op, 313339622.00 ns, 152.9979 ns/op
WorkloadPilot    9: 4096000 op, 628897989.00 ns, 153.5395 ns/op

OverheadWarmup   1: 4096000 op, 18007.00 ns, 0.0044 ns/op
OverheadWarmup   2: 4096000 op, 9734.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 9384.00 ns, 0.0023 ns/op
OverheadWarmup   4: 4096000 op, 9524.00 ns, 0.0023 ns/op
OverheadWarmup   5: 4096000 op, 9364.00 ns, 0.0023 ns/op
OverheadWarmup   6: 4096000 op, 9334.00 ns, 0.0023 ns/op
OverheadWarmup   7: 4096000 op, 9364.00 ns, 0.0023 ns/op
OverheadWarmup   8: 4096000 op, 9424.00 ns, 0.0023 ns/op
OverheadWarmup   9: 4096000 op, 9364.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9534.00 ns, 0.0023 ns/op
OverheadActual   2: 4096000 op, 9434.00 ns, 0.0023 ns/op
OverheadActual   3: 4096000 op, 9454.00 ns, 0.0023 ns/op
OverheadActual   4: 4096000 op, 9434.00 ns, 0.0023 ns/op
OverheadActual   5: 4096000 op, 9434.00 ns, 0.0023 ns/op
OverheadActual   6: 4096000 op, 9374.00 ns, 0.0023 ns/op
OverheadActual   7: 4096000 op, 9314.00 ns, 0.0023 ns/op
OverheadActual   8: 4096000 op, 12589.00 ns, 0.0031 ns/op
OverheadActual   9: 4096000 op, 10786.00 ns, 0.0026 ns/op
OverheadActual  10: 4096000 op, 9424.00 ns, 0.0023 ns/op
OverheadActual  11: 4096000 op, 9634.00 ns, 0.0024 ns/op
OverheadActual  12: 4096000 op, 9364.00 ns, 0.0023 ns/op
OverheadActual  13: 4096000 op, 9324.00 ns, 0.0023 ns/op
OverheadActual  14: 4096000 op, 9534.00 ns, 0.0023 ns/op
OverheadActual  15: 4096000 op, 9334.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 4096000 op, 642506266.00 ns, 156.8619 ns/op
WorkloadWarmup   2: 4096000 op, 644341864.00 ns, 157.3100 ns/op
WorkloadWarmup   3: 4096000 op, 632450768.00 ns, 154.4069 ns/op
WorkloadWarmup   4: 4096000 op, 630488885.00 ns, 153.9280 ns/op
WorkloadWarmup   5: 4096000 op, 633404202.00 ns, 154.6397 ns/op
WorkloadWarmup   6: 4096000 op, 681538386.00 ns, 166.3912 ns/op
WorkloadWarmup   7: 4096000 op, 633529217.00 ns, 154.6702 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 639072574.00 ns, 156.0236 ns/op
WorkloadActual   2: 4096000 op, 633875839.00 ns, 154.7548 ns/op
WorkloadActual   3: 4096000 op, 631194670.00 ns, 154.1003 ns/op
WorkloadActual   4: 4096000 op, 630017978.00 ns, 153.8130 ns/op
WorkloadActual   5: 4096000 op, 629081151.00 ns, 153.5843 ns/op
WorkloadActual   6: 4096000 op, 626657303.00 ns, 152.9925 ns/op
WorkloadActual   7: 4096000 op, 628064776.00 ns, 153.3361 ns/op
WorkloadActual   8: 4096000 op, 625651113.00 ns, 152.7469 ns/op
WorkloadActual   9: 4096000 op, 629045585.00 ns, 153.5756 ns/op
WorkloadActual  10: 4096000 op, 627596474.00 ns, 153.2218 ns/op
WorkloadActual  11: 4096000 op, 626391981.00 ns, 152.9277 ns/op
WorkloadActual  12: 4096000 op, 627207338.00 ns, 153.1268 ns/op
WorkloadActual  13: 4096000 op, 628521223.00 ns, 153.4476 ns/op
WorkloadActual  14: 4096000 op, 627753416.00 ns, 153.2601 ns/op
WorkloadActual  15: 4096000 op, 626991959.00 ns, 153.0742 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 631185236.00 ns, 154.0980 ns/op
WorkloadResult   2: 4096000 op, 630008544.00 ns, 153.8107 ns/op
WorkloadResult   3: 4096000 op, 629071717.00 ns, 153.5820 ns/op
WorkloadResult   4: 4096000 op, 626647869.00 ns, 152.9902 ns/op
WorkloadResult   5: 4096000 op, 628055342.00 ns, 153.3338 ns/op
WorkloadResult   6: 4096000 op, 625641679.00 ns, 152.7446 ns/op
WorkloadResult   7: 4096000 op, 629036151.00 ns, 153.5733 ns/op
WorkloadResult   8: 4096000 op, 627587040.00 ns, 153.2195 ns/op
WorkloadResult   9: 4096000 op, 626382547.00 ns, 152.9254 ns/op
WorkloadResult  10: 4096000 op, 627197904.00 ns, 153.1245 ns/op
WorkloadResult  11: 4096000 op, 628511789.00 ns, 153.4453 ns/op
WorkloadResult  12: 4096000 op, 627743982.00 ns, 153.2578 ns/op
WorkloadResult  13: 4096000 op, 626982525.00 ns, 153.0719 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4458 has exited with code 0.

Mean = 153.321 ns, StdErr = 0.105 ns (0.07%), N = 13, StdDev = 0.377 ns
Min = 152.745 ns, Q1 = 153.072 ns, Median = 153.258 ns, Q3 = 153.573 ns, Max = 154.098 ns
IQR = 0.501 ns, LowerFence = 152.320 ns, UpperFence = 154.325 ns
ConfidenceInterval = [152.870 ns; 153.773 ns] (CI 99.9%), Margin = 0.451 ns (0.29% of Mean)
Skewness = 0.44, Kurtosis = 2.22, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-04 20:12 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 197062.00 ns, 197.0620 ns/op
WorkloadJitting  1: 1000 op, 1033971.00 ns, 1.0340 us/op

OverheadJitting  2: 16000 op, 203631.00 ns, 12.7269 ns/op
WorkloadJitting  2: 16000 op, 6205805.00 ns, 387.8628 ns/op

WorkloadPilot    1: 16000 op, 4947012.00 ns, 309.1883 ns/op
WorkloadPilot    2: 32000 op, 9027632.00 ns, 282.1135 ns/op
WorkloadPilot    3: 64000 op, 17999911.00 ns, 281.2486 ns/op
WorkloadPilot    4: 128000 op, 36115975.00 ns, 282.1561 ns/op
WorkloadPilot    5: 256000 op, 78174156.00 ns, 305.3678 ns/op
WorkloadPilot    6: 512000 op, 37293078.00 ns, 72.8380 ns/op
WorkloadPilot    7: 1024000 op, 67552434.00 ns, 65.9692 ns/op
WorkloadPilot    8: 2048000 op, 133416109.00 ns, 65.1446 ns/op
WorkloadPilot    9: 4096000 op, 266259700.00 ns, 65.0048 ns/op
WorkloadPilot   10: 8192000 op, 534359384.00 ns, 65.2294 ns/op

OverheadWarmup   1: 8192000 op, 38447.00 ns, 0.0047 ns/op
OverheadWarmup   2: 8192000 op, 21021.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 20991.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 21011.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 21061.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 31297.00 ns, 0.0038 ns/op
OverheadWarmup   7: 8192000 op, 31827.00 ns, 0.0039 ns/op
OverheadWarmup   8: 8192000 op, 31476.00 ns, 0.0038 ns/op
OverheadWarmup   9: 8192000 op, 34711.00 ns, 0.0042 ns/op
OverheadWarmup  10: 8192000 op, 43114.00 ns, 0.0053 ns/op

OverheadActual   1: 8192000 op, 32418.00 ns, 0.0040 ns/op
OverheadActual   2: 8192000 op, 30595.00 ns, 0.0037 ns/op
OverheadActual   3: 8192000 op, 28613.00 ns, 0.0035 ns/op
OverheadActual   4: 8192000 op, 30735.00 ns, 0.0038 ns/op
OverheadActual   5: 8192000 op, 31246.00 ns, 0.0038 ns/op
OverheadActual   6: 8192000 op, 30555.00 ns, 0.0037 ns/op
OverheadActual   7: 8192000 op, 32368.00 ns, 0.0040 ns/op
OverheadActual   8: 8192000 op, 27931.00 ns, 0.0034 ns/op
OverheadActual   9: 8192000 op, 30645.00 ns, 0.0037 ns/op
OverheadActual  10: 8192000 op, 31126.00 ns, 0.0038 ns/op
OverheadActual  11: 8192000 op, 30706.00 ns, 0.0037 ns/op
OverheadActual  12: 8192000 op, 30315.00 ns, 0.0037 ns/op
OverheadActual  13: 8192000 op, 32057.00 ns, 0.0039 ns/op
OverheadActual  14: 8192000 op, 28312.00 ns, 0.0035 ns/op
OverheadActual  15: 8192000 op, 31717.00 ns, 0.0039 ns/op

WorkloadWarmup   1: 8192000 op, 544639820.00 ns, 66.4844 ns/op
WorkloadWarmup   2: 8192000 op, 541560912.00 ns, 66.1085 ns/op
WorkloadWarmup   3: 8192000 op, 535789971.00 ns, 65.4040 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 536019481.00 ns, 65.4321 ns/op
WorkloadActual   2: 8192000 op, 533624518.00 ns, 65.1397 ns/op
WorkloadActual   3: 8192000 op, 534634943.00 ns, 65.2631 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 535988775.00 ns, 65.4283 ns/op
WorkloadResult   2: 8192000 op, 533593812.00 ns, 65.1360 ns/op
WorkloadResult   3: 8192000 op, 534604237.00 ns, 65.2593 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4469 has exited with code 0.

Mean = 65.275 ns, StdErr = 0.085 ns (0.13%), N = 3, StdDev = 0.147 ns
Min = 65.136 ns, Q1 = 65.198 ns, Median = 65.259 ns, Q3 = 65.344 ns, Max = 65.428 ns
IQR = 0.146 ns, LowerFence = 64.978 ns, UpperFence = 65.563 ns
ConfidenceInterval = [62.597 ns; 67.952 ns] (CI 99.9%), Margin = 2.678 ns (4.10% of Mean)
Skewness = 0.1, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-04 20:11 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 234557.00 ns, 234.5570 ns/op
WorkloadJitting  1: 1000 op, 1227978.00 ns, 1.2280 us/op

OverheadJitting  2: 16000 op, 193216.00 ns, 12.0760 ns/op
WorkloadJitting  2: 16000 op, 11330564.00 ns, 708.1603 ns/op

WorkloadPilot    1: 16000 op, 9690646.00 ns, 605.6654 ns/op
WorkloadPilot    2: 32000 op, 23347954.00 ns, 729.6236 ns/op
WorkloadPilot    3: 64000 op, 30113199.00 ns, 470.5187 ns/op
WorkloadPilot    4: 128000 op, 52628380.00 ns, 411.1592 ns/op
WorkloadPilot    5: 256000 op, 62480435.00 ns, 244.0642 ns/op
WorkloadPilot    6: 512000 op, 55479190.00 ns, 108.3578 ns/op
WorkloadPilot    7: 1024000 op, 110244264.00 ns, 107.6604 ns/op
WorkloadPilot    8: 2048000 op, 223764006.00 ns, 109.2598 ns/op
WorkloadPilot    9: 4096000 op, 442489543.00 ns, 108.0297 ns/op
WorkloadPilot   10: 8192000 op, 897680799.00 ns, 109.5802 ns/op

OverheadWarmup   1: 8192000 op, 29674.00 ns, 0.0036 ns/op
OverheadWarmup   2: 8192000 op, 29623.00 ns, 0.0036 ns/op
OverheadWarmup   3: 8192000 op, 28793.00 ns, 0.0035 ns/op
OverheadWarmup   4: 8192000 op, 20991.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 38186.00 ns, 0.0047 ns/op
OverheadWarmup   6: 8192000 op, 29404.00 ns, 0.0036 ns/op
OverheadWarmup   7: 8192000 op, 20991.00 ns, 0.0026 ns/op
OverheadWarmup   8: 8192000 op, 45577.00 ns, 0.0056 ns/op
OverheadWarmup   9: 8192000 op, 35172.00 ns, 0.0043 ns/op

OverheadActual   1: 8192000 op, 21011.00 ns, 0.0026 ns/op
OverheadActual   2: 8192000 op, 21182.00 ns, 0.0026 ns/op
OverheadActual   3: 8192000 op, 21222.00 ns, 0.0026 ns/op
OverheadActual   4: 8192000 op, 60490.00 ns, 0.0074 ns/op
OverheadActual   5: 8192000 op, 20991.00 ns, 0.0026 ns/op
OverheadActual   6: 8192000 op, 47891.00 ns, 0.0058 ns/op
OverheadActual   7: 8192000 op, 20981.00 ns, 0.0026 ns/op
OverheadActual   8: 8192000 op, 24877.00 ns, 0.0030 ns/op
OverheadActual   9: 8192000 op, 20991.00 ns, 0.0026 ns/op
OverheadActual  10: 8192000 op, 29394.00 ns, 0.0036 ns/op
OverheadActual  11: 8192000 op, 29313.00 ns, 0.0036 ns/op
OverheadActual  12: 8192000 op, 20991.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 20971.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 37485.00 ns, 0.0046 ns/op
OverheadActual  15: 8192000 op, 20981.00 ns, 0.0026 ns/op
OverheadActual  16: 8192000 op, 40540.00 ns, 0.0049 ns/op
OverheadActual  17: 8192000 op, 29293.00 ns, 0.0036 ns/op
OverheadActual  18: 8192000 op, 21001.00 ns, 0.0026 ns/op
OverheadActual  19: 8192000 op, 20971.00 ns, 0.0026 ns/op
OverheadActual  20: 8192000 op, 21001.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 903197566.00 ns, 110.2536 ns/op
WorkloadWarmup   2: 8192000 op, 901298164.00 ns, 110.0217 ns/op
WorkloadWarmup   3: 8192000 op, 896295288.00 ns, 109.4110 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 887335884.00 ns, 108.3174 ns/op
WorkloadActual   2: 8192000 op, 887837595.00 ns, 108.3786 ns/op
WorkloadActual   3: 8192000 op, 901221499.00 ns, 110.0124 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 887314787.50 ns, 108.3148 ns/op
WorkloadResult   2: 8192000 op, 887816498.50 ns, 108.3760 ns/op
WorkloadResult   3: 8192000 op, 901200402.50 ns, 110.0098 ns/op
// GC:  129 0 0 2162688000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4479 has exited with code 0.

Mean = 108.900 ns, StdErr = 0.555 ns (0.51%), N = 3, StdDev = 0.961 ns
Min = 108.315 ns, Q1 = 108.345 ns, Median = 108.376 ns, Q3 = 109.193 ns, Max = 110.010 ns
IQR = 0.848 ns, LowerFence = 107.074 ns, UpperFence = 110.464 ns
ConfidenceInterval = [91.360 ns; 126.440 ns] (CI 99.9%), Margin = 17.540 ns (16.11% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-04 20:11 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 189340.00 ns, 189.3400 ns/op
WorkloadJitting  1: 1000 op, 1195610.00 ns, 1.1956 us/op

OverheadJitting  2: 16000 op, 195770.00 ns, 12.2356 ns/op
WorkloadJitting  2: 16000 op, 8494947.00 ns, 530.9342 ns/op

WorkloadPilot    1: 16000 op, 7336381.00 ns, 458.5238 ns/op
WorkloadPilot    2: 32000 op, 13165753.00 ns, 411.4298 ns/op
WorkloadPilot    3: 64000 op, 26012793.00 ns, 406.4499 ns/op
WorkloadPilot    4: 128000 op, 50860751.00 ns, 397.3496 ns/op
WorkloadPilot    5: 256000 op, 60784534.00 ns, 237.4396 ns/op
WorkloadPilot    6: 512000 op, 37640909.00 ns, 73.5174 ns/op
WorkloadPilot    7: 1024000 op, 72544083.00 ns, 70.8438 ns/op
WorkloadPilot    8: 2048000 op, 144460397.00 ns, 70.5373 ns/op
WorkloadPilot    9: 4096000 op, 286955565.00 ns, 70.0575 ns/op
WorkloadPilot   10: 8192000 op, 576459431.00 ns, 70.3686 ns/op

OverheadWarmup   1: 8192000 op, 26098.00 ns, 0.0032 ns/op
OverheadWarmup   2: 8192000 op, 21211.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 21171.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 21141.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 21161.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 21161.00 ns, 0.0026 ns/op
OverheadWarmup   7: 8192000 op, 21141.00 ns, 0.0026 ns/op
OverheadWarmup   8: 8192000 op, 21191.00 ns, 0.0026 ns/op

OverheadActual   1: 8192000 op, 24886.00 ns, 0.0030 ns/op
OverheadActual   2: 8192000 op, 21242.00 ns, 0.0026 ns/op
OverheadActual   3: 8192000 op, 21212.00 ns, 0.0026 ns/op
OverheadActual   4: 8192000 op, 21221.00 ns, 0.0026 ns/op
OverheadActual   5: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadActual   6: 8192000 op, 21201.00 ns, 0.0026 ns/op
OverheadActual   7: 8192000 op, 21211.00 ns, 0.0026 ns/op
OverheadActual   8: 8192000 op, 21112.00 ns, 0.0026 ns/op
OverheadActual   9: 8192000 op, 24596.00 ns, 0.0030 ns/op
OverheadActual  10: 8192000 op, 21162.00 ns, 0.0026 ns/op
OverheadActual  11: 8192000 op, 21151.00 ns, 0.0026 ns/op
OverheadActual  12: 8192000 op, 21212.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 21321.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 21162.00 ns, 0.0026 ns/op
OverheadActual  15: 8192000 op, 21202.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 585831287.00 ns, 71.5126 ns/op
WorkloadWarmup   2: 8192000 op, 590015461.00 ns, 72.0234 ns/op
WorkloadWarmup   3: 8192000 op, 575677073.00 ns, 70.2731 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 582858155.00 ns, 71.1497 ns/op
WorkloadActual   2: 8192000 op, 577518409.00 ns, 70.4979 ns/op
WorkloadActual   3: 8192000 op, 575610404.00 ns, 70.2649 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 582836943.00 ns, 71.1471 ns/op
WorkloadResult   2: 8192000 op, 577497197.00 ns, 70.4953 ns/op
WorkloadResult   3: 8192000 op, 575589192.00 ns, 70.2624 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4490 has exited with code 0.

Mean = 70.635 ns, StdErr = 0.265 ns (0.37%), N = 3, StdDev = 0.459 ns
Min = 70.262 ns, Q1 = 70.379 ns, Median = 70.495 ns, Q3 = 70.821 ns, Max = 71.147 ns
IQR = 0.442 ns, LowerFence = 69.715 ns, UpperFence = 71.485 ns
ConfidenceInterval = [62.268 ns; 79.001 ns] (CI 99.9%), Margin = 8.367 ns (11.84% of Mean)
Skewness = 0.28, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-04 20:11 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 188579.00 ns, 188.5790 ns/op
WorkloadJitting  1: 1000 op, 1548985.00 ns, 1.5490 us/op

OverheadJitting  2: 16000 op, 210692.00 ns, 13.1683 ns/op
WorkloadJitting  2: 16000 op, 16102646.00 ns, 1.0064 us/op

WorkloadPilot    1: 16000 op, 14416247.00 ns, 901.0154 ns/op
WorkloadPilot    2: 32000 op, 25850153.00 ns, 807.8173 ns/op
WorkloadPilot    3: 64000 op, 51115870.00 ns, 798.6855 ns/op
WorkloadPilot    4: 128000 op, 70684403.00 ns, 552.2219 ns/op
WorkloadPilot    5: 256000 op, 46546738.00 ns, 181.8232 ns/op
WorkloadPilot    6: 512000 op, 79065885.00 ns, 154.4256 ns/op
WorkloadPilot    7: 1024000 op, 158279800.00 ns, 154.5701 ns/op
WorkloadPilot    8: 2048000 op, 316937478.00 ns, 154.7546 ns/op
WorkloadPilot    9: 4096000 op, 632402053.00 ns, 154.3950 ns/op

OverheadWarmup   1: 4096000 op, 14321.00 ns, 0.0035 ns/op
OverheadWarmup   2: 4096000 op, 10786.00 ns, 0.0026 ns/op
OverheadWarmup   3: 4096000 op, 10686.00 ns, 0.0026 ns/op
OverheadWarmup   4: 4096000 op, 10716.00 ns, 0.0026 ns/op
OverheadWarmup   5: 4096000 op, 10726.00 ns, 0.0026 ns/op
OverheadWarmup   6: 4096000 op, 10957.00 ns, 0.0027 ns/op
OverheadWarmup   7: 4096000 op, 10665.00 ns, 0.0026 ns/op
OverheadWarmup   8: 4096000 op, 10786.00 ns, 0.0026 ns/op
OverheadWarmup   9: 4096000 op, 10826.00 ns, 0.0026 ns/op
OverheadWarmup  10: 4096000 op, 10686.00 ns, 0.0026 ns/op

OverheadActual   1: 4096000 op, 10756.00 ns, 0.0026 ns/op
OverheadActual   2: 4096000 op, 10766.00 ns, 0.0026 ns/op
OverheadActual   3: 4096000 op, 10746.00 ns, 0.0026 ns/op
OverheadActual   4: 4096000 op, 10746.00 ns, 0.0026 ns/op
OverheadActual   5: 4096000 op, 10746.00 ns, 0.0026 ns/op
OverheadActual   6: 4096000 op, 10706.00 ns, 0.0026 ns/op
OverheadActual   7: 4096000 op, 13319.00 ns, 0.0033 ns/op
OverheadActual   8: 4096000 op, 10706.00 ns, 0.0026 ns/op
OverheadActual   9: 4096000 op, 10685.00 ns, 0.0026 ns/op
OverheadActual  10: 4096000 op, 10686.00 ns, 0.0026 ns/op
OverheadActual  11: 4096000 op, 10746.00 ns, 0.0026 ns/op
OverheadActual  12: 4096000 op, 10826.00 ns, 0.0026 ns/op
OverheadActual  13: 4096000 op, 10715.00 ns, 0.0026 ns/op
OverheadActual  14: 4096000 op, 10746.00 ns, 0.0026 ns/op
OverheadActual  15: 4096000 op, 10736.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 4096000 op, 641077742.00 ns, 156.5131 ns/op
WorkloadWarmup   2: 4096000 op, 642799944.00 ns, 156.9336 ns/op
WorkloadWarmup   3: 4096000 op, 632153515.00 ns, 154.3344 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 676011990.00 ns, 165.0420 ns/op
WorkloadActual   2: 4096000 op, 632780132.00 ns, 154.4873 ns/op
WorkloadActual   3: 4096000 op, 630054146.00 ns, 153.8218 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 676001244.00 ns, 165.0394 ns/op
WorkloadResult   2: 4096000 op, 632769386.00 ns, 154.4847 ns/op
WorkloadResult   3: 4096000 op, 630043400.00 ns, 153.8192 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4499 has exited with code 0.

Mean = 157.781 ns, StdErr = 3.634 ns (2.30%), N = 3, StdDev = 6.295 ns
Min = 153.819 ns, Q1 = 154.152 ns, Median = 154.485 ns, Q3 = 159.762 ns, Max = 165.039 ns
IQR = 5.610 ns, LowerFence = 145.737 ns, UpperFence = 168.177 ns
ConfidenceInterval = [42.943 ns; 272.619 ns] (CI 99.9%), Margin = 114.838 ns (72.78% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-04 20:11 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 65.152 ns, StdErr = 0.023 ns (0.04%), N = 14, StdDev = 0.086 ns
Min = 65.033 ns, Q1 = 65.093 ns, Median = 65.119 ns, Q3 = 65.200 ns, Max = 65.365 ns
IQR = 0.107 ns, LowerFence = 64.933 ns, UpperFence = 65.361 ns
ConfidenceInterval = [65.055 ns; 65.250 ns] (CI 99.9%), Margin = 0.098 ns (0.15% of Mean)
Skewness = 0.88, Kurtosis = 3.14, MValue = 2
-------------------- Histogram --------------------
[64.986 ns ; 65.413 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 111.619 ns, StdErr = 0.149 ns (0.13%), N = 15, StdDev = 0.575 ns
Min = 111.015 ns, Q1 = 111.170 ns, Median = 111.421 ns, Q3 = 111.990 ns, Max = 112.966 ns
IQR = 0.820 ns, LowerFence = 109.940 ns, UpperFence = 113.220 ns
ConfidenceInterval = [111.005 ns; 112.234 ns] (CI 99.9%), Margin = 0.615 ns (0.55% of Mean)
Skewness = 0.84, Kurtosis = 2.55, MValue = 2
-------------------- Histogram --------------------
[110.709 ns ; 113.272 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 69.765 ns, StdErr = 0.036 ns (0.05%), N = 14, StdDev = 0.133 ns
Min = 69.533 ns, Q1 = 69.671 ns, Median = 69.765 ns, Q3 = 69.842 ns, Max = 70.067 ns
IQR = 0.171 ns, LowerFence = 69.415 ns, UpperFence = 70.097 ns
ConfidenceInterval = [69.614 ns; 69.915 ns] (CI 99.9%), Margin = 0.150 ns (0.22% of Mean)
Skewness = 0.37, Kurtosis = 2.8, MValue = 2
-------------------- Histogram --------------------
[69.461 ns ; 70.139 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 153.321 ns, StdErr = 0.105 ns (0.07%), N = 13, StdDev = 0.377 ns
Min = 152.745 ns, Q1 = 153.072 ns, Median = 153.258 ns, Q3 = 153.573 ns, Max = 154.098 ns
IQR = 0.501 ns, LowerFence = 152.320 ns, UpperFence = 154.325 ns
ConfidenceInterval = [152.870 ns; 153.773 ns] (CI 99.9%), Margin = 0.451 ns (0.29% of Mean)
Skewness = 0.44, Kurtosis = 2.22, MValue = 2
-------------------- Histogram --------------------
[152.534 ns ; 154.308 ns) | @@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 65.275 ns, StdErr = 0.085 ns (0.13%), N = 3, StdDev = 0.147 ns
Min = 65.136 ns, Q1 = 65.198 ns, Median = 65.259 ns, Q3 = 65.344 ns, Max = 65.428 ns
IQR = 0.146 ns, LowerFence = 64.978 ns, UpperFence = 65.563 ns
ConfidenceInterval = [62.597 ns; 67.952 ns] (CI 99.9%), Margin = 2.678 ns (4.10% of Mean)
Skewness = 0.1, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[65.002 ns ; 65.562 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 108.900 ns, StdErr = 0.555 ns (0.51%), N = 3, StdDev = 0.961 ns
Min = 108.315 ns, Q1 = 108.345 ns, Median = 108.376 ns, Q3 = 109.193 ns, Max = 110.010 ns
IQR = 0.848 ns, LowerFence = 107.074 ns, UpperFence = 110.464 ns
ConfidenceInterval = [91.360 ns; 126.440 ns] (CI 99.9%), Margin = 17.540 ns (16.11% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[108.287 ns ; 110.037 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 70.635 ns, StdErr = 0.265 ns (0.37%), N = 3, StdDev = 0.459 ns
Min = 70.262 ns, Q1 = 70.379 ns, Median = 70.495 ns, Q3 = 70.821 ns, Max = 71.147 ns
IQR = 0.442 ns, LowerFence = 69.715 ns, UpperFence = 71.485 ns
ConfidenceInterval = [62.268 ns; 79.001 ns] (CI 99.9%), Margin = 8.367 ns (11.84% of Mean)
Skewness = 0.28, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[69.845 ns ; 71.565 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 157.781 ns, StdErr = 3.634 ns (2.30%), N = 3, StdDev = 6.295 ns
Min = 153.819 ns, Q1 = 154.152 ns, Median = 154.485 ns, Q3 = 159.762 ns, Max = 165.039 ns
IQR = 5.610 ns, LowerFence = 145.737 ns, UpperFence = 168.177 ns
ConfidenceInterval = [42.943 ns; 272.619 ns] (CI 99.9%), Margin = 114.838 ns (72.78% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[153.701 ns ; 165.158 ns) | @@@
---------------------------------------------------

// * Summary *

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error      | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|-----------:|---------:|-------:|----------:|
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  65.15 ns |   0.098 ns | 0.086 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 111.62 ns |   0.615 ns | 0.575 ns | 0.0157 |     264 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  69.76 ns |   0.150 ns | 0.133 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 153.32 ns |   0.451 ns | 0.377 ns | 0.0115 |     192 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  65.27 ns |   2.678 ns | 0.147 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 108.90 ns |  17.540 ns | 0.961 ns | 0.0157 |     264 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  70.63 ns |   8.367 ns | 0.459 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 157.78 ns | 114.838 ns | 6.295 ns | 0.0115 |     192 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput                        -> 1 outlier  was  removed (65.63 ns)
  CoreDispatchBenchmarks.'Request  Request': RunStrategy=Throughput                     -> 1 outlier  was  removed (70.49 ns)
  CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': RunStrategy=Throughput -> 2 outliers were removed (154.75 ns, 156.02 ns)
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
Run time: 00:01:36 (96.35 sec), executed benchmarks: 8

Global total time: 00:01:50 (110.96 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
