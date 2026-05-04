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

Run: 2026-05-04 16:28 UTC | Branch: copilot/implement-medium-term | Commit: e59310f

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Platinum 8370C CPU 2.80GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4


```
| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error     | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|----------:|---------:|-------:|----------:|
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  66.01 ns |  0.215 ns | 0.201 ns | 0.0009 |      24 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 127.10 ns |  1.031 ns | 0.914 ns | 0.0105 |     264 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  73.97 ns |  0.221 ns | 0.207 ns | 0.0038 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 166.64 ns |  0.300 ns | 0.266 ns | 0.0076 |     192 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  62.55 ns |  2.759 ns | 0.151 ns | 0.0009 |      24 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 128.56 ns |  6.256 ns | 0.343 ns | 0.0105 |     264 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  76.45 ns |  2.581 ns | 0.141 ns | 0.0038 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 168.84 ns | 10.427 ns | 0.572 ns | 0.0076 |     192 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.58 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 11.71 sec and exited with 0
// ***** Done, took 00:00:13 (13.35 sec)   *****
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
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 191470.00 ns, 191.4700 ns/op
WorkloadJitting  1: 1000 op, 1012712.00 ns, 1.0127 us/op

OverheadJitting  2: 16000 op, 237603.00 ns, 14.8502 ns/op
WorkloadJitting  2: 16000 op, 5633876.00 ns, 352.1173 ns/op

WorkloadPilot    1: 16000 op, 4412942.00 ns, 275.8089 ns/op
WorkloadPilot    2: 32000 op, 11826733.00 ns, 369.5854 ns/op
WorkloadPilot    3: 64000 op, 29315352.00 ns, 458.0524 ns/op
WorkloadPilot    4: 128000 op, 40366515.00 ns, 315.3634 ns/op
WorkloadPilot    5: 256000 op, 66874383.00 ns, 261.2281 ns/op
WorkloadPilot    6: 512000 op, 36927522.00 ns, 72.1241 ns/op
WorkloadPilot    7: 1024000 op, 69083931.00 ns, 67.4648 ns/op
WorkloadPilot    8: 2048000 op, 136446794.00 ns, 66.6244 ns/op
WorkloadPilot    9: 4096000 op, 269841821.00 ns, 65.8794 ns/op
WorkloadPilot   10: 8192000 op, 539495503.00 ns, 65.8564 ns/op

OverheadWarmup   1: 8192000 op, 30335.00 ns, 0.0037 ns/op
OverheadWarmup   2: 8192000 op, 29651.00 ns, 0.0036 ns/op
OverheadWarmup   3: 8192000 op, 32664.00 ns, 0.0040 ns/op
OverheadWarmup   4: 8192000 op, 28011.00 ns, 0.0034 ns/op
OverheadWarmup   5: 8192000 op, 25576.00 ns, 0.0031 ns/op
OverheadWarmup   6: 8192000 op, 26775.00 ns, 0.0033 ns/op
OverheadWarmup   7: 8192000 op, 27438.00 ns, 0.0033 ns/op
OverheadWarmup   8: 8192000 op, 27702.00 ns, 0.0034 ns/op
OverheadWarmup   9: 8192000 op, 32533.00 ns, 0.0040 ns/op
OverheadWarmup  10: 8192000 op, 26718.00 ns, 0.0033 ns/op

OverheadActual   1: 8192000 op, 28905.00 ns, 0.0035 ns/op
OverheadActual   2: 8192000 op, 36788.00 ns, 0.0045 ns/op
OverheadActual   3: 8192000 op, 28459.00 ns, 0.0035 ns/op
OverheadActual   4: 8192000 op, 28467.00 ns, 0.0035 ns/op
OverheadActual   5: 8192000 op, 29463.00 ns, 0.0036 ns/op
OverheadActual   6: 8192000 op, 28747.00 ns, 0.0035 ns/op
OverheadActual   7: 8192000 op, 30337.00 ns, 0.0037 ns/op
OverheadActual   8: 8192000 op, 33747.00 ns, 0.0041 ns/op
OverheadActual   9: 8192000 op, 31447.00 ns, 0.0038 ns/op
OverheadActual  10: 8192000 op, 28046.00 ns, 0.0034 ns/op
OverheadActual  11: 8192000 op, 30482.00 ns, 0.0037 ns/op
OverheadActual  12: 8192000 op, 33378.00 ns, 0.0041 ns/op
OverheadActual  13: 8192000 op, 28253.00 ns, 0.0034 ns/op
OverheadActual  14: 8192000 op, 33186.00 ns, 0.0041 ns/op
OverheadActual  15: 8192000 op, 39477.00 ns, 0.0048 ns/op
OverheadActual  16: 8192000 op, 40136.00 ns, 0.0049 ns/op
OverheadActual  17: 8192000 op, 28552.00 ns, 0.0035 ns/op
OverheadActual  18: 8192000 op, 27916.00 ns, 0.0034 ns/op
OverheadActual  19: 8192000 op, 27776.00 ns, 0.0034 ns/op
OverheadActual  20: 8192000 op, 27878.00 ns, 0.0034 ns/op

WorkloadWarmup   1: 8192000 op, 553473751.00 ns, 67.5627 ns/op
WorkloadWarmup   2: 8192000 op, 546679809.00 ns, 66.7334 ns/op
WorkloadWarmup   3: 8192000 op, 542010397.00 ns, 66.1634 ns/op
WorkloadWarmup   4: 8192000 op, 538735322.00 ns, 65.7636 ns/op
WorkloadWarmup   5: 8192000 op, 539697316.00 ns, 65.8810 ns/op
WorkloadWarmup   6: 8192000 op, 541405938.00 ns, 66.0896 ns/op
WorkloadWarmup   7: 8192000 op, 539920575.00 ns, 65.9083 ns/op
WorkloadWarmup   8: 8192000 op, 544572668.00 ns, 66.4762 ns/op
WorkloadWarmup   9: 8192000 op, 543552899.00 ns, 66.3517 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 544334135.00 ns, 66.4470 ns/op
WorkloadActual   2: 8192000 op, 540257364.00 ns, 65.9494 ns/op
WorkloadActual   3: 8192000 op, 541207285.00 ns, 66.0653 ns/op
WorkloadActual   4: 8192000 op, 541388153.00 ns, 66.0874 ns/op
WorkloadActual   5: 8192000 op, 540876248.00 ns, 66.0249 ns/op
WorkloadActual   6: 8192000 op, 540523970.00 ns, 65.9819 ns/op
WorkloadActual   7: 8192000 op, 539103095.00 ns, 65.8085 ns/op
WorkloadActual   8: 8192000 op, 539843034.00 ns, 65.8988 ns/op
WorkloadActual   9: 8192000 op, 539439691.00 ns, 65.8496 ns/op
WorkloadActual  10: 8192000 op, 541418919.00 ns, 66.0912 ns/op
WorkloadActual  11: 8192000 op, 539381943.00 ns, 65.8425 ns/op
WorkloadActual  12: 8192000 op, 543635578.00 ns, 66.3618 ns/op
WorkloadActual  13: 8192000 op, 542490172.00 ns, 66.2219 ns/op
WorkloadActual  14: 8192000 op, 539093909.00 ns, 65.8074 ns/op
WorkloadActual  15: 8192000 op, 539091617.00 ns, 65.8071 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 544304951.00 ns, 66.4435 ns/op
WorkloadResult   2: 8192000 op, 540228180.00 ns, 65.9458 ns/op
WorkloadResult   3: 8192000 op, 541178101.00 ns, 66.0618 ns/op
WorkloadResult   4: 8192000 op, 541358969.00 ns, 66.0839 ns/op
WorkloadResult   5: 8192000 op, 540847064.00 ns, 66.0214 ns/op
WorkloadResult   6: 8192000 op, 540494786.00 ns, 65.9784 ns/op
WorkloadResult   7: 8192000 op, 539073911.00 ns, 65.8049 ns/op
WorkloadResult   8: 8192000 op, 539813850.00 ns, 65.8952 ns/op
WorkloadResult   9: 8192000 op, 539410507.00 ns, 65.8460 ns/op
WorkloadResult  10: 8192000 op, 541389735.00 ns, 66.0876 ns/op
WorkloadResult  11: 8192000 op, 539352759.00 ns, 65.8390 ns/op
WorkloadResult  12: 8192000 op, 543606394.00 ns, 66.3582 ns/op
WorkloadResult  13: 8192000 op, 542460988.00 ns, 66.2184 ns/op
WorkloadResult  14: 8192000 op, 539064725.00 ns, 65.8038 ns/op
WorkloadResult  15: 8192000 op, 539062433.00 ns, 65.8035 ns/op
// GC:  7 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4265 has exited with code 0.

Mean = 66.013 ns, StdErr = 0.052 ns (0.08%), N = 15, StdDev = 0.201 ns
Min = 65.804 ns, Q1 = 65.842 ns, Median = 65.978 ns, Q3 = 66.086 ns, Max = 66.443 ns
IQR = 0.243 ns, LowerFence = 65.478 ns, UpperFence = 66.451 ns
ConfidenceInterval = [65.797 ns; 66.228 ns] (CI 99.9%), Margin = 0.215 ns (0.33% of Mean)
Skewness = 0.75, Kurtosis = 2.35, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-04 16:28 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Notification --job RunStrategy=Throughput --benchmarkId 1 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 174048.00 ns, 174.0480 ns/op
WorkloadJitting  1: 1000 op, 1765641.00 ns, 1.7656 us/op

OverheadJitting  2: 16000 op, 310277.00 ns, 19.3923 ns/op
WorkloadJitting  2: 16000 op, 15535607.00 ns, 970.9754 ns/op

WorkloadPilot    1: 16000 op, 8442710.00 ns, 527.6694 ns/op
WorkloadPilot    2: 32000 op, 15421902.00 ns, 481.9344 ns/op
WorkloadPilot    3: 64000 op, 29710314.00 ns, 464.2237 ns/op
WorkloadPilot    4: 128000 op, 55300013.00 ns, 432.0314 ns/op
WorkloadPilot    5: 256000 op, 63176330.00 ns, 246.7825 ns/op
WorkloadPilot    6: 512000 op, 66122083.00 ns, 129.1447 ns/op
WorkloadPilot    7: 1024000 op, 129386482.00 ns, 126.3540 ns/op
WorkloadPilot    8: 2048000 op, 263867746.00 ns, 128.8417 ns/op
WorkloadPilot    9: 4096000 op, 521227013.00 ns, 127.2527 ns/op

OverheadWarmup   1: 4096000 op, 17305.00 ns, 0.0042 ns/op
OverheadWarmup   2: 4096000 op, 15310.00 ns, 0.0037 ns/op
OverheadWarmup   3: 4096000 op, 14650.00 ns, 0.0036 ns/op
OverheadWarmup   4: 4096000 op, 14829.00 ns, 0.0036 ns/op
OverheadWarmup   5: 4096000 op, 15293.00 ns, 0.0037 ns/op
OverheadWarmup   6: 4096000 op, 14971.00 ns, 0.0037 ns/op
OverheadWarmup   7: 4096000 op, 15048.00 ns, 0.0037 ns/op
OverheadWarmup   8: 4096000 op, 14919.00 ns, 0.0036 ns/op

OverheadActual   1: 4096000 op, 15026.00 ns, 0.0037 ns/op
OverheadActual   2: 4096000 op, 14976.00 ns, 0.0037 ns/op
OverheadActual   3: 4096000 op, 14893.00 ns, 0.0036 ns/op
OverheadActual   4: 4096000 op, 14765.00 ns, 0.0036 ns/op
OverheadActual   5: 4096000 op, 15047.00 ns, 0.0037 ns/op
OverheadActual   6: 4096000 op, 15117.00 ns, 0.0037 ns/op
OverheadActual   7: 4096000 op, 14670.00 ns, 0.0036 ns/op
OverheadActual   8: 4096000 op, 14913.00 ns, 0.0036 ns/op
OverheadActual   9: 4096000 op, 16385.00 ns, 0.0040 ns/op
OverheadActual  10: 4096000 op, 14573.00 ns, 0.0036 ns/op
OverheadActual  11: 4096000 op, 14743.00 ns, 0.0036 ns/op
OverheadActual  12: 4096000 op, 15249.00 ns, 0.0037 ns/op
OverheadActual  13: 4096000 op, 14712.00 ns, 0.0036 ns/op
OverheadActual  14: 4096000 op, 15251.00 ns, 0.0037 ns/op
OverheadActual  15: 4096000 op, 14988.00 ns, 0.0037 ns/op

WorkloadWarmup   1: 4096000 op, 531701909.00 ns, 129.8100 ns/op
WorkloadWarmup   2: 4096000 op, 531640458.00 ns, 129.7950 ns/op
WorkloadWarmup   3: 4096000 op, 526549189.00 ns, 128.5520 ns/op
WorkloadWarmup   4: 4096000 op, 526154050.00 ns, 128.4556 ns/op
WorkloadWarmup   5: 4096000 op, 523380020.00 ns, 127.7783 ns/op
WorkloadWarmup   6: 4096000 op, 524788239.00 ns, 128.1221 ns/op
WorkloadWarmup   7: 4096000 op, 527330141.00 ns, 128.7427 ns/op
WorkloadWarmup   8: 4096000 op, 521769181.00 ns, 127.3851 ns/op
WorkloadWarmup   9: 4096000 op, 524437186.00 ns, 128.0364 ns/op
WorkloadWarmup  10: 4096000 op, 521906440.00 ns, 127.4186 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 522228085.00 ns, 127.4971 ns/op
WorkloadActual   2: 4096000 op, 522454863.00 ns, 127.5525 ns/op
WorkloadActual   3: 4096000 op, 517099060.00 ns, 126.2449 ns/op
WorkloadActual   4: 4096000 op, 519694778.00 ns, 126.8786 ns/op
WorkloadActual   5: 4096000 op, 513697519.00 ns, 125.4144 ns/op
WorkloadActual   6: 4096000 op, 520879938.00 ns, 127.1680 ns/op
WorkloadActual   7: 4096000 op, 518008829.00 ns, 126.4670 ns/op
WorkloadActual   8: 4096000 op, 522377448.00 ns, 127.5336 ns/op
WorkloadActual   9: 4096000 op, 515438819.00 ns, 125.8396 ns/op
WorkloadActual  10: 4096000 op, 520815809.00 ns, 127.1523 ns/op
WorkloadActual  11: 4096000 op, 519878802.00 ns, 126.9235 ns/op
WorkloadActual  12: 4096000 op, 523386379.00 ns, 127.7799 ns/op
WorkloadActual  13: 4096000 op, 524332374.00 ns, 128.0108 ns/op
WorkloadActual  14: 4096000 op, 538224094.00 ns, 131.4024 ns/op
WorkloadActual  15: 4096000 op, 528121332.00 ns, 128.9359 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 522213109.00 ns, 127.4934 ns/op
WorkloadResult   2: 4096000 op, 522439887.00 ns, 127.5488 ns/op
WorkloadResult   3: 4096000 op, 517084084.00 ns, 126.2412 ns/op
WorkloadResult   4: 4096000 op, 519679802.00 ns, 126.8750 ns/op
WorkloadResult   5: 4096000 op, 513682543.00 ns, 125.4108 ns/op
WorkloadResult   6: 4096000 op, 520864962.00 ns, 127.1643 ns/op
WorkloadResult   7: 4096000 op, 517993853.00 ns, 126.4633 ns/op
WorkloadResult   8: 4096000 op, 522362472.00 ns, 127.5299 ns/op
WorkloadResult   9: 4096000 op, 515423843.00 ns, 125.8359 ns/op
WorkloadResult  10: 4096000 op, 520800833.00 ns, 127.1486 ns/op
WorkloadResult  11: 4096000 op, 519863826.00 ns, 126.9199 ns/op
WorkloadResult  12: 4096000 op, 523371403.00 ns, 127.7762 ns/op
WorkloadResult  13: 4096000 op, 524317398.00 ns, 128.0072 ns/op
WorkloadResult  14: 4096000 op, 528106356.00 ns, 128.9322 ns/op
// GC:  43 0 0 1081344000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4280 has exited with code 0.

Mean = 127.096 ns, StdErr = 0.244 ns (0.19%), N = 14, StdDev = 0.914 ns
Min = 125.411 ns, Q1 = 126.566 ns, Median = 127.156 ns, Q3 = 127.544 ns, Max = 128.932 ns
IQR = 0.978 ns, LowerFence = 125.099 ns, UpperFence = 129.011 ns
ConfidenceInterval = [126.065 ns; 128.127 ns] (CI 99.9%), Margin = 1.031 ns (0.81% of Mean)
Skewness = -0.02, Kurtosis = 2.42, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-04 16:28 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Request --job RunStrategy=Throughput --benchmarkId 2 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 191321.00 ns, 191.3210 ns/op
WorkloadJitting  1: 1000 op, 1345595.00 ns, 1.3456 us/op

OverheadJitting  2: 16000 op, 224814.00 ns, 14.0509 ns/op
WorkloadJitting  2: 16000 op, 7155718.00 ns, 447.2324 ns/op

WorkloadPilot    1: 16000 op, 5982353.00 ns, 373.8971 ns/op
WorkloadPilot    2: 32000 op, 10837685.00 ns, 338.6777 ns/op
WorkloadPilot    3: 64000 op, 21782764.00 ns, 340.3557 ns/op
WorkloadPilot    4: 128000 op, 44705314.00 ns, 349.2603 ns/op
WorkloadPilot    5: 256000 op, 80170554.00 ns, 313.1662 ns/op
WorkloadPilot    6: 512000 op, 41084389.00 ns, 80.2429 ns/op
WorkloadPilot    7: 1024000 op, 75592406.00 ns, 73.8207 ns/op
WorkloadPilot    8: 2048000 op, 150527004.00 ns, 73.4995 ns/op
WorkloadPilot    9: 4096000 op, 299151430.00 ns, 73.0350 ns/op
WorkloadPilot   10: 8192000 op, 600383804.00 ns, 73.2890 ns/op

OverheadWarmup   1: 8192000 op, 27265.00 ns, 0.0033 ns/op
OverheadWarmup   2: 8192000 op, 25008.00 ns, 0.0031 ns/op
OverheadWarmup   3: 8192000 op, 25642.00 ns, 0.0031 ns/op
OverheadWarmup   4: 8192000 op, 25817.00 ns, 0.0032 ns/op
OverheadWarmup   5: 8192000 op, 25513.00 ns, 0.0031 ns/op
OverheadWarmup   6: 8192000 op, 25452.00 ns, 0.0031 ns/op
OverheadWarmup   7: 8192000 op, 25494.00 ns, 0.0031 ns/op
OverheadWarmup   8: 8192000 op, 25419.00 ns, 0.0031 ns/op

OverheadActual   1: 8192000 op, 26868.00 ns, 0.0033 ns/op
OverheadActual   2: 8192000 op, 25586.00 ns, 0.0031 ns/op
OverheadActual   3: 8192000 op, 25626.00 ns, 0.0031 ns/op
OverheadActual   4: 8192000 op, 25464.00 ns, 0.0031 ns/op
OverheadActual   5: 8192000 op, 24859.00 ns, 0.0030 ns/op
OverheadActual   6: 8192000 op, 25484.00 ns, 0.0031 ns/op
OverheadActual   7: 8192000 op, 25445.00 ns, 0.0031 ns/op
OverheadActual   8: 8192000 op, 25443.00 ns, 0.0031 ns/op
OverheadActual   9: 8192000 op, 26628.00 ns, 0.0033 ns/op
OverheadActual  10: 8192000 op, 25413.00 ns, 0.0031 ns/op
OverheadActual  11: 8192000 op, 25430.00 ns, 0.0031 ns/op
OverheadActual  12: 8192000 op, 25521.00 ns, 0.0031 ns/op
OverheadActual  13: 8192000 op, 25117.00 ns, 0.0031 ns/op
OverheadActual  14: 8192000 op, 25485.00 ns, 0.0031 ns/op
OverheadActual  15: 8192000 op, 24718.00 ns, 0.0030 ns/op

WorkloadWarmup   1: 8192000 op, 610444952.00 ns, 74.5172 ns/op
WorkloadWarmup   2: 8192000 op, 608599972.00 ns, 74.2920 ns/op
WorkloadWarmup   3: 8192000 op, 605240220.00 ns, 73.8819 ns/op
WorkloadWarmup   4: 8192000 op, 603724563.00 ns, 73.6968 ns/op
WorkloadWarmup   5: 8192000 op, 603772928.00 ns, 73.7028 ns/op
WorkloadWarmup   6: 8192000 op, 604018408.00 ns, 73.7327 ns/op
WorkloadWarmup   7: 8192000 op, 604084700.00 ns, 73.7408 ns/op
WorkloadWarmup   8: 8192000 op, 603640265.00 ns, 73.6866 ns/op
WorkloadWarmup   9: 8192000 op, 604500036.00 ns, 73.7915 ns/op
WorkloadWarmup  10: 8192000 op, 605529909.00 ns, 73.9172 ns/op
WorkloadWarmup  11: 8192000 op, 603709517.00 ns, 73.6950 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 608010251.00 ns, 74.2200 ns/op
WorkloadActual   2: 8192000 op, 604646037.00 ns, 73.8093 ns/op
WorkloadActual   3: 8192000 op, 608787950.00 ns, 74.3149 ns/op
WorkloadActual   4: 8192000 op, 604737363.00 ns, 73.8205 ns/op
WorkloadActual   5: 8192000 op, 605005286.00 ns, 73.8532 ns/op
WorkloadActual   6: 8192000 op, 605692137.00 ns, 73.9370 ns/op
WorkloadActual   7: 8192000 op, 605330351.00 ns, 73.8929 ns/op
WorkloadActual   8: 8192000 op, 607314684.00 ns, 74.1351 ns/op
WorkloadActual   9: 8192000 op, 605291747.00 ns, 73.8882 ns/op
WorkloadActual  10: 8192000 op, 604676436.00 ns, 73.8130 ns/op
WorkloadActual  11: 8192000 op, 606715514.00 ns, 74.0620 ns/op
WorkloadActual  12: 8192000 op, 605947436.00 ns, 73.9682 ns/op
WorkloadActual  13: 8192000 op, 609433649.00 ns, 74.3938 ns/op
WorkloadActual  14: 8192000 op, 604778963.00 ns, 73.8256 ns/op
WorkloadActual  15: 8192000 op, 603635972.00 ns, 73.6860 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 607984787.00 ns, 74.2169 ns/op
WorkloadResult   2: 8192000 op, 604620573.00 ns, 73.8062 ns/op
WorkloadResult   3: 8192000 op, 608762486.00 ns, 74.3118 ns/op
WorkloadResult   4: 8192000 op, 604711899.00 ns, 73.8174 ns/op
WorkloadResult   5: 8192000 op, 604979822.00 ns, 73.8501 ns/op
WorkloadResult   6: 8192000 op, 605666673.00 ns, 73.9339 ns/op
WorkloadResult   7: 8192000 op, 605304887.00 ns, 73.8898 ns/op
WorkloadResult   8: 8192000 op, 607289220.00 ns, 74.1320 ns/op
WorkloadResult   9: 8192000 op, 605266283.00 ns, 73.8850 ns/op
WorkloadResult  10: 8192000 op, 604650972.00 ns, 73.8099 ns/op
WorkloadResult  11: 8192000 op, 606690050.00 ns, 74.0588 ns/op
WorkloadResult  12: 8192000 op, 605921972.00 ns, 73.9651 ns/op
WorkloadResult  13: 8192000 op, 609408185.00 ns, 74.3906 ns/op
WorkloadResult  14: 8192000 op, 604753499.00 ns, 73.8224 ns/op
WorkloadResult  15: 8192000 op, 603610508.00 ns, 73.6829 ns/op
// GC:  31 0 0 786432032 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4290 has exited with code 0.

Mean = 73.972 ns, StdErr = 0.053 ns (0.07%), N = 15, StdDev = 0.207 ns
Min = 73.683 ns, Q1 = 73.820 ns, Median = 73.890 ns, Q3 = 74.095 ns, Max = 74.391 ns
IQR = 0.276 ns, LowerFence = 73.407 ns, UpperFence = 74.509 ns
ConfidenceInterval = [73.751 ns; 74.193 ns] (CI 99.9%), Margin = 0.221 ns (0.30% of Mean)
Skewness = 0.68, Kurtosis = 2.13, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-04 16:28 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Stream --job RunStrategy=Throughput --benchmarkId 3 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 169325.00 ns, 169.3250 ns/op
WorkloadJitting  1: 1000 op, 1959711.00 ns, 1.9597 us/op

OverheadJitting  2: 16000 op, 241767.00 ns, 15.1104 ns/op
WorkloadJitting  2: 16000 op, 15226853.00 ns, 951.6783 ns/op

WorkloadPilot    1: 16000 op, 13555122.00 ns, 847.1951 ns/op
WorkloadPilot    2: 32000 op, 24878891.00 ns, 777.4653 ns/op
WorkloadPilot    3: 64000 op, 49649167.00 ns, 775.7682 ns/op
WorkloadPilot    4: 128000 op, 75183996.00 ns, 587.3750 ns/op
WorkloadPilot    5: 256000 op, 44957487.00 ns, 175.6152 ns/op
WorkloadPilot    6: 512000 op, 86224578.00 ns, 168.4074 ns/op
WorkloadPilot    7: 1024000 op, 171560443.00 ns, 167.5395 ns/op
WorkloadPilot    8: 2048000 op, 341860481.00 ns, 166.9241 ns/op
WorkloadPilot    9: 4096000 op, 682295902.00 ns, 166.5761 ns/op

OverheadWarmup   1: 4096000 op, 14739.00 ns, 0.0036 ns/op
OverheadWarmup   2: 4096000 op, 12986.00 ns, 0.0032 ns/op
OverheadWarmup   3: 4096000 op, 12915.00 ns, 0.0032 ns/op
OverheadWarmup   4: 4096000 op, 12900.00 ns, 0.0031 ns/op
OverheadWarmup   5: 4096000 op, 13002.00 ns, 0.0032 ns/op
OverheadWarmup   6: 4096000 op, 12968.00 ns, 0.0032 ns/op
OverheadWarmup   7: 4096000 op, 12959.00 ns, 0.0032 ns/op
OverheadWarmup   8: 4096000 op, 12924.00 ns, 0.0032 ns/op
OverheadWarmup   9: 4096000 op, 12822.00 ns, 0.0031 ns/op
OverheadWarmup  10: 4096000 op, 12961.00 ns, 0.0032 ns/op

OverheadActual   1: 4096000 op, 13094.00 ns, 0.0032 ns/op
OverheadActual   2: 4096000 op, 12686.00 ns, 0.0031 ns/op
OverheadActual   3: 4096000 op, 13049.00 ns, 0.0032 ns/op
OverheadActual   4: 4096000 op, 13014.00 ns, 0.0032 ns/op
OverheadActual   5: 4096000 op, 13043.00 ns, 0.0032 ns/op
OverheadActual   6: 4096000 op, 12937.00 ns, 0.0032 ns/op
OverheadActual   7: 4096000 op, 14156.00 ns, 0.0035 ns/op
OverheadActual   8: 4096000 op, 30540.00 ns, 0.0075 ns/op
OverheadActual   9: 4096000 op, 12969.00 ns, 0.0032 ns/op
OverheadActual  10: 4096000 op, 12964.00 ns, 0.0032 ns/op
OverheadActual  11: 4096000 op, 12978.00 ns, 0.0032 ns/op
OverheadActual  12: 4096000 op, 13096.00 ns, 0.0032 ns/op
OverheadActual  13: 4096000 op, 12950.00 ns, 0.0032 ns/op
OverheadActual  14: 4096000 op, 12940.00 ns, 0.0032 ns/op
OverheadActual  15: 4096000 op, 12578.00 ns, 0.0031 ns/op

WorkloadWarmup   1: 4096000 op, 693203371.00 ns, 169.2391 ns/op
WorkloadWarmup   2: 4096000 op, 691517818.00 ns, 168.8276 ns/op
WorkloadWarmup   3: 4096000 op, 682621258.00 ns, 166.6556 ns/op
WorkloadWarmup   4: 4096000 op, 681824317.00 ns, 166.4610 ns/op
WorkloadWarmup   5: 4096000 op, 684635956.00 ns, 167.1475 ns/op
WorkloadWarmup   6: 4096000 op, 681576691.00 ns, 166.4006 ns/op
WorkloadWarmup   7: 4096000 op, 681368405.00 ns, 166.3497 ns/op
WorkloadWarmup   8: 4096000 op, 680770169.00 ns, 166.2037 ns/op
WorkloadWarmup   9: 4096000 op, 680935221.00 ns, 166.2440 ns/op
WorkloadWarmup  10: 4096000 op, 681880646.00 ns, 166.4748 ns/op
WorkloadWarmup  11: 4096000 op, 681476641.00 ns, 166.3761 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 683360030.00 ns, 166.8359 ns/op
WorkloadActual   2: 4096000 op, 681988913.00 ns, 166.5012 ns/op
WorkloadActual   3: 4096000 op, 680990853.00 ns, 166.2575 ns/op
WorkloadActual   4: 4096000 op, 683241873.00 ns, 166.8071 ns/op
WorkloadActual   5: 4096000 op, 683509533.00 ns, 166.8724 ns/op
WorkloadActual   6: 4096000 op, 682206346.00 ns, 166.5543 ns/op
WorkloadActual   7: 4096000 op, 682340766.00 ns, 166.5871 ns/op
WorkloadActual   8: 4096000 op, 682441484.00 ns, 166.6117 ns/op
WorkloadActual   9: 4096000 op, 682359877.00 ns, 166.5918 ns/op
WorkloadActual  10: 4096000 op, 683492413.00 ns, 166.8683 ns/op
WorkloadActual  11: 4096000 op, 682019026.00 ns, 166.5086 ns/op
WorkloadActual  12: 4096000 op, 681920742.00 ns, 166.4846 ns/op
WorkloadActual  13: 4096000 op, 681027459.00 ns, 166.2665 ns/op
WorkloadActual  14: 4096000 op, 685099970.00 ns, 167.2607 ns/op
WorkloadActual  15: 4096000 op, 685827845.00 ns, 167.4384 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 683347052.00 ns, 166.8328 ns/op
WorkloadResult   2: 4096000 op, 681975935.00 ns, 166.4980 ns/op
WorkloadResult   3: 4096000 op, 680977875.00 ns, 166.2544 ns/op
WorkloadResult   4: 4096000 op, 683228895.00 ns, 166.8039 ns/op
WorkloadResult   5: 4096000 op, 683496555.00 ns, 166.8693 ns/op
WorkloadResult   6: 4096000 op, 682193368.00 ns, 166.5511 ns/op
WorkloadResult   7: 4096000 op, 682327788.00 ns, 166.5839 ns/op
WorkloadResult   8: 4096000 op, 682428506.00 ns, 166.6085 ns/op
WorkloadResult   9: 4096000 op, 682346899.00 ns, 166.5886 ns/op
WorkloadResult  10: 4096000 op, 683479435.00 ns, 166.8651 ns/op
WorkloadResult  11: 4096000 op, 682006048.00 ns, 166.5054 ns/op
WorkloadResult  12: 4096000 op, 681907764.00 ns, 166.4814 ns/op
WorkloadResult  13: 4096000 op, 681014481.00 ns, 166.2633 ns/op
WorkloadResult  14: 4096000 op, 685086992.00 ns, 167.2576 ns/op
// GC:  31 0 0 786432032 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4304 has exited with code 0.

Mean = 166.640 ns, StdErr = 0.071 ns (0.04%), N = 14, StdDev = 0.266 ns
Min = 166.254 ns, Q1 = 166.500 ns, Median = 166.586 ns, Q3 = 166.826 ns, Max = 167.258 ns
IQR = 0.326 ns, LowerFence = 166.011 ns, UpperFence = 167.314 ns
ConfidenceInterval = [166.341 ns; 166.940 ns] (CI 99.9%), Margin = 0.300 ns (0.18% of Mean)
Skewness = 0.57, Kurtosis = 2.82, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-04 16:28 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Command --job ShortRun --benchmarkId 4 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 210033.00 ns, 210.0330 ns/op
WorkloadJitting  1: 1000 op, 1235974.00 ns, 1.2360 us/op

OverheadJitting  2: 16000 op, 222721.00 ns, 13.9201 ns/op
WorkloadJitting  2: 16000 op, 6065313.00 ns, 379.0821 ns/op

WorkloadPilot    1: 16000 op, 4362509.00 ns, 272.6568 ns/op
WorkloadPilot    2: 32000 op, 8427154.00 ns, 263.3486 ns/op
WorkloadPilot    3: 64000 op, 16460638.00 ns, 257.1975 ns/op
WorkloadPilot    4: 128000 op, 33182004.00 ns, 259.2344 ns/op
WorkloadPilot    5: 256000 op, 67812942.00 ns, 264.8943 ns/op
WorkloadPilot    6: 512000 op, 49959269.00 ns, 97.5767 ns/op
WorkloadPilot    7: 1024000 op, 64839643.00 ns, 63.3200 ns/op
WorkloadPilot    8: 2048000 op, 127871602.00 ns, 62.4373 ns/op
WorkloadPilot    9: 4096000 op, 255990804.00 ns, 62.4978 ns/op
WorkloadPilot   10: 8192000 op, 511909423.00 ns, 62.4889 ns/op

OverheadWarmup   1: 8192000 op, 27925.00 ns, 0.0034 ns/op
OverheadWarmup   2: 8192000 op, 25753.00 ns, 0.0031 ns/op
OverheadWarmup   3: 8192000 op, 25502.00 ns, 0.0031 ns/op
OverheadWarmup   4: 8192000 op, 25493.00 ns, 0.0031 ns/op
OverheadWarmup   5: 8192000 op, 25503.00 ns, 0.0031 ns/op
OverheadWarmup   6: 8192000 op, 25609.00 ns, 0.0031 ns/op
OverheadWarmup   7: 8192000 op, 25590.00 ns, 0.0031 ns/op
OverheadWarmup   8: 8192000 op, 27258.00 ns, 0.0033 ns/op
OverheadWarmup   9: 8192000 op, 32195.00 ns, 0.0039 ns/op
OverheadWarmup  10: 8192000 op, 27458.00 ns, 0.0034 ns/op

OverheadActual   1: 8192000 op, 27754.00 ns, 0.0034 ns/op
OverheadActual   2: 8192000 op, 27582.00 ns, 0.0034 ns/op
OverheadActual   3: 8192000 op, 28047.00 ns, 0.0034 ns/op
OverheadActual   4: 8192000 op, 27722.00 ns, 0.0034 ns/op
OverheadActual   5: 8192000 op, 28070.00 ns, 0.0034 ns/op
OverheadActual   6: 8192000 op, 27568.00 ns, 0.0034 ns/op
OverheadActual   7: 8192000 op, 28742.00 ns, 0.0035 ns/op
OverheadActual   8: 8192000 op, 27234.00 ns, 0.0033 ns/op
OverheadActual   9: 8192000 op, 27492.00 ns, 0.0034 ns/op
OverheadActual  10: 8192000 op, 27596.00 ns, 0.0034 ns/op
OverheadActual  11: 8192000 op, 28438.00 ns, 0.0035 ns/op
OverheadActual  12: 8192000 op, 28622.00 ns, 0.0035 ns/op
OverheadActual  13: 8192000 op, 25567.00 ns, 0.0031 ns/op
OverheadActual  14: 8192000 op, 25487.00 ns, 0.0031 ns/op
OverheadActual  15: 8192000 op, 26505.00 ns, 0.0032 ns/op

WorkloadWarmup   1: 8192000 op, 548647308.00 ns, 66.9735 ns/op
WorkloadWarmup   2: 8192000 op, 517014620.00 ns, 63.1121 ns/op
WorkloadWarmup   3: 8192000 op, 510912902.00 ns, 62.3673 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 513849253.00 ns, 62.7257 ns/op
WorkloadActual   2: 8192000 op, 511576118.00 ns, 62.4483 ns/op
WorkloadActual   3: 8192000 op, 511858062.00 ns, 62.4827 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 513821657.00 ns, 62.7224 ns/op
WorkloadResult   2: 8192000 op, 511548522.00 ns, 62.4449 ns/op
WorkloadResult   3: 8192000 op, 511830466.00 ns, 62.4793 ns/op
// GC:  7 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4316 has exited with code 0.

Mean = 62.549 ns, StdErr = 0.087 ns (0.14%), N = 3, StdDev = 0.151 ns
Min = 62.445 ns, Q1 = 62.462 ns, Median = 62.479 ns, Q3 = 62.601 ns, Max = 62.722 ns
IQR = 0.139 ns, LowerFence = 62.254 ns, UpperFence = 62.809 ns
ConfidenceInterval = [59.789 ns; 65.308 ns] (CI 99.9%), Margin = 2.759 ns (4.41% of Mean)
Skewness = 0.36, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-04 16:28 (0h 0m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Notification --job ShortRun --benchmarkId 5 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 205105.00 ns, 205.1050 ns/op
WorkloadJitting  1: 1000 op, 1250750.00 ns, 1.2508 us/op

OverheadJitting  2: 16000 op, 208169.00 ns, 13.0106 ns/op
WorkloadJitting  2: 16000 op, 10314847.00 ns, 644.6779 ns/op

WorkloadPilot    1: 16000 op, 8743886.00 ns, 546.4929 ns/op
WorkloadPilot    2: 32000 op, 15863091.00 ns, 495.7216 ns/op
WorkloadPilot    3: 64000 op, 30314155.00 ns, 473.6587 ns/op
WorkloadPilot    4: 128000 op, 55477651.00 ns, 433.4191 ns/op
WorkloadPilot    5: 256000 op, 68699847.00 ns, 268.3588 ns/op
WorkloadPilot    6: 512000 op, 66638554.00 ns, 130.1534 ns/op
WorkloadPilot    7: 1024000 op, 132894998.00 ns, 129.7803 ns/op
WorkloadPilot    8: 2048000 op, 265094890.00 ns, 129.4409 ns/op
WorkloadPilot    9: 4096000 op, 532790804.00 ns, 130.0759 ns/op

OverheadWarmup   1: 4096000 op, 16555.00 ns, 0.0040 ns/op
OverheadWarmup   2: 4096000 op, 15446.00 ns, 0.0038 ns/op
OverheadWarmup   3: 4096000 op, 15051.00 ns, 0.0037 ns/op
OverheadWarmup   4: 4096000 op, 15144.00 ns, 0.0037 ns/op
OverheadWarmup   5: 4096000 op, 14380.00 ns, 0.0035 ns/op
OverheadWarmup   6: 4096000 op, 14707.00 ns, 0.0036 ns/op
OverheadWarmup   7: 4096000 op, 14628.00 ns, 0.0036 ns/op

OverheadActual   1: 4096000 op, 15180.00 ns, 0.0037 ns/op
OverheadActual   2: 4096000 op, 15032.00 ns, 0.0037 ns/op
OverheadActual   3: 4096000 op, 15188.00 ns, 0.0037 ns/op
OverheadActual   4: 4096000 op, 14900.00 ns, 0.0036 ns/op
OverheadActual   5: 4096000 op, 15339.00 ns, 0.0037 ns/op
OverheadActual   6: 4096000 op, 14579.00 ns, 0.0036 ns/op
OverheadActual   7: 4096000 op, 14676.00 ns, 0.0036 ns/op
OverheadActual   8: 4096000 op, 15243.00 ns, 0.0037 ns/op
OverheadActual   9: 4096000 op, 15288.00 ns, 0.0037 ns/op
OverheadActual  10: 4096000 op, 15759.00 ns, 0.0038 ns/op
OverheadActual  11: 4096000 op, 14796.00 ns, 0.0036 ns/op
OverheadActual  12: 4096000 op, 15197.00 ns, 0.0037 ns/op
OverheadActual  13: 4096000 op, 14919.00 ns, 0.0036 ns/op
OverheadActual  14: 4096000 op, 15480.00 ns, 0.0038 ns/op
OverheadActual  15: 4096000 op, 14782.00 ns, 0.0036 ns/op

WorkloadWarmup   1: 4096000 op, 537519651.00 ns, 131.2304 ns/op
WorkloadWarmup   2: 4096000 op, 539523053.00 ns, 131.7195 ns/op
WorkloadWarmup   3: 4096000 op, 530873761.00 ns, 129.6079 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 528198681.00 ns, 128.9548 ns/op
WorkloadActual   2: 4096000 op, 525615983.00 ns, 128.3242 ns/op
WorkloadActual   3: 4096000 op, 525950678.00 ns, 128.4059 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 528183501.00 ns, 128.9511 ns/op
WorkloadResult   2: 4096000 op, 525600803.00 ns, 128.3205 ns/op
WorkloadResult   3: 4096000 op, 525935498.00 ns, 128.4022 ns/op
// GC:  43 0 0 1081344000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4326 has exited with code 0.

Mean = 128.558 ns, StdErr = 0.198 ns (0.15%), N = 3, StdDev = 0.343 ns
Min = 128.321 ns, Q1 = 128.361 ns, Median = 128.402 ns, Q3 = 128.677 ns, Max = 128.951 ns
IQR = 0.315 ns, LowerFence = 127.888 ns, UpperFence = 129.150 ns
ConfidenceInterval = [122.302 ns; 134.814 ns] (CI 99.9%), Margin = 6.256 ns (4.87% of Mean)
Skewness = 0.36, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-04 16:28 (0h 0m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Request --job ShortRun --benchmarkId 6 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 208766.00 ns, 208.7660 ns/op
WorkloadJitting  1: 1000 op, 1367337.00 ns, 1.3673 us/op

OverheadJitting  2: 16000 op, 214761.00 ns, 13.4226 ns/op
WorkloadJitting  2: 16000 op, 7323123.00 ns, 457.6952 ns/op

WorkloadPilot    1: 16000 op, 5927210.00 ns, 370.4506 ns/op
WorkloadPilot    2: 32000 op, 11013331.00 ns, 344.1666 ns/op
WorkloadPilot    3: 64000 op, 21904001.00 ns, 342.2500 ns/op
WorkloadPilot    4: 128000 op, 44565353.00 ns, 348.1668 ns/op
WorkloadPilot    5: 256000 op, 83543751.00 ns, 326.3428 ns/op
WorkloadPilot    6: 512000 op, 42972412.00 ns, 83.9305 ns/op
WorkloadPilot    7: 1024000 op, 77905245.00 ns, 76.0793 ns/op
WorkloadPilot    8: 2048000 op, 155590516.00 ns, 75.9719 ns/op
WorkloadPilot    9: 4096000 op, 311597339.00 ns, 76.0736 ns/op
WorkloadPilot   10: 8192000 op, 624536598.00 ns, 76.2374 ns/op

OverheadWarmup   1: 8192000 op, 27817.00 ns, 0.0034 ns/op
OverheadWarmup   2: 8192000 op, 27234.00 ns, 0.0033 ns/op
OverheadWarmup   3: 8192000 op, 27381.00 ns, 0.0033 ns/op
OverheadWarmup   4: 8192000 op, 27215.00 ns, 0.0033 ns/op
OverheadWarmup   5: 8192000 op, 27922.00 ns, 0.0034 ns/op
OverheadWarmup   6: 8192000 op, 27651.00 ns, 0.0034 ns/op

OverheadActual   1: 8192000 op, 27483.00 ns, 0.0034 ns/op
OverheadActual   2: 8192000 op, 27992.00 ns, 0.0034 ns/op
OverheadActual   3: 8192000 op, 29179.00 ns, 0.0036 ns/op
OverheadActual   4: 8192000 op, 26736.00 ns, 0.0033 ns/op
OverheadActual   5: 8192000 op, 27576.00 ns, 0.0034 ns/op
OverheadActual   6: 8192000 op, 28018.00 ns, 0.0034 ns/op
OverheadActual   7: 8192000 op, 26357.00 ns, 0.0032 ns/op
OverheadActual   8: 8192000 op, 27272.00 ns, 0.0033 ns/op
OverheadActual   9: 8192000 op, 27329.00 ns, 0.0033 ns/op
OverheadActual  10: 8192000 op, 26981.00 ns, 0.0033 ns/op
OverheadActual  11: 8192000 op, 28960.00 ns, 0.0035 ns/op
OverheadActual  12: 8192000 op, 27827.00 ns, 0.0034 ns/op
OverheadActual  13: 8192000 op, 27747.00 ns, 0.0034 ns/op
OverheadActual  14: 8192000 op, 27347.00 ns, 0.0033 ns/op
OverheadActual  15: 8192000 op, 27504.00 ns, 0.0034 ns/op

WorkloadWarmup   1: 8192000 op, 637082227.00 ns, 77.7688 ns/op
WorkloadWarmup   2: 8192000 op, 631438755.00 ns, 77.0799 ns/op
WorkloadWarmup   3: 8192000 op, 625793227.00 ns, 76.3908 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 625285905.00 ns, 76.3288 ns/op
WorkloadActual   2: 8192000 op, 627578416.00 ns, 76.6087 ns/op
WorkloadActual   3: 8192000 op, 626134591.00 ns, 76.4324 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 625258401.00 ns, 76.3255 ns/op
WorkloadResult   2: 8192000 op, 627550912.00 ns, 76.6053 ns/op
WorkloadResult   3: 8192000 op, 626107087.00 ns, 76.4291 ns/op
// GC:  31 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4338 has exited with code 0.

Mean = 76.453 ns, StdErr = 0.082 ns (0.11%), N = 3, StdDev = 0.141 ns
Min = 76.325 ns, Q1 = 76.377 ns, Median = 76.429 ns, Q3 = 76.517 ns, Max = 76.605 ns
IQR = 0.140 ns, LowerFence = 76.167 ns, UpperFence = 76.727 ns
ConfidenceInterval = [73.872 ns; 79.035 ns] (CI 99.9%), Margin = 2.581 ns (3.38% of Mean)
Skewness = 0.17, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-04 16:28 (0h 0m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 140 141 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Stream --job ShortRun --benchmarkId 7 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 196372.00 ns, 196.3720 ns/op
WorkloadJitting  1: 1000 op, 1946926.00 ns, 1.9469 us/op

OverheadJitting  2: 16000 op, 215326.00 ns, 13.4579 ns/op
WorkloadJitting  2: 16000 op, 14818995.00 ns, 926.1872 ns/op

WorkloadPilot    1: 16000 op, 12692535.00 ns, 793.2834 ns/op
WorkloadPilot    2: 32000 op, 23763084.00 ns, 742.5964 ns/op
WorkloadPilot    3: 64000 op, 47017775.00 ns, 734.6527 ns/op
WorkloadPilot    4: 128000 op, 74573197.00 ns, 582.6031 ns/op
WorkloadPilot    5: 256000 op, 46024986.00 ns, 179.7851 ns/op
WorkloadPilot    6: 512000 op, 85678621.00 ns, 167.3411 ns/op
WorkloadPilot    7: 1024000 op, 172070403.00 ns, 168.0375 ns/op
WorkloadPilot    8: 2048000 op, 344999889.00 ns, 168.4570 ns/op
WorkloadPilot    9: 4096000 op, 687790129.00 ns, 167.9175 ns/op

OverheadWarmup   1: 4096000 op, 17708.00 ns, 0.0043 ns/op
OverheadWarmup   2: 4096000 op, 14948.00 ns, 0.0036 ns/op
OverheadWarmup   3: 4096000 op, 14484.00 ns, 0.0035 ns/op
OverheadWarmup   4: 4096000 op, 14509.00 ns, 0.0035 ns/op
OverheadWarmup   5: 4096000 op, 14385.00 ns, 0.0035 ns/op
OverheadWarmup   6: 4096000 op, 14542.00 ns, 0.0036 ns/op
OverheadWarmup   7: 4096000 op, 14251.00 ns, 0.0035 ns/op

OverheadActual   1: 4096000 op, 14993.00 ns, 0.0037 ns/op
OverheadActual   2: 4096000 op, 14831.00 ns, 0.0036 ns/op
OverheadActual   3: 4096000 op, 15076.00 ns, 0.0037 ns/op
OverheadActual   4: 4096000 op, 14620.00 ns, 0.0036 ns/op
OverheadActual   5: 4096000 op, 14691.00 ns, 0.0036 ns/op
OverheadActual   6: 4096000 op, 14316.00 ns, 0.0035 ns/op
OverheadActual   7: 4096000 op, 14411.00 ns, 0.0035 ns/op
OverheadActual   8: 4096000 op, 14378.00 ns, 0.0035 ns/op
OverheadActual   9: 4096000 op, 14603.00 ns, 0.0036 ns/op
OverheadActual  10: 4096000 op, 16124.00 ns, 0.0039 ns/op
OverheadActual  11: 4096000 op, 14355.00 ns, 0.0035 ns/op
OverheadActual  12: 4096000 op, 14539.00 ns, 0.0035 ns/op
OverheadActual  13: 4096000 op, 13109.00 ns, 0.0032 ns/op
OverheadActual  14: 4096000 op, 13078.00 ns, 0.0032 ns/op
OverheadActual  15: 4096000 op, 13069.00 ns, 0.0032 ns/op
OverheadActual  16: 4096000 op, 13053.00 ns, 0.0032 ns/op
OverheadActual  17: 4096000 op, 13065.00 ns, 0.0032 ns/op
OverheadActual  18: 4096000 op, 13064.00 ns, 0.0032 ns/op
OverheadActual  19: 4096000 op, 13029.00 ns, 0.0032 ns/op
OverheadActual  20: 4096000 op, 13040.00 ns, 0.0032 ns/op

WorkloadWarmup   1: 4096000 op, 703130869.00 ns, 171.6628 ns/op
WorkloadWarmup   2: 4096000 op, 696507967.00 ns, 170.0459 ns/op
WorkloadWarmup   3: 4096000 op, 687876061.00 ns, 167.9385 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 694130997.00 ns, 169.4656 ns/op
WorkloadActual   2: 4096000 op, 691124495.00 ns, 168.7316 ns/op
WorkloadActual   3: 4096000 op, 689519334.00 ns, 168.3397 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 694116630.50 ns, 169.4621 ns/op
WorkloadResult   2: 4096000 op, 691110128.50 ns, 168.7281 ns/op
WorkloadResult   3: 4096000 op, 689504967.50 ns, 168.3362 ns/op
// GC:  31 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4347 has exited with code 0.

Mean = 168.842 ns, StdErr = 0.330 ns (0.20%), N = 3, StdDev = 0.572 ns
Min = 168.336 ns, Q1 = 168.532 ns, Median = 168.728 ns, Q3 = 169.095 ns, Max = 169.462 ns
IQR = 0.563 ns, LowerFence = 167.688 ns, UpperFence = 169.939 ns
ConfidenceInterval = [158.415 ns; 179.269 ns] (CI 99.9%), Margin = 10.427 ns (6.18% of Mean)
Skewness = 0.19, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-04 16:28 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 66.013 ns, StdErr = 0.052 ns (0.08%), N = 15, StdDev = 0.201 ns
Min = 65.804 ns, Q1 = 65.842 ns, Median = 65.978 ns, Q3 = 66.086 ns, Max = 66.443 ns
IQR = 0.243 ns, LowerFence = 65.478 ns, UpperFence = 66.451 ns
ConfidenceInterval = [65.797 ns; 66.228 ns] (CI 99.9%), Margin = 0.215 ns (0.33% of Mean)
Skewness = 0.75, Kurtosis = 2.35, MValue = 2
-------------------- Histogram --------------------
[65.696 ns ; 66.551 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 127.096 ns, StdErr = 0.244 ns (0.19%), N = 14, StdDev = 0.914 ns
Min = 125.411 ns, Q1 = 126.566 ns, Median = 127.156 ns, Q3 = 127.544 ns, Max = 128.932 ns
IQR = 0.978 ns, LowerFence = 125.099 ns, UpperFence = 129.011 ns
ConfidenceInterval = [126.065 ns; 128.127 ns] (CI 99.9%), Margin = 1.031 ns (0.81% of Mean)
Skewness = -0.02, Kurtosis = 2.42, MValue = 2
-------------------- Histogram --------------------
[124.913 ns ; 129.430 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 73.972 ns, StdErr = 0.053 ns (0.07%), N = 15, StdDev = 0.207 ns
Min = 73.683 ns, Q1 = 73.820 ns, Median = 73.890 ns, Q3 = 74.095 ns, Max = 74.391 ns
IQR = 0.276 ns, LowerFence = 73.407 ns, UpperFence = 74.509 ns
ConfidenceInterval = [73.751 ns; 74.193 ns] (CI 99.9%), Margin = 0.221 ns (0.30% of Mean)
Skewness = 0.68, Kurtosis = 2.13, MValue = 2
-------------------- Histogram --------------------
[73.573 ns ; 74.501 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 166.640 ns, StdErr = 0.071 ns (0.04%), N = 14, StdDev = 0.266 ns
Min = 166.254 ns, Q1 = 166.500 ns, Median = 166.586 ns, Q3 = 166.826 ns, Max = 167.258 ns
IQR = 0.326 ns, LowerFence = 166.011 ns, UpperFence = 167.314 ns
ConfidenceInterval = [166.341 ns; 166.940 ns] (CI 99.9%), Margin = 0.300 ns (0.18% of Mean)
Skewness = 0.57, Kurtosis = 2.82, MValue = 2
-------------------- Histogram --------------------
[166.110 ns ; 167.402 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 62.549 ns, StdErr = 0.087 ns (0.14%), N = 3, StdDev = 0.151 ns
Min = 62.445 ns, Q1 = 62.462 ns, Median = 62.479 ns, Q3 = 62.601 ns, Max = 62.722 ns
IQR = 0.139 ns, LowerFence = 62.254 ns, UpperFence = 62.809 ns
ConfidenceInterval = [59.789 ns; 65.308 ns] (CI 99.9%), Margin = 2.759 ns (4.41% of Mean)
Skewness = 0.36, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[62.307 ns ; 62.860 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 128.558 ns, StdErr = 0.198 ns (0.15%), N = 3, StdDev = 0.343 ns
Min = 128.321 ns, Q1 = 128.361 ns, Median = 128.402 ns, Q3 = 128.677 ns, Max = 128.951 ns
IQR = 0.315 ns, LowerFence = 127.888 ns, UpperFence = 129.150 ns
ConfidenceInterval = [122.302 ns; 134.814 ns] (CI 99.9%), Margin = 6.256 ns (4.87% of Mean)
Skewness = 0.36, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[128.008 ns ; 129.263 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 76.453 ns, StdErr = 0.082 ns (0.11%), N = 3, StdDev = 0.141 ns
Min = 76.325 ns, Q1 = 76.377 ns, Median = 76.429 ns, Q3 = 76.517 ns, Max = 76.605 ns
IQR = 0.140 ns, LowerFence = 76.167 ns, UpperFence = 76.727 ns
ConfidenceInterval = [73.872 ns; 79.035 ns] (CI 99.9%), Margin = 2.581 ns (3.38% of Mean)
Skewness = 0.17, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[76.197 ns ; 76.734 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 168.842 ns, StdErr = 0.330 ns (0.20%), N = 3, StdDev = 0.572 ns
Min = 168.336 ns, Q1 = 168.532 ns, Median = 168.728 ns, Q3 = 169.095 ns, Max = 169.462 ns
IQR = 0.563 ns, LowerFence = 167.688 ns, UpperFence = 169.939 ns
ConfidenceInterval = [158.415 ns; 179.269 ns] (CI 99.9%), Margin = 10.427 ns (6.18% of Mean)
Skewness = 0.19, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[167.816 ns ; 169.982 ns) | @@@
---------------------------------------------------

// * Summary *

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Platinum 8370C CPU 2.80GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4


| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error     | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|----------:|---------:|-------:|----------:|
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  66.01 ns |  0.215 ns | 0.201 ns | 0.0009 |      24 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 127.10 ns |  1.031 ns | 0.914 ns | 0.0105 |     264 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  73.97 ns |  0.221 ns | 0.207 ns | 0.0038 |      96 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 166.64 ns |  0.300 ns | 0.266 ns | 0.0076 |     192 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  62.55 ns |  2.759 ns | 0.151 ns | 0.0009 |      24 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 128.56 ns |  6.256 ns | 0.343 ns | 0.0105 |     264 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  76.45 ns |  2.581 ns | 0.141 ns | 0.0038 |      96 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 168.84 ns | 10.427 ns | 0.572 ns | 0.0076 |     192 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Notification  Notify': RunStrategy=Throughput                 -> 1 outlier  was  removed (131.40 ns)
  CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': RunStrategy=Throughput -> 1 outlier  was  removed (167.44 ns)
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
Run time: 00:01:31 (91.24 sec), executed benchmarks: 8

Global total time: 00:01:44 (104.69 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
