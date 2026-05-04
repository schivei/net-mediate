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


---


---

## Latest CI Benchmark Run

Run: 2026-05-04 10:30 UTC | Branch: feature/aot | Commit: ec173eb

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean     | Error    | StdDev  | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |---------:|---------:|--------:|-------:|----------:|
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 146.8 ns |  0.59 ns | 0.53 ns | 0.0115 |     192 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 231.5 ns |  0.52 ns | 0.46 ns | 0.0256 |     432 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 216.2 ns |  0.69 ns | 0.65 ns | 0.0156 |     264 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 275.3 ns |  5.28 ns | 5.65 ns | 0.0215 |     360 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           | 149.0 ns |  3.26 ns | 0.18 ns | 0.0115 |     192 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 222.1 ns | 18.72 ns | 1.03 ns | 0.0256 |     432 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           | 201.8 ns | 13.85 ns | 0.76 ns | 0.0156 |     264 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 287.9 ns | 61.09 ns | 3.35 ns | 0.0215 |     360 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.87 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 11.99 sec and exited with 0
// ***** Done, took 00:00:13 (13.92 sec)   *****
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

OverheadJitting  1: 1000 op, 178072.00 ns, 178.0720 ns/op
WorkloadJitting  1: 1000 op, 1821266.00 ns, 1.8213 us/op

OverheadJitting  2: 16000 op, 179074.00 ns, 11.1921 ns/op
WorkloadJitting  2: 16000 op, 21071756.00 ns, 1.3170 us/op

WorkloadPilot    1: 16000 op, 19589012.00 ns, 1.2243 us/op
WorkloadPilot    2: 32000 op, 37064421.00 ns, 1.1583 us/op
WorkloadPilot    3: 64000 op, 71713327.00 ns, 1.1205 us/op
WorkloadPilot    4: 128000 op, 151948336.00 ns, 1.1871 us/op
WorkloadPilot    5: 256000 op, 181776192.00 ns, 710.0633 ns/op
WorkloadPilot    6: 512000 op, 75261474.00 ns, 146.9951 ns/op
WorkloadPilot    7: 1024000 op, 149829415.00 ns, 146.3178 ns/op
WorkloadPilot    8: 2048000 op, 300002051.00 ns, 146.4854 ns/op
WorkloadPilot    9: 4096000 op, 603304103.00 ns, 147.2910 ns/op

OverheadWarmup   1: 4096000 op, 14106.00 ns, 0.0034 ns/op
OverheadWarmup   2: 4096000 op, 10871.00 ns, 0.0027 ns/op
OverheadWarmup   3: 4096000 op, 10840.00 ns, 0.0026 ns/op
OverheadWarmup   4: 4096000 op, 10831.00 ns, 0.0026 ns/op
OverheadWarmup   5: 4096000 op, 22071.00 ns, 0.0054 ns/op
OverheadWarmup   6: 4096000 op, 22022.00 ns, 0.0054 ns/op
OverheadWarmup   7: 4096000 op, 22012.00 ns, 0.0054 ns/op
OverheadWarmup   8: 4096000 op, 22211.00 ns, 0.0054 ns/op
OverheadWarmup   9: 4096000 op, 22061.00 ns, 0.0054 ns/op

OverheadActual   1: 4096000 op, 24916.00 ns, 0.0061 ns/op
OverheadActual   2: 4096000 op, 22062.00 ns, 0.0054 ns/op
OverheadActual   3: 4096000 op, 21860.00 ns, 0.0053 ns/op
OverheadActual   4: 4096000 op, 22331.00 ns, 0.0055 ns/op
OverheadActual   5: 4096000 op, 22182.00 ns, 0.0054 ns/op
OverheadActual   6: 4096000 op, 22041.00 ns, 0.0054 ns/op
OverheadActual   7: 4096000 op, 22191.00 ns, 0.0054 ns/op
OverheadActual   8: 4096000 op, 26149.00 ns, 0.0064 ns/op
OverheadActual   9: 4096000 op, 21991.00 ns, 0.0054 ns/op
OverheadActual  10: 4096000 op, 21760.00 ns, 0.0053 ns/op
OverheadActual  11: 4096000 op, 22171.00 ns, 0.0054 ns/op
OverheadActual  12: 4096000 op, 22051.00 ns, 0.0054 ns/op
OverheadActual  13: 4096000 op, 22101.00 ns, 0.0054 ns/op
OverheadActual  14: 4096000 op, 22001.00 ns, 0.0054 ns/op
OverheadActual  15: 4096000 op, 43330.00 ns, 0.0106 ns/op

WorkloadWarmup   1: 4096000 op, 622878657.00 ns, 152.0700 ns/op
WorkloadWarmup   2: 4096000 op, 606393214.00 ns, 148.0452 ns/op
WorkloadWarmup   3: 4096000 op, 600465019.00 ns, 146.5979 ns/op
WorkloadWarmup   4: 4096000 op, 603218993.00 ns, 147.2703 ns/op
WorkloadWarmup   5: 4096000 op, 606283699.00 ns, 148.0185 ns/op
WorkloadWarmup   6: 4096000 op, 602739119.00 ns, 147.1531 ns/op
WorkloadWarmup   7: 4096000 op, 602903946.00 ns, 147.1933 ns/op
WorkloadWarmup   8: 4096000 op, 599769220.00 ns, 146.4280 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 606046877.00 ns, 147.9607 ns/op
WorkloadActual   2: 4096000 op, 602579030.00 ns, 147.1140 ns/op
WorkloadActual   3: 4096000 op, 600490024.00 ns, 146.6040 ns/op
WorkloadActual   4: 4096000 op, 598989666.00 ns, 146.2377 ns/op
WorkloadActual   5: 4096000 op, 604075511.00 ns, 147.4794 ns/op
WorkloadActual   6: 4096000 op, 602245338.00 ns, 147.0326 ns/op
WorkloadActual   7: 4096000 op, 601994289.00 ns, 146.9713 ns/op
WorkloadActual   8: 4096000 op, 600140663.00 ns, 146.5187 ns/op
WorkloadActual   9: 4096000 op, 599022151.00 ns, 146.2456 ns/op
WorkloadActual  10: 4096000 op, 598847241.00 ns, 146.2029 ns/op
WorkloadActual  11: 4096000 op, 598387434.00 ns, 146.0907 ns/op
WorkloadActual  12: 4096000 op, 601456807.00 ns, 146.8400 ns/op
WorkloadActual  13: 4096000 op, 600693755.00 ns, 146.6537 ns/op
WorkloadActual  14: 4096000 op, 600687996.00 ns, 146.6523 ns/op
WorkloadActual  15: 4096000 op, 618803686.00 ns, 151.0751 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 606024776.00 ns, 147.9553 ns/op
WorkloadResult   2: 4096000 op, 602556929.00 ns, 147.1086 ns/op
WorkloadResult   3: 4096000 op, 600467923.00 ns, 146.5986 ns/op
WorkloadResult   4: 4096000 op, 598967565.00 ns, 146.2323 ns/op
WorkloadResult   5: 4096000 op, 604053410.00 ns, 147.4740 ns/op
WorkloadResult   6: 4096000 op, 602223237.00 ns, 147.0272 ns/op
WorkloadResult   7: 4096000 op, 601972188.00 ns, 146.9659 ns/op
WorkloadResult   8: 4096000 op, 600118562.00 ns, 146.5133 ns/op
WorkloadResult   9: 4096000 op, 599000050.00 ns, 146.2402 ns/op
WorkloadResult  10: 4096000 op, 598825140.00 ns, 146.1975 ns/op
WorkloadResult  11: 4096000 op, 598365333.00 ns, 146.0853 ns/op
WorkloadResult  12: 4096000 op, 601434706.00 ns, 146.8346 ns/op
WorkloadResult  13: 4096000 op, 600671654.00 ns, 146.6484 ns/op
WorkloadResult  14: 4096000 op, 600665895.00 ns, 146.6469 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4507 has exited with code 0.

Mean = 146.752 ns, StdErr = 0.141 ns (0.10%), N = 14, StdDev = 0.527 ns
Min = 146.085 ns, Q1 = 146.309 ns, Median = 146.648 ns, Q3 = 147.012 ns, Max = 147.955 ns
IQR = 0.703 ns, LowerFence = 145.254 ns, UpperFence = 148.067 ns
ConfidenceInterval = [146.158 ns; 147.347 ns] (CI 99.9%), Margin = 0.595 ns (0.41% of Mean)
Skewness = 0.7, Kurtosis = 2.62, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-04 10:30 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 171329.00 ns, 171.3290 ns/op
WorkloadJitting  1: 1000 op, 2073887.00 ns, 2.0739 us/op

OverheadJitting  2: 16000 op, 196226.00 ns, 12.2641 ns/op
WorkloadJitting  2: 16000 op, 25300832.00 ns, 1.5813 us/op

WorkloadPilot    1: 16000 op, 21795284.00 ns, 1.3622 us/op
WorkloadPilot    2: 32000 op, 40573783.00 ns, 1.2679 us/op
WorkloadPilot    3: 64000 op, 70080106.00 ns, 1.0950 us/op
WorkloadPilot    4: 128000 op, 156419343.00 ns, 1.2220 us/op
WorkloadPilot    5: 256000 op, 118544303.00 ns, 463.0637 ns/op
WorkloadPilot    6: 512000 op, 117602758.00 ns, 229.6929 ns/op
WorkloadPilot    7: 1024000 op, 236076817.00 ns, 230.5438 ns/op
WorkloadPilot    8: 2048000 op, 469701310.00 ns, 229.3463 ns/op
WorkloadPilot    9: 4096000 op, 943084070.00 ns, 230.2451 ns/op

OverheadWarmup   1: 4096000 op, 14167.00 ns, 0.0035 ns/op
OverheadWarmup   2: 4096000 op, 10880.00 ns, 0.0027 ns/op
OverheadWarmup   3: 4096000 op, 10820.00 ns, 0.0026 ns/op
OverheadWarmup   4: 4096000 op, 10851.00 ns, 0.0026 ns/op
OverheadWarmup   5: 4096000 op, 10860.00 ns, 0.0027 ns/op
OverheadWarmup   6: 4096000 op, 10810.00 ns, 0.0026 ns/op
OverheadWarmup   7: 4096000 op, 10850.00 ns, 0.0026 ns/op
OverheadWarmup   8: 4096000 op, 10830.00 ns, 0.0026 ns/op

OverheadActual   1: 4096000 op, 10910.00 ns, 0.0027 ns/op
OverheadActual   2: 4096000 op, 10900.00 ns, 0.0027 ns/op
OverheadActual   3: 4096000 op, 10900.00 ns, 0.0027 ns/op
OverheadActual   4: 4096000 op, 10910.00 ns, 0.0027 ns/op
OverheadActual   5: 4096000 op, 10870.00 ns, 0.0027 ns/op
OverheadActual   6: 4096000 op, 10851.00 ns, 0.0026 ns/op
OverheadActual   7: 4096000 op, 10851.00 ns, 0.0026 ns/op
OverheadActual   8: 4096000 op, 10821.00 ns, 0.0026 ns/op
OverheadActual   9: 4096000 op, 12763.00 ns, 0.0031 ns/op
OverheadActual  10: 4096000 op, 10830.00 ns, 0.0026 ns/op
OverheadActual  11: 4096000 op, 10830.00 ns, 0.0026 ns/op
OverheadActual  12: 4096000 op, 10860.00 ns, 0.0027 ns/op
OverheadActual  13: 4096000 op, 10820.00 ns, 0.0026 ns/op
OverheadActual  14: 4096000 op, 10831.00 ns, 0.0026 ns/op
OverheadActual  15: 4096000 op, 10880.00 ns, 0.0027 ns/op

WorkloadWarmup   1: 4096000 op, 959785886.00 ns, 234.3227 ns/op
WorkloadWarmup   2: 4096000 op, 942472179.00 ns, 230.0957 ns/op
WorkloadWarmup   3: 4096000 op, 939379427.00 ns, 229.3407 ns/op
WorkloadWarmup   4: 4096000 op, 948514982.00 ns, 231.5710 ns/op
WorkloadWarmup   5: 4096000 op, 939765577.00 ns, 229.4350 ns/op
WorkloadWarmup   6: 4096000 op, 936386566.00 ns, 228.6100 ns/op
WorkloadWarmup   7: 4096000 op, 936745947.00 ns, 228.6977 ns/op
WorkloadWarmup   8: 4096000 op, 935923573.00 ns, 228.4970 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 947636043.00 ns, 231.3565 ns/op
WorkloadActual   2: 4096000 op, 946281342.00 ns, 231.0257 ns/op
WorkloadActual   3: 4096000 op, 945476712.00 ns, 230.8293 ns/op
WorkloadActual   4: 4096000 op, 947118012.00 ns, 231.2300 ns/op
WorkloadActual   5: 4096000 op, 947622934.00 ns, 231.3533 ns/op
WorkloadActual   6: 4096000 op, 947782121.00 ns, 231.3921 ns/op
WorkloadActual   7: 4096000 op, 950121052.00 ns, 231.9631 ns/op
WorkloadActual   8: 4096000 op, 959181388.00 ns, 234.1751 ns/op
WorkloadActual   9: 4096000 op, 948736791.00 ns, 231.6252 ns/op
WorkloadActual  10: 4096000 op, 949150523.00 ns, 231.7262 ns/op
WorkloadActual  11: 4096000 op, 946433010.00 ns, 231.0627 ns/op
WorkloadActual  12: 4096000 op, 952542013.00 ns, 232.5542 ns/op
WorkloadActual  13: 4096000 op, 948523829.00 ns, 231.5732 ns/op
WorkloadActual  14: 4096000 op, 949499659.00 ns, 231.8114 ns/op
WorkloadActual  15: 4096000 op, 950629767.00 ns, 232.0873 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 947625183.00 ns, 231.3538 ns/op
WorkloadResult   2: 4096000 op, 946270482.00 ns, 231.0231 ns/op
WorkloadResult   3: 4096000 op, 945465852.00 ns, 230.8266 ns/op
WorkloadResult   4: 4096000 op, 947107152.00 ns, 231.2273 ns/op
WorkloadResult   5: 4096000 op, 947612074.00 ns, 231.3506 ns/op
WorkloadResult   6: 4096000 op, 947771261.00 ns, 231.3895 ns/op
WorkloadResult   7: 4096000 op, 950110192.00 ns, 231.9605 ns/op
WorkloadResult   8: 4096000 op, 948725931.00 ns, 231.6225 ns/op
WorkloadResult   9: 4096000 op, 949139663.00 ns, 231.7236 ns/op
WorkloadResult  10: 4096000 op, 946422150.00 ns, 231.0601 ns/op
WorkloadResult  11: 4096000 op, 952531153.00 ns, 232.5516 ns/op
WorkloadResult  12: 4096000 op, 948512969.00 ns, 231.5705 ns/op
WorkloadResult  13: 4096000 op, 949488799.00 ns, 231.8088 ns/op
WorkloadResult  14: 4096000 op, 950618907.00 ns, 232.0847 ns/op
// GC:  105 0 0 1769472000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4519 has exited with code 0.

Mean = 231.540 ns, StdErr = 0.124 ns (0.05%), N = 14, StdDev = 0.465 ns
Min = 230.827 ns, Q1 = 231.258 ns, Median = 231.480 ns, Q3 = 231.787 ns, Max = 232.552 ns
IQR = 0.529 ns, LowerFence = 230.464 ns, UpperFence = 232.581 ns
ConfidenceInterval = [231.015 ns; 232.064 ns] (CI 99.9%), Margin = 0.524 ns (0.23% of Mean)
Skewness = 0.46, Kurtosis = 2.4, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-04 10:31 (0h 2m from now) **
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

OverheadJitting  1: 1000 op, 173113.00 ns, 173.1130 ns/op
WorkloadJitting  1: 1000 op, 1856051.00 ns, 1.8561 us/op

OverheadJitting  2: 16000 op, 189353.00 ns, 11.8346 ns/op
WorkloadJitting  2: 16000 op, 21215703.00 ns, 1.3260 us/op

WorkloadPilot    1: 16000 op, 23859874.00 ns, 1.4912 us/op
WorkloadPilot    2: 32000 op, 43993695.00 ns, 1.3748 us/op
WorkloadPilot    3: 64000 op, 65472404.00 ns, 1.0230 us/op
WorkloadPilot    4: 128000 op, 145649399.00 ns, 1.1379 us/op
WorkloadPilot    5: 256000 op, 119669263.00 ns, 467.4581 ns/op
WorkloadPilot    6: 512000 op, 111524693.00 ns, 217.8217 ns/op
WorkloadPilot    7: 1024000 op, 220849653.00 ns, 215.6735 ns/op
WorkloadPilot    8: 2048000 op, 438062344.00 ns, 213.8976 ns/op
WorkloadPilot    9: 4096000 op, 878780851.00 ns, 214.5461 ns/op

OverheadWarmup   1: 4096000 op, 13705.00 ns, 0.0033 ns/op
OverheadWarmup   2: 4096000 op, 10910.00 ns, 0.0027 ns/op
OverheadWarmup   3: 4096000 op, 10920.00 ns, 0.0027 ns/op
OverheadWarmup   4: 4096000 op, 10901.00 ns, 0.0027 ns/op
OverheadWarmup   5: 4096000 op, 10911.00 ns, 0.0027 ns/op
OverheadWarmup   6: 4096000 op, 10920.00 ns, 0.0027 ns/op
OverheadWarmup   7: 4096000 op, 10921.00 ns, 0.0027 ns/op
OverheadWarmup   8: 4096000 op, 10920.00 ns, 0.0027 ns/op

OverheadActual   1: 4096000 op, 10890.00 ns, 0.0027 ns/op
OverheadActual   2: 4096000 op, 10901.00 ns, 0.0027 ns/op
OverheadActual   3: 4096000 op, 10900.00 ns, 0.0027 ns/op
OverheadActual   4: 4096000 op, 10900.00 ns, 0.0027 ns/op
OverheadActual   5: 4096000 op, 10911.00 ns, 0.0027 ns/op
OverheadActual   6: 4096000 op, 10930.00 ns, 0.0027 ns/op
OverheadActual   7: 4096000 op, 10880.00 ns, 0.0027 ns/op
OverheadActual   8: 4096000 op, 10910.00 ns, 0.0027 ns/op
OverheadActual   9: 4096000 op, 12824.00 ns, 0.0031 ns/op
OverheadActual  10: 4096000 op, 10910.00 ns, 0.0027 ns/op
OverheadActual  11: 4096000 op, 10900.00 ns, 0.0027 ns/op
OverheadActual  12: 4096000 op, 10930.00 ns, 0.0027 ns/op
OverheadActual  13: 4096000 op, 10930.00 ns, 0.0027 ns/op
OverheadActual  14: 4096000 op, 10910.00 ns, 0.0027 ns/op
OverheadActual  15: 4096000 op, 10921.00 ns, 0.0027 ns/op

WorkloadWarmup   1: 4096000 op, 888478963.00 ns, 216.9138 ns/op
WorkloadWarmup   2: 4096000 op, 892886541.00 ns, 217.9899 ns/op
WorkloadWarmup   3: 4096000 op, 883212307.00 ns, 215.6280 ns/op
WorkloadWarmup   4: 4096000 op, 879006587.00 ns, 214.6012 ns/op
WorkloadWarmup   5: 4096000 op, 879689720.00 ns, 214.7680 ns/op
WorkloadWarmup   6: 4096000 op, 879489617.00 ns, 214.7191 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 884170711.00 ns, 215.8620 ns/op
WorkloadActual   2: 4096000 op, 885553500.00 ns, 216.1996 ns/op
WorkloadActual   3: 4096000 op, 885846156.00 ns, 216.2710 ns/op
WorkloadActual   4: 4096000 op, 890527605.00 ns, 217.4140 ns/op
WorkloadActual   5: 4096000 op, 882770051.00 ns, 215.5200 ns/op
WorkloadActual   6: 4096000 op, 887295329.00 ns, 216.6248 ns/op
WorkloadActual   7: 4096000 op, 882981846.00 ns, 215.5717 ns/op
WorkloadActual   8: 4096000 op, 880833410.00 ns, 215.0472 ns/op
WorkloadActual   9: 4096000 op, 884243068.00 ns, 215.8797 ns/op
WorkloadActual  10: 4096000 op, 887677932.00 ns, 216.7182 ns/op
WorkloadActual  11: 4096000 op, 888585253.00 ns, 216.9398 ns/op
WorkloadActual  12: 4096000 op, 888787580.00 ns, 216.9892 ns/op
WorkloadActual  13: 4096000 op, 883957879.00 ns, 215.8100 ns/op
WorkloadActual  14: 4096000 op, 887252315.00 ns, 216.6143 ns/op
WorkloadActual  15: 4096000 op, 884702748.00 ns, 215.9919 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 884159801.00 ns, 215.8593 ns/op
WorkloadResult   2: 4096000 op, 885542590.00 ns, 216.1969 ns/op
WorkloadResult   3: 4096000 op, 885835246.00 ns, 216.2684 ns/op
WorkloadResult   4: 4096000 op, 890516695.00 ns, 217.4113 ns/op
WorkloadResult   5: 4096000 op, 882759141.00 ns, 215.5174 ns/op
WorkloadResult   6: 4096000 op, 887284419.00 ns, 216.6222 ns/op
WorkloadResult   7: 4096000 op, 882970936.00 ns, 215.5691 ns/op
WorkloadResult   8: 4096000 op, 880822500.00 ns, 215.0446 ns/op
WorkloadResult   9: 4096000 op, 884232158.00 ns, 215.8770 ns/op
WorkloadResult  10: 4096000 op, 887667022.00 ns, 216.7156 ns/op
WorkloadResult  11: 4096000 op, 888574343.00 ns, 216.9371 ns/op
WorkloadResult  12: 4096000 op, 888776670.00 ns, 216.9865 ns/op
WorkloadResult  13: 4096000 op, 883946969.00 ns, 215.8074 ns/op
WorkloadResult  14: 4096000 op, 887241405.00 ns, 216.6117 ns/op
WorkloadResult  15: 4096000 op, 884691838.00 ns, 215.9892 ns/op
// GC:  64 0 0 1081344000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4536 has exited with code 0.

Mean = 216.228 ns, StdErr = 0.167 ns (0.08%), N = 15, StdDev = 0.647 ns
Min = 215.045 ns, Q1 = 215.833 ns, Median = 216.197 ns, Q3 = 216.669 ns, Max = 217.411 ns
IQR = 0.836 ns, LowerFence = 214.580 ns, UpperFence = 217.922 ns
ConfidenceInterval = [215.536 ns; 216.919 ns] (CI 99.9%), Margin = 0.691 ns (0.32% of Mean)
Skewness = 0.05, Kurtosis = 1.95, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-04 10:31 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 194503.00 ns, 194.5030 ns/op
WorkloadJitting  1: 1000 op, 2462135.00 ns, 2.4621 us/op

OverheadJitting  2: 16000 op, 228355.00 ns, 14.2722 ns/op
WorkloadJitting  2: 16000 op, 30813432.00 ns, 1.9258 us/op

WorkloadPilot    1: 16000 op, 26491212.00 ns, 1.6557 us/op
WorkloadPilot    2: 32000 op, 54126936.00 ns, 1.6915 us/op
WorkloadPilot    3: 64000 op, 95106648.00 ns, 1.4860 us/op
WorkloadPilot    4: 128000 op, 185567223.00 ns, 1.4497 us/op
WorkloadPilot    5: 256000 op, 70747437.00 ns, 276.3572 ns/op
WorkloadPilot    6: 512000 op, 139848896.00 ns, 273.1424 ns/op
WorkloadPilot    7: 1024000 op, 279436765.00 ns, 272.8875 ns/op
WorkloadPilot    8: 2048000 op, 558515353.00 ns, 272.7126 ns/op

OverheadWarmup   1: 2048000 op, 7975.00 ns, 0.0039 ns/op
OverheadWarmup   2: 2048000 op, 5771.00 ns, 0.0028 ns/op
OverheadWarmup   3: 2048000 op, 13706.00 ns, 0.0067 ns/op
OverheadWarmup   4: 2048000 op, 11441.00 ns, 0.0056 ns/op
OverheadWarmup   5: 2048000 op, 11441.00 ns, 0.0056 ns/op
OverheadWarmup   6: 2048000 op, 11341.00 ns, 0.0055 ns/op

OverheadActual   1: 2048000 op, 11401.00 ns, 0.0056 ns/op
OverheadActual   2: 2048000 op, 11422.00 ns, 0.0056 ns/op
OverheadActual   3: 2048000 op, 11601.00 ns, 0.0057 ns/op
OverheadActual   4: 2048000 op, 11512.00 ns, 0.0056 ns/op
OverheadActual   5: 2048000 op, 11571.00 ns, 0.0056 ns/op
OverheadActual   6: 2048000 op, 11080.00 ns, 0.0054 ns/op
OverheadActual   7: 2048000 op, 11391.00 ns, 0.0056 ns/op
OverheadActual   8: 2048000 op, 11351.00 ns, 0.0055 ns/op
OverheadActual   9: 2048000 op, 11391.00 ns, 0.0056 ns/op
OverheadActual  10: 2048000 op, 11311.00 ns, 0.0055 ns/op
OverheadActual  11: 2048000 op, 11471.00 ns, 0.0056 ns/op
OverheadActual  12: 2048000 op, 11191.00 ns, 0.0055 ns/op
OverheadActual  13: 2048000 op, 11111.00 ns, 0.0054 ns/op
OverheadActual  14: 2048000 op, 11091.00 ns, 0.0054 ns/op
OverheadActual  15: 2048000 op, 10760.00 ns, 0.0053 ns/op

WorkloadWarmup   1: 2048000 op, 578719404.00 ns, 282.5778 ns/op
WorkloadWarmup   2: 2048000 op, 570227583.00 ns, 278.4314 ns/op
WorkloadWarmup   3: 2048000 op, 558422589.00 ns, 272.6673 ns/op
WorkloadWarmup   4: 2048000 op, 555772518.00 ns, 271.3733 ns/op
WorkloadWarmup   5: 2048000 op, 556849657.00 ns, 271.8992 ns/op
WorkloadWarmup   6: 2048000 op, 560496837.00 ns, 273.6801 ns/op
WorkloadWarmup   7: 2048000 op, 554131203.00 ns, 270.5719 ns/op
WorkloadWarmup   8: 2048000 op, 558674572.00 ns, 272.7903 ns/op
WorkloadWarmup   9: 2048000 op, 559150450.00 ns, 273.0227 ns/op
WorkloadWarmup  10: 2048000 op, 557627478.00 ns, 272.2790 ns/op

// BeforeActualRun
WorkloadActual   1: 2048000 op, 559539997.00 ns, 273.2129 ns/op
WorkloadActual   2: 2048000 op, 555864879.00 ns, 271.4184 ns/op
WorkloadActual   3: 2048000 op, 556824329.00 ns, 271.8869 ns/op
WorkloadActual   4: 2048000 op, 551533248.00 ns, 269.3033 ns/op
WorkloadActual   5: 2048000 op, 554832372.00 ns, 270.9142 ns/op
WorkloadActual   6: 2048000 op, 554713930.00 ns, 270.8564 ns/op
WorkloadActual   7: 2048000 op, 559627534.00 ns, 273.2556 ns/op
WorkloadActual   8: 2048000 op, 555558025.00 ns, 271.2686 ns/op
WorkloadActual   9: 2048000 op, 561192622.00 ns, 274.0198 ns/op
WorkloadActual  10: 2048000 op, 552528172.00 ns, 269.7891 ns/op
WorkloadActual  11: 2048000 op, 568186068.00 ns, 277.4346 ns/op
WorkloadActual  12: 2048000 op, 581393517.00 ns, 283.8836 ns/op
WorkloadActual  13: 2048000 op, 583070534.00 ns, 284.7024 ns/op
WorkloadActual  14: 2048000 op, 582707447.00 ns, 284.5251 ns/op
WorkloadActual  15: 2048000 op, 580904095.00 ns, 283.6446 ns/op
WorkloadActual  16: 2048000 op, 575690800.00 ns, 281.0990 ns/op
WorkloadActual  17: 2048000 op, 560056639.00 ns, 273.4652 ns/op
WorkloadActual  18: 2048000 op, 553579300.00 ns, 270.3024 ns/op

// AfterActualRun
WorkloadResult   1: 2048000 op, 559528606.00 ns, 273.2073 ns/op
WorkloadResult   2: 2048000 op, 555853488.00 ns, 271.4128 ns/op
WorkloadResult   3: 2048000 op, 556812938.00 ns, 271.8813 ns/op
WorkloadResult   4: 2048000 op, 551521857.00 ns, 269.2978 ns/op
WorkloadResult   5: 2048000 op, 554820981.00 ns, 270.9087 ns/op
WorkloadResult   6: 2048000 op, 554702539.00 ns, 270.8508 ns/op
WorkloadResult   7: 2048000 op, 559616143.00 ns, 273.2501 ns/op
WorkloadResult   8: 2048000 op, 555546634.00 ns, 271.2630 ns/op
WorkloadResult   9: 2048000 op, 561181231.00 ns, 274.0143 ns/op
WorkloadResult  10: 2048000 op, 552516781.00 ns, 269.7836 ns/op
WorkloadResult  11: 2048000 op, 568174677.00 ns, 277.4290 ns/op
WorkloadResult  12: 2048000 op, 581382126.00 ns, 283.8780 ns/op
WorkloadResult  13: 2048000 op, 583059143.00 ns, 284.6968 ns/op
WorkloadResult  14: 2048000 op, 582696056.00 ns, 284.5196 ns/op
WorkloadResult  15: 2048000 op, 580892704.00 ns, 283.6390 ns/op
WorkloadResult  16: 2048000 op, 575679409.00 ns, 281.0935 ns/op
WorkloadResult  17: 2048000 op, 560045248.00 ns, 273.4596 ns/op
WorkloadResult  18: 2048000 op, 553567909.00 ns, 270.2968 ns/op
// GC:  44 0 0 737280032 2048000
// Threading:  0 0 2048000

// AfterAll
// Benchmark Process 4552 has exited with code 0.

Mean = 275.271 ns, StdErr = 1.331 ns (0.48%), N = 18, StdDev = 5.645 ns
Min = 269.298 ns, Q1 = 270.997 ns, Median = 273.229 ns, Q3 = 280.177 ns, Max = 284.697 ns
IQR = 9.180 ns, LowerFence = 257.227 ns, UpperFence = 293.947 ns
ConfidenceInterval = [269.995 ns; 280.547 ns] (CI 99.9%), Margin = 5.276 ns (1.92% of Mean)
Skewness = 0.68, Kurtosis = 1.69, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-04 10:31 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 168825.00 ns, 168.8250 ns/op
WorkloadJitting  1: 1000 op, 1758299.00 ns, 1.7583 us/op

OverheadJitting  2: 16000 op, 178764.00 ns, 11.1728 ns/op
WorkloadJitting  2: 16000 op, 20250683.00 ns, 1.2657 us/op

WorkloadPilot    1: 16000 op, 18722663.00 ns, 1.1702 us/op
WorkloadPilot    2: 32000 op, 36086623.00 ns, 1.1277 us/op
WorkloadPilot    3: 64000 op, 71354098.00 ns, 1.1149 us/op
WorkloadPilot    4: 128000 op, 155784520.00 ns, 1.2171 us/op
WorkloadPilot    5: 256000 op, 103943038.00 ns, 406.0275 ns/op
WorkloadPilot    6: 512000 op, 76104497.00 ns, 148.6416 ns/op
WorkloadPilot    7: 1024000 op, 152936128.00 ns, 149.3517 ns/op
WorkloadPilot    8: 2048000 op, 308807651.00 ns, 150.7850 ns/op
WorkloadPilot    9: 4096000 op, 608508831.00 ns, 148.5617 ns/op

OverheadWarmup   1: 4096000 op, 13125.00 ns, 0.0032 ns/op
OverheadWarmup   2: 4096000 op, 9728.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 15328.00 ns, 0.0037 ns/op
OverheadWarmup   4: 4096000 op, 9668.00 ns, 0.0024 ns/op
OverheadWarmup   5: 4096000 op, 9749.00 ns, 0.0024 ns/op
OverheadWarmup   6: 4096000 op, 9638.00 ns, 0.0024 ns/op

OverheadActual   1: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   2: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual   4: 4096000 op, 9649.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual   6: 4096000 op, 9647.00 ns, 0.0024 ns/op
OverheadActual   7: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual   8: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual   9: 4096000 op, 9607.00 ns, 0.0023 ns/op
OverheadActual  10: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual  11: 4096000 op, 22262.00 ns, 0.0054 ns/op
OverheadActual  12: 4096000 op, 17513.00 ns, 0.0043 ns/op
OverheadActual  13: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  14: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual  15: 4096000 op, 9647.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 4096000 op, 616716205.00 ns, 150.5655 ns/op
WorkloadWarmup   2: 4096000 op, 615848790.00 ns, 150.3537 ns/op
WorkloadWarmup   3: 4096000 op, 613247572.00 ns, 149.7186 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 610955657.00 ns, 149.1591 ns/op
WorkloadActual   2: 4096000 op, 609899649.00 ns, 148.9013 ns/op
WorkloadActual   3: 4096000 op, 609550217.00 ns, 148.8160 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 610946010.00 ns, 149.1567 ns/op
WorkloadResult   2: 4096000 op, 609890002.00 ns, 148.8989 ns/op
WorkloadResult   3: 4096000 op, 609540570.00 ns, 148.8136 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4566 has exited with code 0.

Mean = 148.956 ns, StdErr = 0.103 ns (0.07%), N = 3, StdDev = 0.179 ns
Min = 148.814 ns, Q1 = 148.856 ns, Median = 148.899 ns, Q3 = 149.028 ns, Max = 149.157 ns
IQR = 0.172 ns, LowerFence = 148.599 ns, UpperFence = 149.285 ns
ConfidenceInterval = [145.697 ns; 152.216 ns] (CI 99.9%), Margin = 3.259 ns (2.19% of Mean)
Skewness = 0.29, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-04 10:31 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 171690.00 ns, 171.6900 ns/op
WorkloadJitting  1: 1000 op, 1999449.00 ns, 1.9994 us/op

OverheadJitting  2: 16000 op, 180045.00 ns, 11.2528 ns/op
WorkloadJitting  2: 16000 op, 23952491.00 ns, 1.4970 us/op

WorkloadPilot    1: 16000 op, 20205824.00 ns, 1.2629 us/op
WorkloadPilot    2: 32000 op, 39492133.00 ns, 1.2341 us/op
WorkloadPilot    3: 64000 op, 68616008.00 ns, 1.0721 us/op
WorkloadPilot    4: 128000 op, 160411116.00 ns, 1.2532 us/op
WorkloadPilot    5: 256000 op, 97646279.00 ns, 381.4308 ns/op
WorkloadPilot    6: 512000 op, 112796354.00 ns, 220.3054 ns/op
WorkloadPilot    7: 1024000 op, 224461759.00 ns, 219.2009 ns/op
WorkloadPilot    8: 2048000 op, 455171973.00 ns, 222.2519 ns/op
WorkloadPilot    9: 4096000 op, 907252720.00 ns, 221.4972 ns/op

OverheadWarmup   1: 4096000 op, 13144.00 ns, 0.0032 ns/op
OverheadWarmup   2: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 9577.00 ns, 0.0023 ns/op
OverheadWarmup   4: 4096000 op, 9567.00 ns, 0.0023 ns/op
OverheadWarmup   5: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadWarmup   6: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadWarmup   7: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadWarmup   8: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadWarmup   9: 4096000 op, 9618.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual   2: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 17583.00 ns, 0.0043 ns/op
OverheadActual   6: 4096000 op, 17012.00 ns, 0.0042 ns/op
OverheadActual   7: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual   8: 4096000 op, 11871.00 ns, 0.0029 ns/op
OverheadActual   9: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  10: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual  11: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual  12: 4096000 op, 19026.00 ns, 0.0046 ns/op
OverheadActual  13: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual  14: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual  15: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  16: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual  17: 4096000 op, 9638.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 4096000 op, 913054470.00 ns, 222.9137 ns/op
WorkloadWarmup   2: 4096000 op, 912205106.00 ns, 222.7063 ns/op
WorkloadWarmup   3: 4096000 op, 908777080.00 ns, 221.8694 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 905272945.00 ns, 221.0139 ns/op
WorkloadActual   2: 4096000 op, 910398666.00 ns, 222.2653 ns/op
WorkloadActual   3: 4096000 op, 913607778.00 ns, 223.0488 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 905263307.00 ns, 221.0115 ns/op
WorkloadResult   2: 4096000 op, 910389028.00 ns, 222.2629 ns/op
WorkloadResult   3: 4096000 op, 913598140.00 ns, 223.0464 ns/op
// GC:  105 0 0 1769472000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4578 has exited with code 0.

Mean = 222.107 ns, StdErr = 0.593 ns (0.27%), N = 3, StdDev = 1.026 ns
Min = 221.012 ns, Q1 = 221.637 ns, Median = 222.263 ns, Q3 = 222.655 ns, Max = 223.046 ns
IQR = 1.017 ns, LowerFence = 220.111 ns, UpperFence = 224.181 ns
ConfidenceInterval = [203.382 ns; 240.832 ns] (CI 99.9%), Margin = 18.725 ns (8.43% of Mean)
Skewness = -0.15, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-04 10:30 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 171220.00 ns, 171.2200 ns/op
WorkloadJitting  1: 1000 op, 1824146.00 ns, 1.8241 us/op

OverheadJitting  2: 16000 op, 188472.00 ns, 11.7795 ns/op
WorkloadJitting  2: 16000 op, 20533771.00 ns, 1.2834 us/op

WorkloadPilot    1: 16000 op, 17712784.00 ns, 1.1070 us/op
WorkloadPilot    2: 32000 op, 36805354.00 ns, 1.1502 us/op
WorkloadPilot    3: 64000 op, 62755637.00 ns, 980.5568 ns/op
WorkloadPilot    4: 128000 op, 135080455.00 ns, 1.0553 us/op
WorkloadPilot    5: 256000 op, 137625286.00 ns, 537.5988 ns/op
WorkloadPilot    6: 512000 op, 103293611.00 ns, 201.7453 ns/op
WorkloadPilot    7: 1024000 op, 205538364.00 ns, 200.7211 ns/op
WorkloadPilot    8: 2048000 op, 413870093.00 ns, 202.0850 ns/op
WorkloadPilot    9: 4096000 op, 823737230.00 ns, 201.1077 ns/op

OverheadWarmup   1: 4096000 op, 12864.00 ns, 0.0031 ns/op
OverheadWarmup   2: 4096000 op, 11491.00 ns, 0.0028 ns/op
OverheadWarmup   3: 4096000 op, 10760.00 ns, 0.0026 ns/op
OverheadWarmup   4: 4096000 op, 11472.00 ns, 0.0028 ns/op
OverheadWarmup   5: 4096000 op, 10700.00 ns, 0.0026 ns/op
OverheadWarmup   6: 4096000 op, 10690.00 ns, 0.0026 ns/op
OverheadWarmup   7: 4096000 op, 10740.00 ns, 0.0026 ns/op
OverheadWarmup   8: 4096000 op, 10690.00 ns, 0.0026 ns/op

OverheadActual   1: 4096000 op, 10709.00 ns, 0.0026 ns/op
OverheadActual   2: 4096000 op, 10861.00 ns, 0.0027 ns/op
OverheadActual   3: 4096000 op, 10840.00 ns, 0.0026 ns/op
OverheadActual   4: 4096000 op, 11461.00 ns, 0.0028 ns/op
OverheadActual   5: 4096000 op, 10740.00 ns, 0.0026 ns/op
OverheadActual   6: 4096000 op, 10680.00 ns, 0.0026 ns/op
OverheadActual   7: 4096000 op, 10750.00 ns, 0.0026 ns/op
OverheadActual   8: 4096000 op, 10690.00 ns, 0.0026 ns/op
OverheadActual   9: 4096000 op, 11762.00 ns, 0.0029 ns/op
OverheadActual  10: 4096000 op, 11471.00 ns, 0.0028 ns/op
OverheadActual  11: 4096000 op, 10710.00 ns, 0.0026 ns/op
OverheadActual  12: 4096000 op, 10729.00 ns, 0.0026 ns/op
OverheadActual  13: 4096000 op, 10830.00 ns, 0.0026 ns/op
OverheadActual  14: 4096000 op, 10751.00 ns, 0.0026 ns/op
OverheadActual  15: 4096000 op, 10770.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 4096000 op, 835198204.00 ns, 203.9058 ns/op
WorkloadWarmup   2: 4096000 op, 828918986.00 ns, 202.3728 ns/op
WorkloadWarmup   3: 4096000 op, 825277147.00 ns, 201.4837 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 823713601.00 ns, 201.1020 ns/op
WorkloadActual   2: 4096000 op, 829897332.00 ns, 202.6117 ns/op
WorkloadActual   3: 4096000 op, 826215156.00 ns, 201.7127 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 823702850.00 ns, 201.0993 ns/op
WorkloadResult   2: 4096000 op, 829886581.00 ns, 202.6090 ns/op
WorkloadResult   3: 4096000 op, 826204405.00 ns, 201.7101 ns/op
// GC:  64 0 0 1081344000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4587 has exited with code 0.

Mean = 201.806 ns, StdErr = 0.438 ns (0.22%), N = 3, StdDev = 0.759 ns
Min = 201.099 ns, Q1 = 201.405 ns, Median = 201.710 ns, Q3 = 202.160 ns, Max = 202.609 ns
IQR = 0.755 ns, LowerFence = 200.272 ns, UpperFence = 203.292 ns
ConfidenceInterval = [187.951 ns; 215.661 ns] (CI 99.9%), Margin = 13.855 ns (6.87% of Mean)
Skewness = 0.12, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-04 10:30 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 175307.00 ns, 175.3070 ns/op
WorkloadJitting  1: 1000 op, 2447564.00 ns, 2.4476 us/op

OverheadJitting  2: 16000 op, 234407.00 ns, 14.6504 ns/op
WorkloadJitting  2: 16000 op, 30752564.00 ns, 1.9220 us/op

WorkloadPilot    1: 16000 op, 26178444.00 ns, 1.6362 us/op
WorkloadPilot    2: 32000 op, 53333449.00 ns, 1.6667 us/op
WorkloadPilot    3: 64000 op, 93675615.00 ns, 1.4637 us/op
WorkloadPilot    4: 128000 op, 191520655.00 ns, 1.4963 us/op
WorkloadPilot    5: 256000 op, 73445803.00 ns, 286.8977 ns/op
WorkloadPilot    6: 512000 op, 144752287.00 ns, 282.7193 ns/op
WorkloadPilot    7: 1024000 op, 296152079.00 ns, 289.2110 ns/op
WorkloadPilot    8: 2048000 op, 586645305.00 ns, 286.4479 ns/op

OverheadWarmup   1: 2048000 op, 7334.00 ns, 0.0036 ns/op
OverheadWarmup   2: 2048000 op, 5040.00 ns, 0.0025 ns/op
OverheadWarmup   3: 2048000 op, 4989.00 ns, 0.0024 ns/op
OverheadWarmup   4: 2048000 op, 4989.00 ns, 0.0024 ns/op
OverheadWarmup   5: 2048000 op, 5000.00 ns, 0.0024 ns/op
OverheadWarmup   6: 2048000 op, 4979.00 ns, 0.0024 ns/op
OverheadWarmup   7: 2048000 op, 5010.00 ns, 0.0024 ns/op

OverheadActual   1: 2048000 op, 5060.00 ns, 0.0025 ns/op
OverheadActual   2: 2048000 op, 5030.00 ns, 0.0025 ns/op
OverheadActual   3: 2048000 op, 5040.00 ns, 0.0025 ns/op
OverheadActual   4: 2048000 op, 5039.00 ns, 0.0025 ns/op
OverheadActual   5: 2048000 op, 5020.00 ns, 0.0025 ns/op
OverheadActual   6: 2048000 op, 4989.00 ns, 0.0024 ns/op
OverheadActual   7: 2048000 op, 5009.00 ns, 0.0024 ns/op
OverheadActual   8: 2048000 op, 5019.00 ns, 0.0025 ns/op
OverheadActual   9: 2048000 op, 5009.00 ns, 0.0024 ns/op
OverheadActual  10: 2048000 op, 5019.00 ns, 0.0025 ns/op
OverheadActual  11: 2048000 op, 4999.00 ns, 0.0024 ns/op
OverheadActual  12: 2048000 op, 5010.00 ns, 0.0024 ns/op
OverheadActual  13: 2048000 op, 4999.00 ns, 0.0024 ns/op
OverheadActual  14: 2048000 op, 4990.00 ns, 0.0024 ns/op
OverheadActual  15: 2048000 op, 5009.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 2048000 op, 598342036.00 ns, 292.1592 ns/op
WorkloadWarmup   2: 2048000 op, 590536270.00 ns, 288.3478 ns/op
WorkloadWarmup   3: 2048000 op, 587262987.00 ns, 286.7495 ns/op

// BeforeActualRun
WorkloadActual   1: 2048000 op, 595801743.00 ns, 290.9188 ns/op
WorkloadActual   2: 2048000 op, 582214756.00 ns, 284.2845 ns/op
WorkloadActual   3: 2048000 op, 590625392.00 ns, 288.3913 ns/op

// AfterActualRun
WorkloadResult   1: 2048000 op, 595796733.00 ns, 290.9164 ns/op
WorkloadResult   2: 2048000 op, 582209746.00 ns, 284.2821 ns/op
WorkloadResult   3: 2048000 op, 590620382.00 ns, 288.3889 ns/op
// GC:  44 0 0 737280000 2048000
// Threading:  0 0 2048000

// AfterAll
// Benchmark Process 4597 has exited with code 0.

Mean = 287.862 ns, StdErr = 1.933 ns (0.67%), N = 3, StdDev = 3.348 ns
Min = 284.282 ns, Q1 = 286.335 ns, Median = 288.389 ns, Q3 = 289.653 ns, Max = 290.916 ns
IQR = 3.317 ns, LowerFence = 281.360 ns, UpperFence = 294.628 ns
ConfidenceInterval = [226.777 ns; 348.948 ns] (CI 99.9%), Margin = 61.086 ns (21.22% of Mean)
Skewness = -0.15, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-04 10:30 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 146.752 ns, StdErr = 0.141 ns (0.10%), N = 14, StdDev = 0.527 ns
Min = 146.085 ns, Q1 = 146.309 ns, Median = 146.648 ns, Q3 = 147.012 ns, Max = 147.955 ns
IQR = 0.703 ns, LowerFence = 145.254 ns, UpperFence = 148.067 ns
ConfidenceInterval = [146.158 ns; 147.347 ns] (CI 99.9%), Margin = 0.595 ns (0.41% of Mean)
Skewness = 0.7, Kurtosis = 2.62, MValue = 2
-------------------- Histogram --------------------
[145.798 ns ; 148.242 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 231.540 ns, StdErr = 0.124 ns (0.05%), N = 14, StdDev = 0.465 ns
Min = 230.827 ns, Q1 = 231.258 ns, Median = 231.480 ns, Q3 = 231.787 ns, Max = 232.552 ns
IQR = 0.529 ns, LowerFence = 230.464 ns, UpperFence = 232.581 ns
ConfidenceInterval = [231.015 ns; 232.064 ns] (CI 99.9%), Margin = 0.524 ns (0.23% of Mean)
Skewness = 0.46, Kurtosis = 2.4, MValue = 2
-------------------- Histogram --------------------
[230.574 ns ; 232.805 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 216.228 ns, StdErr = 0.167 ns (0.08%), N = 15, StdDev = 0.647 ns
Min = 215.045 ns, Q1 = 215.833 ns, Median = 216.197 ns, Q3 = 216.669 ns, Max = 217.411 ns
IQR = 0.836 ns, LowerFence = 214.580 ns, UpperFence = 217.922 ns
ConfidenceInterval = [215.536 ns; 216.919 ns] (CI 99.9%), Margin = 0.691 ns (0.32% of Mean)
Skewness = 0.05, Kurtosis = 1.95, MValue = 2
-------------------- Histogram --------------------
[214.700 ns ; 217.756 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 275.271 ns, StdErr = 1.331 ns (0.48%), N = 18, StdDev = 5.645 ns
Min = 269.298 ns, Q1 = 270.997 ns, Median = 273.229 ns, Q3 = 280.177 ns, Max = 284.697 ns
IQR = 9.180 ns, LowerFence = 257.227 ns, UpperFence = 293.947 ns
ConfidenceInterval = [269.995 ns; 280.547 ns] (CI 99.9%), Margin = 5.276 ns (1.92% of Mean)
Skewness = 0.68, Kurtosis = 1.69, MValue = 2
-------------------- Histogram --------------------
[268.829 ns ; 274.483 ns) | @@@@@@@@@@@@
[274.483 ns ; 285.724 ns) | @@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 148.956 ns, StdErr = 0.103 ns (0.07%), N = 3, StdDev = 0.179 ns
Min = 148.814 ns, Q1 = 148.856 ns, Median = 148.899 ns, Q3 = 149.028 ns, Max = 149.157 ns
IQR = 0.172 ns, LowerFence = 148.599 ns, UpperFence = 149.285 ns
ConfidenceInterval = [145.697 ns; 152.216 ns] (CI 99.9%), Margin = 3.259 ns (2.19% of Mean)
Skewness = 0.29, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[148.651 ns ; 149.319 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 222.107 ns, StdErr = 0.593 ns (0.27%), N = 3, StdDev = 1.026 ns
Min = 221.012 ns, Q1 = 221.637 ns, Median = 222.263 ns, Q3 = 222.655 ns, Max = 223.046 ns
IQR = 1.017 ns, LowerFence = 220.111 ns, UpperFence = 224.181 ns
ConfidenceInterval = [203.382 ns; 240.832 ns] (CI 99.9%), Margin = 18.725 ns (8.43% of Mean)
Skewness = -0.15, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[220.077 ns ; 223.981 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 201.806 ns, StdErr = 0.438 ns (0.22%), N = 3, StdDev = 0.759 ns
Min = 201.099 ns, Q1 = 201.405 ns, Median = 201.710 ns, Q3 = 202.160 ns, Max = 202.609 ns
IQR = 0.755 ns, LowerFence = 200.272 ns, UpperFence = 203.292 ns
ConfidenceInterval = [187.951 ns; 215.661 ns] (CI 99.9%), Margin = 13.855 ns (6.87% of Mean)
Skewness = 0.12, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[200.408 ns ; 203.300 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 287.862 ns, StdErr = 1.933 ns (0.67%), N = 3, StdDev = 3.348 ns
Min = 284.282 ns, Q1 = 286.335 ns, Median = 288.389 ns, Q3 = 289.653 ns, Max = 290.916 ns
IQR = 3.317 ns, LowerFence = 281.360 ns, UpperFence = 294.628 ns
ConfidenceInterval = [226.777 ns; 348.948 ns] (CI 99.9%), Margin = 61.086 ns (21.22% of Mean)
Skewness = -0.15, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[281.235 ns ; 292.700 ns) | @@@
---------------------------------------------------

// * Summary *

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean     | Error    | StdDev  | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |---------:|---------:|--------:|-------:|----------:|
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 146.8 ns |  0.59 ns | 0.53 ns | 0.0115 |     192 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 231.5 ns |  0.52 ns | 0.46 ns | 0.0256 |     432 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 216.2 ns |  0.69 ns | 0.65 ns | 0.0156 |     264 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 275.3 ns |  5.28 ns | 5.65 ns | 0.0215 |     360 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           | 149.0 ns |  3.26 ns | 0.18 ns | 0.0115 |     192 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 222.1 ns | 18.72 ns | 1.03 ns | 0.0256 |     432 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           | 201.8 ns | 13.85 ns | 0.76 ns | 0.0156 |     264 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 287.9 ns | 61.09 ns | 3.35 ns | 0.0215 |     360 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput        -> 1 outlier  was  removed (151.08 ns)
  CoreDispatchBenchmarks.'Notification  Notify': RunStrategy=Throughput -> 1 outlier  was  removed (234.18 ns)
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
Run time: 00:01:50 (110.21 sec), executed benchmarks: 8

Global total time: 00:02:04 (124.25 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
