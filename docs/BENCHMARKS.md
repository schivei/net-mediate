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

Run: 2026-05-04 20:17 UTC | Branch: copilot/fix-pipeline-failures-and-warnings | Commit: 02e1dae

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
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  66.97 ns |  0.420 ns | 0.373 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 111.23 ns |  0.375 ns | 0.351 ns | 0.0157 |     264 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  71.21 ns |  0.538 ns | 0.477 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 156.08 ns |  1.168 ns | 1.093 ns | 0.0115 |     192 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  66.38 ns |  3.565 ns | 0.195 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 111.03 ns |  5.366 ns | 0.294 ns | 0.0157 |     264 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  72.66 ns |  7.803 ns | 0.428 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 152.48 ns | 17.483 ns | 0.958 ns | 0.0115 |     192 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.64 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 12.87 sec and exited with 0
// ***** Done, took 00:00:14 (14.57 sec)   *****
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

OverheadJitting  1: 1000 op, 188957.00 ns, 188.9570 ns/op
WorkloadJitting  1: 1000 op, 1094167.00 ns, 1.0942 us/op

OverheadJitting  2: 16000 op, 214455.00 ns, 13.4034 ns/op
WorkloadJitting  2: 16000 op, 8343188.00 ns, 521.4493 ns/op

WorkloadPilot    1: 16000 op, 7332227.00 ns, 458.2642 ns/op
WorkloadPilot    2: 32000 op, 13753554.00 ns, 429.7986 ns/op
WorkloadPilot    3: 64000 op, 22586303.00 ns, 352.9110 ns/op
WorkloadPilot    4: 128000 op, 39103264.00 ns, 305.4943 ns/op
WorkloadPilot    5: 256000 op, 59898154.00 ns, 233.9772 ns/op
WorkloadPilot    6: 512000 op, 35319973.00 ns, 68.9843 ns/op
WorkloadPilot    7: 1024000 op, 68991660.00 ns, 67.3747 ns/op
WorkloadPilot    8: 2048000 op, 136792504.00 ns, 66.7932 ns/op
WorkloadPilot    9: 4096000 op, 274366122.00 ns, 66.9839 ns/op
WorkloadPilot   10: 8192000 op, 547469381.00 ns, 66.8298 ns/op

OverheadWarmup   1: 8192000 op, 23596.00 ns, 0.0029 ns/op
OverheadWarmup   2: 8192000 op, 18548.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18689.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18498.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18328.00 ns, 0.0022 ns/op
OverheadWarmup   6: 8192000 op, 25268.00 ns, 0.0031 ns/op
OverheadWarmup   7: 8192000 op, 26771.00 ns, 0.0033 ns/op
OverheadWarmup   8: 8192000 op, 26490.00 ns, 0.0032 ns/op

OverheadActual   1: 8192000 op, 33521.00 ns, 0.0041 ns/op
OverheadActual   2: 8192000 op, 27171.00 ns, 0.0033 ns/op
OverheadActual   3: 8192000 op, 26820.00 ns, 0.0033 ns/op
OverheadActual   4: 8192000 op, 25969.00 ns, 0.0032 ns/op
OverheadActual   5: 8192000 op, 26480.00 ns, 0.0032 ns/op
OverheadActual   6: 8192000 op, 26811.00 ns, 0.0033 ns/op
OverheadActual   7: 8192000 op, 26680.00 ns, 0.0033 ns/op
OverheadActual   8: 8192000 op, 26770.00 ns, 0.0033 ns/op
OverheadActual   9: 8192000 op, 22775.00 ns, 0.0028 ns/op
OverheadActual  10: 8192000 op, 18427.00 ns, 0.0022 ns/op
OverheadActual  11: 8192000 op, 26631.00 ns, 0.0033 ns/op
OverheadActual  12: 8192000 op, 28964.00 ns, 0.0035 ns/op
OverheadActual  13: 8192000 op, 25329.00 ns, 0.0031 ns/op
OverheadActual  14: 8192000 op, 25418.00 ns, 0.0031 ns/op
OverheadActual  15: 8192000 op, 24907.00 ns, 0.0030 ns/op
OverheadActual  16: 8192000 op, 18568.00 ns, 0.0023 ns/op
OverheadActual  17: 8192000 op, 22504.00 ns, 0.0027 ns/op
OverheadActual  18: 8192000 op, 25458.00 ns, 0.0031 ns/op
OverheadActual  19: 8192000 op, 25870.00 ns, 0.0032 ns/op
OverheadActual  20: 8192000 op, 45749.00 ns, 0.0056 ns/op

WorkloadWarmup   1: 8192000 op, 562689882.00 ns, 68.6877 ns/op
WorkloadWarmup   2: 8192000 op, 555508944.00 ns, 67.8112 ns/op
WorkloadWarmup   3: 8192000 op, 551893963.00 ns, 67.3699 ns/op
WorkloadWarmup   4: 8192000 op, 552378612.00 ns, 67.4290 ns/op
WorkloadWarmup   5: 8192000 op, 549460478.00 ns, 67.0728 ns/op
WorkloadWarmup   6: 8192000 op, 547704838.00 ns, 66.8585 ns/op
WorkloadWarmup   7: 8192000 op, 548286519.00 ns, 66.9295 ns/op
WorkloadWarmup   8: 8192000 op, 547832790.00 ns, 66.8741 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 552029939.00 ns, 67.3865 ns/op
WorkloadActual   2: 8192000 op, 554292216.00 ns, 67.6626 ns/op
WorkloadActual   3: 8192000 op, 547255311.00 ns, 66.8036 ns/op
WorkloadActual   4: 8192000 op, 547421394.00 ns, 66.8239 ns/op
WorkloadActual   5: 8192000 op, 546822166.00 ns, 66.7508 ns/op
WorkloadActual   6: 8192000 op, 556014941.00 ns, 67.8729 ns/op
WorkloadActual   7: 8192000 op, 555647765.00 ns, 67.8281 ns/op
WorkloadActual   8: 8192000 op, 548836552.00 ns, 66.9966 ns/op
WorkloadActual   9: 8192000 op, 546829497.00 ns, 66.7516 ns/op
WorkloadActual  10: 8192000 op, 546864450.00 ns, 66.7559 ns/op
WorkloadActual  11: 8192000 op, 546840915.00 ns, 66.7530 ns/op
WorkloadActual  12: 8192000 op, 546705229.00 ns, 66.7365 ns/op
WorkloadActual  13: 8192000 op, 547394042.00 ns, 66.8206 ns/op
WorkloadActual  14: 8192000 op, 546541158.00 ns, 66.7164 ns/op
WorkloadActual  15: 8192000 op, 547050088.00 ns, 66.7786 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 552003714.50 ns, 67.3833 ns/op
WorkloadResult   2: 8192000 op, 554265991.50 ns, 67.6594 ns/op
WorkloadResult   3: 8192000 op, 547229086.50 ns, 66.8004 ns/op
WorkloadResult   4: 8192000 op, 547395169.50 ns, 66.8207 ns/op
WorkloadResult   5: 8192000 op, 546795941.50 ns, 66.7476 ns/op
WorkloadResult   6: 8192000 op, 555621540.50 ns, 67.8249 ns/op
WorkloadResult   7: 8192000 op, 548810327.50 ns, 66.9934 ns/op
WorkloadResult   8: 8192000 op, 546803272.50 ns, 66.7484 ns/op
WorkloadResult   9: 8192000 op, 546838225.50 ns, 66.7527 ns/op
WorkloadResult  10: 8192000 op, 546814690.50 ns, 66.7498 ns/op
WorkloadResult  11: 8192000 op, 546679004.50 ns, 66.7333 ns/op
WorkloadResult  12: 8192000 op, 547367817.50 ns, 66.8174 ns/op
WorkloadResult  13: 8192000 op, 546514933.50 ns, 66.7132 ns/op
WorkloadResult  14: 8192000 op, 547023863.50 ns, 66.7754 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4360 has exited with code 0.

Mean = 66.966 ns, StdErr = 0.100 ns (0.15%), N = 14, StdDev = 0.373 ns
Min = 66.713 ns, Q1 = 66.749 ns, Median = 66.788 ns, Q3 = 66.950 ns, Max = 67.825 ns
IQR = 0.201 ns, LowerFence = 66.447 ns, UpperFence = 67.252 ns
ConfidenceInterval = [66.545 ns; 67.386 ns] (CI 99.9%), Margin = 0.420 ns (0.63% of Mean)
Skewness = 1.32, Kurtosis = 3.06, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-04 20:17 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 182726.00 ns, 182.7260 ns/op
WorkloadJitting  1: 1000 op, 1186768.00 ns, 1.1868 us/op

OverheadJitting  2: 16000 op, 197038.00 ns, 12.3149 ns/op
WorkloadJitting  2: 16000 op, 11733653.00 ns, 733.3533 ns/op

WorkloadPilot    1: 16000 op, 9933247.00 ns, 620.8279 ns/op
WorkloadPilot    2: 32000 op, 17379786.00 ns, 543.1183 ns/op
WorkloadPilot    3: 64000 op, 31737079.00 ns, 495.8919 ns/op
WorkloadPilot    4: 128000 op, 58248171.00 ns, 455.0638 ns/op
WorkloadPilot    5: 256000 op, 56085095.00 ns, 219.0824 ns/op
WorkloadPilot    6: 512000 op, 57192303.00 ns, 111.7037 ns/op
WorkloadPilot    7: 1024000 op, 114893987.00 ns, 112.2012 ns/op
WorkloadPilot    8: 2048000 op, 229051688.00 ns, 111.8416 ns/op
WorkloadPilot    9: 4096000 op, 457354371.00 ns, 111.6588 ns/op
WorkloadPilot   10: 8192000 op, 907585802.00 ns, 110.7893 ns/op

OverheadWarmup   1: 8192000 op, 23495.00 ns, 0.0029 ns/op
OverheadWarmup   2: 8192000 op, 18348.00 ns, 0.0022 ns/op
OverheadWarmup   3: 8192000 op, 18318.00 ns, 0.0022 ns/op
OverheadWarmup   4: 8192000 op, 18348.00 ns, 0.0022 ns/op
OverheadWarmup   5: 8192000 op, 18849.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18288.00 ns, 0.0022 ns/op
OverheadWarmup   7: 8192000 op, 18538.00 ns, 0.0023 ns/op
OverheadWarmup   8: 8192000 op, 18578.00 ns, 0.0023 ns/op
OverheadWarmup   9: 8192000 op, 21703.00 ns, 0.0026 ns/op
OverheadWarmup  10: 8192000 op, 21723.00 ns, 0.0027 ns/op

OverheadActual   1: 8192000 op, 18418.00 ns, 0.0022 ns/op
OverheadActual   2: 8192000 op, 18458.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 18498.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18438.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18398.00 ns, 0.0022 ns/op
OverheadActual   6: 8192000 op, 18528.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 22474.00 ns, 0.0027 ns/op
OverheadActual   8: 8192000 op, 18799.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18689.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 18548.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 18338.00 ns, 0.0022 ns/op
OverheadActual  12: 8192000 op, 18478.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 18368.00 ns, 0.0022 ns/op
OverheadActual  14: 8192000 op, 18668.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 22033.00 ns, 0.0027 ns/op

WorkloadWarmup   1: 8192000 op, 915305716.00 ns, 111.7317 ns/op
WorkloadWarmup   2: 8192000 op, 912321035.00 ns, 111.3673 ns/op
WorkloadWarmup   3: 8192000 op, 907295002.00 ns, 110.7538 ns/op
WorkloadWarmup   4: 8192000 op, 905853919.00 ns, 110.5779 ns/op
WorkloadWarmup   5: 8192000 op, 910235780.00 ns, 111.1128 ns/op
WorkloadWarmup   6: 8192000 op, 909918189.00 ns, 111.0740 ns/op
WorkloadWarmup   7: 8192000 op, 909264960.00 ns, 110.9943 ns/op
WorkloadWarmup   8: 8192000 op, 906335011.00 ns, 110.6366 ns/op
WorkloadWarmup   9: 8192000 op, 913508918.00 ns, 111.5123 ns/op
WorkloadWarmup  10: 8192000 op, 908367530.00 ns, 110.8847 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 913741976.00 ns, 111.5408 ns/op
WorkloadActual   2: 8192000 op, 914672781.00 ns, 111.6544 ns/op
WorkloadActual   3: 8192000 op, 910215469.00 ns, 111.1103 ns/op
WorkloadActual   4: 8192000 op, 909738051.00 ns, 111.0520 ns/op
WorkloadActual   5: 8192000 op, 912176476.00 ns, 111.3497 ns/op
WorkloadActual   6: 8192000 op, 917311494.00 ns, 111.9765 ns/op
WorkloadActual   7: 8192000 op, 909384127.00 ns, 111.0088 ns/op
WorkloadActual   8: 8192000 op, 909158196.00 ns, 110.9812 ns/op
WorkloadActual   9: 8192000 op, 908720622.00 ns, 110.9278 ns/op
WorkloadActual  10: 8192000 op, 909258587.00 ns, 110.9935 ns/op
WorkloadActual  11: 8192000 op, 908668513.00 ns, 110.9214 ns/op
WorkloadActual  12: 8192000 op, 907725024.00 ns, 110.8063 ns/op
WorkloadActual  13: 8192000 op, 913795233.00 ns, 111.5473 ns/op
WorkloadActual  14: 8192000 op, 914119399.00 ns, 111.5868 ns/op
WorkloadActual  15: 8192000 op, 909088551.00 ns, 110.9727 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 913723478.00 ns, 111.5385 ns/op
WorkloadResult   2: 8192000 op, 914654283.00 ns, 111.6521 ns/op
WorkloadResult   3: 8192000 op, 910196971.00 ns, 111.1080 ns/op
WorkloadResult   4: 8192000 op, 909719553.00 ns, 111.0498 ns/op
WorkloadResult   5: 8192000 op, 912157978.00 ns, 111.3474 ns/op
WorkloadResult   6: 8192000 op, 917292996.00 ns, 111.9742 ns/op
WorkloadResult   7: 8192000 op, 909365629.00 ns, 111.0065 ns/op
WorkloadResult   8: 8192000 op, 909139698.00 ns, 110.9790 ns/op
WorkloadResult   9: 8192000 op, 908702124.00 ns, 110.9256 ns/op
WorkloadResult  10: 8192000 op, 909240089.00 ns, 110.9912 ns/op
WorkloadResult  11: 8192000 op, 908650015.00 ns, 110.9192 ns/op
WorkloadResult  12: 8192000 op, 907706526.00 ns, 110.8040 ns/op
WorkloadResult  13: 8192000 op, 913776735.00 ns, 111.5450 ns/op
WorkloadResult  14: 8192000 op, 914100901.00 ns, 111.5846 ns/op
WorkloadResult  15: 8192000 op, 909070053.00 ns, 110.9705 ns/op
// GC:  129 0 0 2162688000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4400 has exited with code 0.

Mean = 111.226 ns, StdErr = 0.091 ns (0.08%), N = 15, StdDev = 0.351 ns
Min = 110.804 ns, Q1 = 110.975 ns, Median = 111.050 ns, Q3 = 111.542 ns, Max = 111.974 ns
IQR = 0.567 ns, LowerFence = 110.124 ns, UpperFence = 112.392 ns
ConfidenceInterval = [110.852 ns; 111.601 ns] (CI 99.9%), Margin = 0.375 ns (0.34% of Mean)
Skewness = 0.64, Kurtosis = 1.97, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-04 20:18 (0h 2m from now) **
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

OverheadJitting  1: 1000 op, 190299.00 ns, 190.2990 ns/op
WorkloadJitting  1: 1000 op, 1197453.00 ns, 1.1975 us/op

OverheadJitting  2: 16000 op, 247655.00 ns, 15.4784 ns/op
WorkloadJitting  2: 16000 op, 8707456.00 ns, 544.2160 ns/op

WorkloadPilot    1: 16000 op, 6953570.00 ns, 434.5981 ns/op
WorkloadPilot    2: 32000 op, 12694374.00 ns, 396.6992 ns/op
WorkloadPilot    3: 64000 op, 38143948.00 ns, 595.9992 ns/op
WorkloadPilot    4: 128000 op, 51160475.00 ns, 399.6912 ns/op
WorkloadPilot    5: 256000 op, 55374636.00 ns, 216.3072 ns/op
WorkloadPilot    6: 512000 op, 36379415.00 ns, 71.0535 ns/op
WorkloadPilot    7: 1024000 op, 72931383.00 ns, 71.2221 ns/op
WorkloadPilot    8: 2048000 op, 146407872.00 ns, 71.4882 ns/op
WorkloadPilot    9: 4096000 op, 292635570.00 ns, 71.4442 ns/op
WorkloadPilot   10: 8192000 op, 586889802.00 ns, 71.6418 ns/op

OverheadWarmup   1: 8192000 op, 33991.00 ns, 0.0041 ns/op
OverheadWarmup   2: 8192000 op, 18679.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18427.00 ns, 0.0022 ns/op
OverheadWarmup   4: 8192000 op, 18348.00 ns, 0.0022 ns/op
OverheadWarmup   5: 8192000 op, 18648.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18318.00 ns, 0.0022 ns/op
OverheadWarmup   7: 8192000 op, 18508.00 ns, 0.0023 ns/op
OverheadWarmup   8: 8192000 op, 18288.00 ns, 0.0022 ns/op

OverheadActual   1: 8192000 op, 22444.00 ns, 0.0027 ns/op
OverheadActual   2: 8192000 op, 18938.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 18458.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18628.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18759.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18688.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18298.00 ns, 0.0022 ns/op
OverheadActual   8: 8192000 op, 18297.00 ns, 0.0022 ns/op
OverheadActual   9: 8192000 op, 22624.00 ns, 0.0028 ns/op
OverheadActual  10: 8192000 op, 18558.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 18568.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 18408.00 ns, 0.0022 ns/op
OverheadActual  13: 8192000 op, 18739.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18328.00 ns, 0.0022 ns/op
OverheadActual  15: 8192000 op, 18307.00 ns, 0.0022 ns/op

WorkloadWarmup   1: 8192000 op, 598441124.00 ns, 73.0519 ns/op
WorkloadWarmup   2: 8192000 op, 594336609.00 ns, 72.5509 ns/op
WorkloadWarmup   3: 8192000 op, 579467151.00 ns, 70.7357 ns/op
WorkloadWarmup   4: 8192000 op, 581567752.00 ns, 70.9922 ns/op
WorkloadWarmup   5: 8192000 op, 584550749.00 ns, 71.3563 ns/op
WorkloadWarmup   6: 8192000 op, 579759667.00 ns, 70.7714 ns/op
WorkloadWarmup   7: 8192000 op, 581506155.00 ns, 70.9846 ns/op
WorkloadWarmup   8: 8192000 op, 581625105.00 ns, 70.9992 ns/op
WorkloadWarmup   9: 8192000 op, 579885330.00 ns, 70.7868 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 589133187.00 ns, 71.9157 ns/op
WorkloadActual   2: 8192000 op, 587904105.00 ns, 71.7656 ns/op
WorkloadActual   3: 8192000 op, 584225228.00 ns, 71.3166 ns/op
WorkloadActual   4: 8192000 op, 582183711.00 ns, 71.0673 ns/op
WorkloadActual   5: 8192000 op, 581577633.00 ns, 70.9934 ns/op
WorkloadActual   6: 8192000 op, 582644309.00 ns, 71.1236 ns/op
WorkloadActual   7: 8192000 op, 581325401.00 ns, 70.9626 ns/op
WorkloadActual   8: 8192000 op, 613650229.00 ns, 74.9085 ns/op
WorkloadActual   9: 8192000 op, 581690104.00 ns, 71.0071 ns/op
WorkloadActual  10: 8192000 op, 580240287.00 ns, 70.8301 ns/op
WorkloadActual  11: 8192000 op, 579967225.00 ns, 70.7968 ns/op
WorkloadActual  12: 8192000 op, 581329691.00 ns, 70.9631 ns/op
WorkloadActual  13: 8192000 op, 592905190.00 ns, 72.3761 ns/op
WorkloadActual  14: 8192000 op, 582930233.00 ns, 71.1585 ns/op
WorkloadActual  15: 8192000 op, 579435292.00 ns, 70.7318 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 589114619.00 ns, 71.9134 ns/op
WorkloadResult   2: 8192000 op, 587885537.00 ns, 71.7634 ns/op
WorkloadResult   3: 8192000 op, 584206660.00 ns, 71.3143 ns/op
WorkloadResult   4: 8192000 op, 582165143.00 ns, 71.0651 ns/op
WorkloadResult   5: 8192000 op, 581559065.00 ns, 70.9911 ns/op
WorkloadResult   6: 8192000 op, 582625741.00 ns, 71.1213 ns/op
WorkloadResult   7: 8192000 op, 581306833.00 ns, 70.9603 ns/op
WorkloadResult   8: 8192000 op, 581671536.00 ns, 71.0048 ns/op
WorkloadResult   9: 8192000 op, 580221719.00 ns, 70.8278 ns/op
WorkloadResult  10: 8192000 op, 579948657.00 ns, 70.7945 ns/op
WorkloadResult  11: 8192000 op, 581311123.00 ns, 70.9608 ns/op
WorkloadResult  12: 8192000 op, 592886622.00 ns, 72.3739 ns/op
WorkloadResult  13: 8192000 op, 582911665.00 ns, 71.1562 ns/op
WorkloadResult  14: 8192000 op, 579416724.00 ns, 70.7296 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4418 has exited with code 0.

Mean = 71.213 ns, StdErr = 0.128 ns (0.18%), N = 14, StdDev = 0.477 ns
Min = 70.730 ns, Q1 = 70.960 ns, Median = 71.035 ns, Q3 = 71.275 ns, Max = 72.374 ns
IQR = 0.314 ns, LowerFence = 70.489 ns, UpperFence = 71.746 ns
ConfidenceInterval = [70.674 ns; 71.751 ns] (CI 99.9%), Margin = 0.538 ns (0.76% of Mean)
Skewness = 1.17, Kurtosis = 3.13, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-04 20:18 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 229317.00 ns, 229.3170 ns/op
WorkloadJitting  1: 1000 op, 2127032.00 ns, 2.1270 us/op

OverheadJitting  2: 16000 op, 271341.00 ns, 16.9588 ns/op
WorkloadJitting  2: 16000 op, 19196526.00 ns, 1.1998 us/op

WorkloadPilot    1: 16000 op, 14155841.00 ns, 884.7401 ns/op
WorkloadPilot    2: 32000 op, 25423207.00 ns, 794.4752 ns/op
WorkloadPilot    3: 64000 op, 50164742.00 ns, 783.8241 ns/op
WorkloadPilot    4: 128000 op, 94458652.00 ns, 737.9582 ns/op
WorkloadPilot    5: 256000 op, 65473901.00 ns, 255.7574 ns/op
WorkloadPilot    6: 512000 op, 78808657.00 ns, 153.9232 ns/op
WorkloadPilot    7: 1024000 op, 158911136.00 ns, 155.1867 ns/op
WorkloadPilot    8: 2048000 op, 321692743.00 ns, 157.0765 ns/op
WorkloadPilot    9: 4096000 op, 639209381.00 ns, 156.0570 ns/op

OverheadWarmup   1: 4096000 op, 12979.00 ns, 0.0032 ns/op
OverheadWarmup   2: 4096000 op, 10897.00 ns, 0.0027 ns/op
OverheadWarmup   3: 4096000 op, 10766.00 ns, 0.0026 ns/op
OverheadWarmup   4: 4096000 op, 10796.00 ns, 0.0026 ns/op
OverheadWarmup   5: 4096000 op, 10816.00 ns, 0.0026 ns/op
OverheadWarmup   6: 4096000 op, 10716.00 ns, 0.0026 ns/op
OverheadWarmup   7: 4096000 op, 10797.00 ns, 0.0026 ns/op
OverheadWarmup   8: 4096000 op, 23506.00 ns, 0.0057 ns/op
OverheadWarmup   9: 4096000 op, 10757.00 ns, 0.0026 ns/op

OverheadActual   1: 4096000 op, 9604.00 ns, 0.0023 ns/op
OverheadActual   2: 4096000 op, 9464.00 ns, 0.0023 ns/op
OverheadActual   3: 4096000 op, 9444.00 ns, 0.0023 ns/op
OverheadActual   4: 4096000 op, 9464.00 ns, 0.0023 ns/op
OverheadActual   5: 4096000 op, 9424.00 ns, 0.0023 ns/op
OverheadActual   6: 4096000 op, 9314.00 ns, 0.0023 ns/op
OverheadActual   7: 4096000 op, 9304.00 ns, 0.0023 ns/op
OverheadActual   8: 4096000 op, 12008.00 ns, 0.0029 ns/op
OverheadActual   9: 4096000 op, 10836.00 ns, 0.0026 ns/op
OverheadActual  10: 4096000 op, 10836.00 ns, 0.0026 ns/op
OverheadActual  11: 4096000 op, 38598.00 ns, 0.0094 ns/op
OverheadActual  12: 4096000 op, 10827.00 ns, 0.0026 ns/op
OverheadActual  13: 4096000 op, 11017.00 ns, 0.0027 ns/op
OverheadActual  14: 4096000 op, 10816.00 ns, 0.0026 ns/op
OverheadActual  15: 4096000 op, 10757.00 ns, 0.0026 ns/op
OverheadActual  16: 4096000 op, 10826.00 ns, 0.0026 ns/op
OverheadActual  17: 4096000 op, 10757.00 ns, 0.0026 ns/op
OverheadActual  18: 4096000 op, 9404.00 ns, 0.0023 ns/op
OverheadActual  19: 4096000 op, 9334.00 ns, 0.0023 ns/op
OverheadActual  20: 4096000 op, 9495.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 4096000 op, 653597885.00 ns, 159.5698 ns/op
WorkloadWarmup   2: 4096000 op, 647743317.00 ns, 158.1405 ns/op
WorkloadWarmup   3: 4096000 op, 634601342.00 ns, 154.9320 ns/op
WorkloadWarmup   4: 4096000 op, 643929901.00 ns, 157.2094 ns/op
WorkloadWarmup   5: 4096000 op, 642355869.00 ns, 156.8252 ns/op
WorkloadWarmup   6: 4096000 op, 639188772.00 ns, 156.0519 ns/op
WorkloadWarmup   7: 4096000 op, 637747089.00 ns, 155.7000 ns/op
WorkloadWarmup   8: 4096000 op, 637195144.00 ns, 155.5652 ns/op
WorkloadWarmup   9: 4096000 op, 639767809.00 ns, 156.1933 ns/op
WorkloadWarmup  10: 4096000 op, 632950967.00 ns, 154.5290 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 643684769.00 ns, 157.1496 ns/op
WorkloadActual   2: 4096000 op, 636306448.00 ns, 155.3483 ns/op
WorkloadActual   3: 4096000 op, 632669200.00 ns, 154.4603 ns/op
WorkloadActual   4: 4096000 op, 633287131.00 ns, 154.6111 ns/op
WorkloadActual   5: 4096000 op, 635420432.00 ns, 155.1319 ns/op
WorkloadActual   6: 4096000 op, 649292931.00 ns, 158.5188 ns/op
WorkloadActual   7: 4096000 op, 644388915.00 ns, 157.3215 ns/op
WorkloadActual   8: 4096000 op, 643355655.00 ns, 157.0693 ns/op
WorkloadActual   9: 4096000 op, 639984190.00 ns, 156.2461 ns/op
WorkloadActual  10: 4096000 op, 639322348.00 ns, 156.0846 ns/op
WorkloadActual  11: 4096000 op, 638390992.00 ns, 155.8572 ns/op
WorkloadActual  12: 4096000 op, 636299401.00 ns, 155.3465 ns/op
WorkloadActual  13: 4096000 op, 639110808.00 ns, 156.0329 ns/op
WorkloadActual  14: 4096000 op, 637549964.00 ns, 155.6518 ns/op
WorkloadActual  15: 4096000 op, 640493872.00 ns, 156.3706 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 643674588.50 ns, 157.1471 ns/op
WorkloadResult   2: 4096000 op, 636296267.50 ns, 155.3458 ns/op
WorkloadResult   3: 4096000 op, 632659019.50 ns, 154.4578 ns/op
WorkloadResult   4: 4096000 op, 633276950.50 ns, 154.6086 ns/op
WorkloadResult   5: 4096000 op, 635410251.50 ns, 155.1295 ns/op
WorkloadResult   6: 4096000 op, 649282750.50 ns, 158.5163 ns/op
WorkloadResult   7: 4096000 op, 644378734.50 ns, 157.3190 ns/op
WorkloadResult   8: 4096000 op, 643345474.50 ns, 157.0668 ns/op
WorkloadResult   9: 4096000 op, 639974009.50 ns, 156.2437 ns/op
WorkloadResult  10: 4096000 op, 639312167.50 ns, 156.0821 ns/op
WorkloadResult  11: 4096000 op, 638380811.50 ns, 155.8547 ns/op
WorkloadResult  12: 4096000 op, 636289220.50 ns, 155.3440 ns/op
WorkloadResult  13: 4096000 op, 639100627.50 ns, 156.0304 ns/op
WorkloadResult  14: 4096000 op, 637539783.50 ns, 155.6494 ns/op
WorkloadResult  15: 4096000 op, 640483691.50 ns, 156.3681 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4432 has exited with code 0.

Mean = 156.078 ns, StdErr = 0.282 ns (0.18%), N = 15, StdDev = 1.093 ns
Min = 154.458 ns, Q1 = 155.345 ns, Median = 156.030 ns, Q3 = 156.717 ns, Max = 158.516 ns
IQR = 1.373 ns, LowerFence = 153.286 ns, UpperFence = 158.776 ns
ConfidenceInterval = [154.910 ns; 157.246 ns] (CI 99.9%), Margin = 1.168 ns (0.75% of Mean)
Skewness = 0.49, Kurtosis = 2.48, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-04 20:18 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 188445.00 ns, 188.4450 ns/op
WorkloadJitting  1: 1000 op, 1019383.00 ns, 1.0194 us/op

OverheadJitting  2: 16000 op, 195807.00 ns, 12.2379 ns/op
WorkloadJitting  2: 16000 op, 6297235.00 ns, 393.5772 ns/op

WorkloadPilot    1: 16000 op, 5005629.00 ns, 312.8518 ns/op
WorkloadPilot    2: 32000 op, 9214281.00 ns, 287.9463 ns/op
WorkloadPilot    3: 64000 op, 18327269.00 ns, 286.3636 ns/op
WorkloadPilot    4: 128000 op, 36846768.00 ns, 287.8654 ns/op
WorkloadPilot    5: 256000 op, 80701116.00 ns, 315.2387 ns/op
WorkloadPilot    6: 512000 op, 35745621.00 ns, 69.8157 ns/op
WorkloadPilot    7: 1024000 op, 68673174.00 ns, 67.0636 ns/op
WorkloadPilot    8: 2048000 op, 135716712.00 ns, 66.2679 ns/op
WorkloadPilot    9: 4096000 op, 271913211.00 ns, 66.3851 ns/op
WorkloadPilot   10: 8192000 op, 542938568.00 ns, 66.2767 ns/op

OverheadWarmup   1: 8192000 op, 38749.00 ns, 0.0047 ns/op
OverheadWarmup   2: 8192000 op, 34562.00 ns, 0.0042 ns/op
OverheadWarmup   3: 8192000 op, 34372.00 ns, 0.0042 ns/op
OverheadWarmup   4: 8192000 op, 30987.00 ns, 0.0038 ns/op
OverheadWarmup   5: 8192000 op, 20992.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 31057.00 ns, 0.0038 ns/op
OverheadWarmup   7: 8192000 op, 31097.00 ns, 0.0038 ns/op
OverheadWarmup   8: 8192000 op, 31247.00 ns, 0.0038 ns/op
OverheadWarmup   9: 8192000 op, 34442.00 ns, 0.0042 ns/op
OverheadWarmup  10: 8192000 op, 28443.00 ns, 0.0035 ns/op

OverheadActual   1: 8192000 op, 32409.00 ns, 0.0040 ns/op
OverheadActual   2: 8192000 op, 28843.00 ns, 0.0035 ns/op
OverheadActual   3: 8192000 op, 29204.00 ns, 0.0036 ns/op
OverheadActual   4: 8192000 op, 31868.00 ns, 0.0039 ns/op
OverheadActual   5: 8192000 op, 31168.00 ns, 0.0038 ns/op
OverheadActual   6: 8192000 op, 31638.00 ns, 0.0039 ns/op
OverheadActual   7: 8192000 op, 34792.00 ns, 0.0042 ns/op
OverheadActual   8: 8192000 op, 33781.00 ns, 0.0041 ns/op
OverheadActual   9: 8192000 op, 33971.00 ns, 0.0041 ns/op
OverheadActual  10: 8192000 op, 30867.00 ns, 0.0038 ns/op
OverheadActual  11: 8192000 op, 30376.00 ns, 0.0037 ns/op
OverheadActual  12: 8192000 op, 31027.00 ns, 0.0038 ns/op
OverheadActual  13: 8192000 op, 32219.00 ns, 0.0039 ns/op
OverheadActual  14: 8192000 op, 31097.00 ns, 0.0038 ns/op
OverheadActual  15: 8192000 op, 31798.00 ns, 0.0039 ns/op
OverheadActual  16: 8192000 op, 31919.00 ns, 0.0039 ns/op

WorkloadWarmup   1: 8192000 op, 555570914.00 ns, 67.8187 ns/op
WorkloadWarmup   2: 8192000 op, 552225060.00 ns, 67.4103 ns/op
WorkloadWarmup   3: 8192000 op, 547505611.00 ns, 66.8342 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 545646090.00 ns, 66.6072 ns/op
WorkloadActual   2: 8192000 op, 542602887.00 ns, 66.2357 ns/op
WorkloadActual   3: 8192000 op, 543262446.00 ns, 66.3162 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 545614372.00 ns, 66.6033 ns/op
WorkloadResult   2: 8192000 op, 542571169.00 ns, 66.2318 ns/op
WorkloadResult   3: 8192000 op, 543230728.00 ns, 66.3123 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4447 has exited with code 0.

Mean = 66.382 ns, StdErr = 0.113 ns (0.17%), N = 3, StdDev = 0.195 ns
Min = 66.232 ns, Q1 = 66.272 ns, Median = 66.312 ns, Q3 = 66.458 ns, Max = 66.603 ns
IQR = 0.186 ns, LowerFence = 65.993 ns, UpperFence = 66.736 ns
ConfidenceInterval = [62.817 ns; 69.948 ns] (CI 99.9%), Margin = 3.565 ns (5.37% of Mean)
Skewness = 0.31, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-04 20:17 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 190168.00 ns, 190.1680 ns/op
WorkloadJitting  1: 1000 op, 1240779.00 ns, 1.2408 us/op

OverheadJitting  2: 16000 op, 213924.00 ns, 13.3703 ns/op
WorkloadJitting  2: 16000 op, 11077054.00 ns, 692.3159 ns/op

WorkloadPilot    1: 16000 op, 9375126.00 ns, 585.9454 ns/op
WorkloadPilot    2: 32000 op, 17232435.00 ns, 538.5136 ns/op
WorkloadPilot    3: 64000 op, 29090050.00 ns, 454.5320 ns/op
WorkloadPilot    4: 128000 op, 51127079.00 ns, 399.4303 ns/op
WorkloadPilot    5: 256000 op, 70201257.00 ns, 274.2237 ns/op
WorkloadPilot    6: 512000 op, 56692777.00 ns, 110.7281 ns/op
WorkloadPilot    7: 1024000 op, 113411413.00 ns, 110.7533 ns/op
WorkloadPilot    8: 2048000 op, 226794875.00 ns, 110.7397 ns/op
WorkloadPilot    9: 4096000 op, 452287497.00 ns, 110.4218 ns/op
WorkloadPilot   10: 8192000 op, 909642317.00 ns, 111.0403 ns/op

OverheadWarmup   1: 8192000 op, 26360.00 ns, 0.0032 ns/op
OverheadWarmup   2: 8192000 op, 21042.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 20971.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 20981.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 21062.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 20991.00 ns, 0.0026 ns/op
OverheadWarmup   7: 8192000 op, 21012.00 ns, 0.0026 ns/op
OverheadWarmup   8: 8192000 op, 20982.00 ns, 0.0026 ns/op

OverheadActual   1: 8192000 op, 24677.00 ns, 0.0030 ns/op
OverheadActual   2: 8192000 op, 21062.00 ns, 0.0026 ns/op
OverheadActual   3: 8192000 op, 21012.00 ns, 0.0026 ns/op
OverheadActual   4: 8192000 op, 20992.00 ns, 0.0026 ns/op
OverheadActual   5: 8192000 op, 21052.00 ns, 0.0026 ns/op
OverheadActual   6: 8192000 op, 20992.00 ns, 0.0026 ns/op
OverheadActual   7: 8192000 op, 20991.00 ns, 0.0026 ns/op
OverheadActual   8: 8192000 op, 21182.00 ns, 0.0026 ns/op
OverheadActual   9: 8192000 op, 24658.00 ns, 0.0030 ns/op
OverheadActual  10: 8192000 op, 21012.00 ns, 0.0026 ns/op
OverheadActual  11: 8192000 op, 20941.00 ns, 0.0026 ns/op
OverheadActual  12: 8192000 op, 21042.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 21032.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 21002.00 ns, 0.0026 ns/op
OverheadActual  15: 8192000 op, 21042.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 923569386.00 ns, 112.7404 ns/op
WorkloadWarmup   2: 8192000 op, 920702986.00 ns, 112.3905 ns/op
WorkloadWarmup   3: 8192000 op, 908576282.00 ns, 110.9102 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 911317570.00 ns, 111.2448 ns/op
WorkloadActual   2: 8192000 op, 910625272.00 ns, 111.1603 ns/op
WorkloadActual   3: 8192000 op, 906841292.00 ns, 110.6984 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 911296538.00 ns, 111.2423 ns/op
WorkloadResult   2: 8192000 op, 910604240.00 ns, 111.1577 ns/op
WorkloadResult   3: 8192000 op, 906820260.00 ns, 110.6958 ns/op
// GC:  129 0 0 2162688000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4455 has exited with code 0.

Mean = 111.032 ns, StdErr = 0.170 ns (0.15%), N = 3, StdDev = 0.294 ns
Min = 110.696 ns, Q1 = 110.927 ns, Median = 111.158 ns, Q3 = 111.200 ns, Max = 111.242 ns
IQR = 0.273 ns, LowerFence = 110.517 ns, UpperFence = 111.610 ns
ConfidenceInterval = [105.666 ns; 116.398 ns] (CI 99.9%), Margin = 5.366 ns (4.83% of Mean)
Skewness = -0.35, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-04 20:17 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 182596.00 ns, 182.5960 ns/op
WorkloadJitting  1: 1000 op, 1177863.00 ns, 1.1779 us/op

OverheadJitting  2: 16000 op, 233444.00 ns, 14.5903 ns/op
WorkloadJitting  2: 16000 op, 9277518.00 ns, 579.8449 ns/op

WorkloadPilot    1: 16000 op, 7297861.00 ns, 456.1163 ns/op
WorkloadPilot    2: 32000 op, 13249141.00 ns, 414.0357 ns/op
WorkloadPilot    3: 64000 op, 26101482.00 ns, 407.8357 ns/op
WorkloadPilot    4: 128000 op, 50918981.00 ns, 397.8045 ns/op
WorkloadPilot    5: 256000 op, 57761943.00 ns, 225.6326 ns/op
WorkloadPilot    6: 512000 op, 38562682.00 ns, 75.3177 ns/op
WorkloadPilot    7: 1024000 op, 74509227.00 ns, 72.7629 ns/op
WorkloadPilot    8: 2048000 op, 149741549.00 ns, 73.1160 ns/op
WorkloadPilot    9: 4096000 op, 299993451.00 ns, 73.2406 ns/op
WorkloadPilot   10: 8192000 op, 595545367.00 ns, 72.6984 ns/op

OverheadWarmup   1: 8192000 op, 36706.00 ns, 0.0045 ns/op
OverheadWarmup   2: 8192000 op, 34221.00 ns, 0.0042 ns/op
OverheadWarmup   3: 8192000 op, 34342.00 ns, 0.0042 ns/op
OverheadWarmup   4: 8192000 op, 52459.00 ns, 0.0064 ns/op
OverheadWarmup   5: 8192000 op, 34472.00 ns, 0.0042 ns/op
OverheadWarmup   6: 8192000 op, 34332.00 ns, 0.0042 ns/op
OverheadWarmup   7: 8192000 op, 28934.00 ns, 0.0035 ns/op
OverheadWarmup   8: 8192000 op, 34252.00 ns, 0.0042 ns/op
OverheadWarmup   9: 8192000 op, 35344.00 ns, 0.0043 ns/op
OverheadWarmup  10: 8192000 op, 34092.00 ns, 0.0042 ns/op

OverheadActual   1: 8192000 op, 45189.00 ns, 0.0055 ns/op
OverheadActual   2: 8192000 op, 32119.00 ns, 0.0039 ns/op
OverheadActual   3: 8192000 op, 32319.00 ns, 0.0039 ns/op
OverheadActual   4: 8192000 op, 31698.00 ns, 0.0039 ns/op
OverheadActual   5: 8192000 op, 31157.00 ns, 0.0038 ns/op
OverheadActual   6: 8192000 op, 31487.00 ns, 0.0038 ns/op
OverheadActual   7: 8192000 op, 35273.00 ns, 0.0043 ns/op
OverheadActual   8: 8192000 op, 34743.00 ns, 0.0042 ns/op
OverheadActual   9: 8192000 op, 34332.00 ns, 0.0042 ns/op
OverheadActual  10: 8192000 op, 34362.00 ns, 0.0042 ns/op
OverheadActual  11: 8192000 op, 34362.00 ns, 0.0042 ns/op
OverheadActual  12: 8192000 op, 36866.00 ns, 0.0045 ns/op
OverheadActual  13: 8192000 op, 32128.00 ns, 0.0039 ns/op
OverheadActual  14: 8192000 op, 31127.00 ns, 0.0038 ns/op
OverheadActual  15: 8192000 op, 32088.00 ns, 0.0039 ns/op
OverheadActual  16: 8192000 op, 31217.00 ns, 0.0038 ns/op
OverheadActual  17: 8192000 op, 30957.00 ns, 0.0038 ns/op
OverheadActual  18: 8192000 op, 31587.00 ns, 0.0039 ns/op
OverheadActual  19: 8192000 op, 24096.00 ns, 0.0029 ns/op
OverheadActual  20: 8192000 op, 24026.00 ns, 0.0029 ns/op

WorkloadWarmup   1: 8192000 op, 608752455.00 ns, 74.3106 ns/op
WorkloadWarmup   2: 8192000 op, 605738241.00 ns, 73.9427 ns/op
WorkloadWarmup   3: 8192000 op, 597068042.00 ns, 72.8843 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 599340443.00 ns, 73.1617 ns/op
WorkloadActual   2: 8192000 op, 593294510.00 ns, 72.4236 ns/op
WorkloadActual   3: 8192000 op, 593249107.00 ns, 72.4181 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 599308339.50 ns, 73.1578 ns/op
WorkloadResult   2: 8192000 op, 593262406.50 ns, 72.4197 ns/op
WorkloadResult   3: 8192000 op, 593217003.50 ns, 72.4142 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4469 has exited with code 0.

Mean = 72.664 ns, StdErr = 0.247 ns (0.34%), N = 3, StdDev = 0.428 ns
Min = 72.414 ns, Q1 = 72.417 ns, Median = 72.420 ns, Q3 = 72.789 ns, Max = 73.158 ns
IQR = 0.372 ns, LowerFence = 71.859 ns, UpperFence = 73.346 ns
ConfidenceInterval = [64.861 ns; 80.467 ns] (CI 99.9%), Margin = 7.803 ns (10.74% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-04 20:17 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 188456.00 ns, 188.4560 ns/op
WorkloadJitting  1: 1000 op, 1512339.00 ns, 1.5123 us/op

OverheadJitting  2: 16000 op, 225562.00 ns, 14.0976 ns/op
WorkloadJitting  2: 16000 op, 15978285.00 ns, 998.6428 ns/op

WorkloadPilot    1: 16000 op, 14446976.00 ns, 902.9360 ns/op
WorkloadPilot    2: 32000 op, 25824167.00 ns, 807.0052 ns/op
WorkloadPilot    3: 64000 op, 50760499.00 ns, 793.1328 ns/op
WorkloadPilot    4: 128000 op, 72531918.00 ns, 566.6556 ns/op
WorkloadPilot    5: 256000 op, 45368800.00 ns, 177.2219 ns/op
WorkloadPilot    6: 512000 op, 77729239.00 ns, 151.8149 ns/op
WorkloadPilot    7: 1024000 op, 155347731.00 ns, 151.7068 ns/op
WorkloadPilot    8: 2048000 op, 309803801.00 ns, 151.2714 ns/op
WorkloadPilot    9: 4096000 op, 619487430.00 ns, 151.2420 ns/op

OverheadWarmup   1: 4096000 op, 14662.00 ns, 0.0036 ns/op
OverheadWarmup   2: 4096000 op, 15924.00 ns, 0.0039 ns/op
OverheadWarmup   3: 4096000 op, 16344.00 ns, 0.0040 ns/op
OverheadWarmup   4: 4096000 op, 16174.00 ns, 0.0039 ns/op
OverheadWarmup   5: 4096000 op, 16275.00 ns, 0.0040 ns/op
OverheadWarmup   6: 4096000 op, 16235.00 ns, 0.0040 ns/op

OverheadActual   1: 4096000 op, 16145.00 ns, 0.0039 ns/op
OverheadActual   2: 4096000 op, 16325.00 ns, 0.0040 ns/op
OverheadActual   3: 4096000 op, 16385.00 ns, 0.0040 ns/op
OverheadActual   4: 4096000 op, 16224.00 ns, 0.0040 ns/op
OverheadActual   5: 4096000 op, 16215.00 ns, 0.0040 ns/op
OverheadActual   6: 4096000 op, 16165.00 ns, 0.0039 ns/op
OverheadActual   7: 4096000 op, 16304.00 ns, 0.0040 ns/op
OverheadActual   8: 4096000 op, 16135.00 ns, 0.0039 ns/op
OverheadActual   9: 4096000 op, 16204.00 ns, 0.0040 ns/op
OverheadActual  10: 4096000 op, 16255.00 ns, 0.0040 ns/op
OverheadActual  11: 4096000 op, 20230.00 ns, 0.0049 ns/op
OverheadActual  12: 4096000 op, 16245.00 ns, 0.0040 ns/op
OverheadActual  13: 4096000 op, 16365.00 ns, 0.0040 ns/op
OverheadActual  14: 4096000 op, 16014.00 ns, 0.0039 ns/op
OverheadActual  15: 4096000 op, 16054.00 ns, 0.0039 ns/op

WorkloadWarmup   1: 4096000 op, 631813697.00 ns, 154.2514 ns/op
WorkloadWarmup   2: 4096000 op, 629095603.00 ns, 153.5878 ns/op
WorkloadWarmup   3: 4096000 op, 621303450.00 ns, 151.6854 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 628971265.00 ns, 153.5574 ns/op
WorkloadActual   2: 4096000 op, 621421709.00 ns, 151.7143 ns/op
WorkloadActual   3: 4096000 op, 623332181.00 ns, 152.1807 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 628955041.00 ns, 153.5535 ns/op
WorkloadResult   2: 4096000 op, 621405485.00 ns, 151.7103 ns/op
WorkloadResult   3: 4096000 op, 623315957.00 ns, 152.1767 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4480 has exited with code 0.

Mean = 152.480 ns, StdErr = 0.553 ns (0.36%), N = 3, StdDev = 0.958 ns
Min = 151.710 ns, Q1 = 151.944 ns, Median = 152.177 ns, Q3 = 152.865 ns, Max = 153.553 ns
IQR = 0.922 ns, LowerFence = 150.561 ns, UpperFence = 154.247 ns
ConfidenceInterval = [134.997 ns; 169.963 ns] (CI 99.9%), Margin = 17.483 ns (11.47% of Mean)
Skewness = 0.28, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-04 20:17 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 66.966 ns, StdErr = 0.100 ns (0.15%), N = 14, StdDev = 0.373 ns
Min = 66.713 ns, Q1 = 66.749 ns, Median = 66.788 ns, Q3 = 66.950 ns, Max = 67.825 ns
IQR = 0.201 ns, LowerFence = 66.447 ns, UpperFence = 67.252 ns
ConfidenceInterval = [66.545 ns; 67.386 ns] (CI 99.9%), Margin = 0.420 ns (0.63% of Mean)
Skewness = 1.32, Kurtosis = 3.06, MValue = 2
-------------------- Histogram --------------------
[66.510 ns ; 68.028 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 111.226 ns, StdErr = 0.091 ns (0.08%), N = 15, StdDev = 0.351 ns
Min = 110.804 ns, Q1 = 110.975 ns, Median = 111.050 ns, Q3 = 111.542 ns, Max = 111.974 ns
IQR = 0.567 ns, LowerFence = 110.124 ns, UpperFence = 112.392 ns
ConfidenceInterval = [110.852 ns; 111.601 ns] (CI 99.9%), Margin = 0.375 ns (0.34% of Mean)
Skewness = 0.64, Kurtosis = 1.97, MValue = 2
-------------------- Histogram --------------------
[110.617 ns ; 112.161 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 71.213 ns, StdErr = 0.128 ns (0.18%), N = 14, StdDev = 0.477 ns
Min = 70.730 ns, Q1 = 70.960 ns, Median = 71.035 ns, Q3 = 71.275 ns, Max = 72.374 ns
IQR = 0.314 ns, LowerFence = 70.489 ns, UpperFence = 71.746 ns
ConfidenceInterval = [70.674 ns; 71.751 ns] (CI 99.9%), Margin = 0.538 ns (0.76% of Mean)
Skewness = 1.17, Kurtosis = 3.13, MValue = 2
-------------------- Histogram --------------------
[70.470 ns ; 72.634 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 156.078 ns, StdErr = 0.282 ns (0.18%), N = 15, StdDev = 1.093 ns
Min = 154.458 ns, Q1 = 155.345 ns, Median = 156.030 ns, Q3 = 156.717 ns, Max = 158.516 ns
IQR = 1.373 ns, LowerFence = 153.286 ns, UpperFence = 158.776 ns
ConfidenceInterval = [154.910 ns; 157.246 ns] (CI 99.9%), Margin = 1.168 ns (0.75% of Mean)
Skewness = 0.49, Kurtosis = 2.48, MValue = 2
-------------------- Histogram --------------------
[154.298 ns ; 159.098 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 66.382 ns, StdErr = 0.113 ns (0.17%), N = 3, StdDev = 0.195 ns
Min = 66.232 ns, Q1 = 66.272 ns, Median = 66.312 ns, Q3 = 66.458 ns, Max = 66.603 ns
IQR = 0.186 ns, LowerFence = 65.993 ns, UpperFence = 66.736 ns
ConfidenceInterval = [62.817 ns; 69.948 ns] (CI 99.9%), Margin = 3.565 ns (5.37% of Mean)
Skewness = 0.31, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[66.054 ns ; 66.781 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 111.032 ns, StdErr = 0.170 ns (0.15%), N = 3, StdDev = 0.294 ns
Min = 110.696 ns, Q1 = 110.927 ns, Median = 111.158 ns, Q3 = 111.200 ns, Max = 111.242 ns
IQR = 0.273 ns, LowerFence = 110.517 ns, UpperFence = 111.610 ns
ConfidenceInterval = [105.666 ns; 116.398 ns] (CI 99.9%), Margin = 5.366 ns (4.83% of Mean)
Skewness = -0.35, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[110.428 ns ; 111.510 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 72.664 ns, StdErr = 0.247 ns (0.34%), N = 3, StdDev = 0.428 ns
Min = 72.414 ns, Q1 = 72.417 ns, Median = 72.420 ns, Q3 = 72.789 ns, Max = 73.158 ns
IQR = 0.372 ns, LowerFence = 71.859 ns, UpperFence = 73.346 ns
ConfidenceInterval = [64.861 ns; 80.467 ns] (CI 99.9%), Margin = 7.803 ns (10.74% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[72.397 ns ; 73.175 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 152.480 ns, StdErr = 0.553 ns (0.36%), N = 3, StdDev = 0.958 ns
Min = 151.710 ns, Q1 = 151.944 ns, Median = 152.177 ns, Q3 = 152.865 ns, Max = 153.553 ns
IQR = 0.922 ns, LowerFence = 150.561 ns, UpperFence = 154.247 ns
ConfidenceInterval = [134.997 ns; 169.963 ns] (CI 99.9%), Margin = 17.483 ns (11.47% of Mean)
Skewness = 0.28, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[150.838 ns ; 154.426 ns) | @@@
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
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  66.97 ns |  0.420 ns | 0.373 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 111.23 ns |  0.375 ns | 0.351 ns | 0.0157 |     264 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  71.21 ns |  0.538 ns | 0.477 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 156.08 ns |  1.168 ns | 1.093 ns | 0.0115 |     192 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  66.38 ns |  3.565 ns | 0.195 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 111.03 ns |  5.366 ns | 0.294 ns | 0.0157 |     264 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  72.66 ns |  7.803 ns | 0.428 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 152.48 ns | 17.483 ns | 0.958 ns | 0.0115 |     192 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput    -> 1 outlier  was  removed (67.87 ns)
  CoreDispatchBenchmarks.'Request  Request': RunStrategy=Throughput -> 1 outlier  was  removed (74.91 ns)
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
Run time: 00:01:41 (101.17 sec), executed benchmarks: 8

Global total time: 00:01:55 (115.85 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
