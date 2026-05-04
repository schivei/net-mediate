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


---


---

## Latest CI Benchmark Run

Run: 2026-05-04 15:36 UTC | Branch: copilot/implement-medium-term | Commit: 0917cf6

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.87GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error     | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|----------:|---------:|-------:|----------:|
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  65.03 ns |  0.220 ns | 0.184 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 114.93 ns |  0.358 ns | 0.335 ns | 0.0157 |     264 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  73.20 ns |  0.148 ns | 0.123 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 167.70 ns |  0.465 ns | 0.412 ns | 0.0115 |     192 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  72.98 ns |  2.367 ns | 0.130 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 111.05 ns |  4.988 ns | 0.273 ns | 0.0157 |     264 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  73.26 ns |  8.584 ns | 0.471 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 155.89 ns | 12.301 ns | 0.674 ns | 0.0115 |     192 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.65 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 12.96 sec and exited with 0
// ***** Done, took 00:00:14 (14.67 sec)   *****
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

OverheadJitting  1: 1000 op, 196195.00 ns, 196.1950 ns/op
WorkloadJitting  1: 1000 op, 1135076.00 ns, 1.1351 us/op

OverheadJitting  2: 16000 op, 221413.00 ns, 13.8383 ns/op
WorkloadJitting  2: 16000 op, 6642982.00 ns, 415.1864 ns/op

WorkloadPilot    1: 16000 op, 5259012.00 ns, 328.6883 ns/op
WorkloadPilot    2: 32000 op, 9744562.00 ns, 304.5176 ns/op
WorkloadPilot    3: 64000 op, 19406921.00 ns, 303.2331 ns/op
WorkloadPilot    4: 128000 op, 41109422.00 ns, 321.1674 ns/op
WorkloadPilot    5: 256000 op, 65477417.00 ns, 255.7712 ns/op
WorkloadPilot    6: 512000 op, 36098521.00 ns, 70.5049 ns/op
WorkloadPilot    7: 1024000 op, 67076519.00 ns, 65.5044 ns/op
WorkloadPilot    8: 2048000 op, 133096592.00 ns, 64.9886 ns/op
WorkloadPilot    9: 4096000 op, 267030952.00 ns, 65.1931 ns/op
WorkloadPilot   10: 8192000 op, 532300727.00 ns, 64.9781 ns/op

OverheadWarmup   1: 8192000 op, 23896.00 ns, 0.0029 ns/op
OverheadWarmup   2: 8192000 op, 30055.00 ns, 0.0037 ns/op
OverheadWarmup   3: 8192000 op, 30556.00 ns, 0.0037 ns/op
OverheadWarmup   4: 8192000 op, 30486.00 ns, 0.0037 ns/op
OverheadWarmup   5: 8192000 op, 29705.00 ns, 0.0036 ns/op
OverheadWarmup   6: 8192000 op, 31818.00 ns, 0.0039 ns/op
OverheadWarmup   7: 8192000 op, 32109.00 ns, 0.0039 ns/op
OverheadWarmup   8: 8192000 op, 31657.00 ns, 0.0039 ns/op

OverheadActual   1: 8192000 op, 33501.00 ns, 0.0041 ns/op
OverheadActual   2: 8192000 op, 21692.00 ns, 0.0026 ns/op
OverheadActual   3: 8192000 op, 28172.00 ns, 0.0034 ns/op
OverheadActual   4: 8192000 op, 21282.00 ns, 0.0026 ns/op
OverheadActual   5: 8192000 op, 21292.00 ns, 0.0026 ns/op
OverheadActual   6: 8192000 op, 21162.00 ns, 0.0026 ns/op
OverheadActual   7: 8192000 op, 21151.00 ns, 0.0026 ns/op
OverheadActual   8: 8192000 op, 21172.00 ns, 0.0026 ns/op
OverheadActual   9: 8192000 op, 23075.00 ns, 0.0028 ns/op
OverheadActual  10: 8192000 op, 21172.00 ns, 0.0026 ns/op
OverheadActual  11: 8192000 op, 31327.00 ns, 0.0038 ns/op
OverheadActual  12: 8192000 op, 31407.00 ns, 0.0038 ns/op
OverheadActual  13: 8192000 op, 29064.00 ns, 0.0035 ns/op
OverheadActual  14: 8192000 op, 30926.00 ns, 0.0038 ns/op
OverheadActual  15: 8192000 op, 31878.00 ns, 0.0039 ns/op
OverheadActual  16: 8192000 op, 32809.00 ns, 0.0040 ns/op
OverheadActual  17: 8192000 op, 33600.00 ns, 0.0041 ns/op
OverheadActual  18: 8192000 op, 30956.00 ns, 0.0038 ns/op
OverheadActual  19: 8192000 op, 30716.00 ns, 0.0037 ns/op
OverheadActual  20: 8192000 op, 31206.00 ns, 0.0038 ns/op

WorkloadWarmup   1: 8192000 op, 543755440.00 ns, 66.3764 ns/op
WorkloadWarmup   2: 8192000 op, 540629657.00 ns, 65.9948 ns/op
WorkloadWarmup   3: 8192000 op, 534331889.00 ns, 65.2261 ns/op
WorkloadWarmup   4: 8192000 op, 535557279.00 ns, 65.3756 ns/op
WorkloadWarmup   5: 8192000 op, 533316354.00 ns, 65.1021 ns/op
WorkloadWarmup   6: 8192000 op, 532896593.00 ns, 65.0509 ns/op
WorkloadWarmup   7: 8192000 op, 531516019.00 ns, 64.8823 ns/op
WorkloadWarmup   8: 8192000 op, 532187208.00 ns, 64.9643 ns/op
WorkloadWarmup   9: 8192000 op, 531816020.00 ns, 64.9189 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 536667246.00 ns, 65.5111 ns/op
WorkloadActual   2: 8192000 op, 531936702.00 ns, 64.9337 ns/op
WorkloadActual   3: 8192000 op, 553568607.00 ns, 67.5743 ns/op
WorkloadActual   4: 8192000 op, 531784106.00 ns, 64.9151 ns/op
WorkloadActual   5: 8192000 op, 532219620.00 ns, 64.9682 ns/op
WorkloadActual   6: 8192000 op, 532349836.00 ns, 64.9841 ns/op
WorkloadActual   7: 8192000 op, 532240867.00 ns, 64.9708 ns/op
WorkloadActual   8: 8192000 op, 531765144.00 ns, 64.9127 ns/op
WorkloadActual   9: 8192000 op, 531172344.00 ns, 64.8404 ns/op
WorkloadActual  10: 8192000 op, 535118293.00 ns, 65.3221 ns/op
WorkloadActual  11: 8192000 op, 532077752.00 ns, 64.9509 ns/op
WorkloadActual  12: 8192000 op, 532901909.00 ns, 65.0515 ns/op
WorkloadActual  13: 8192000 op, 533036296.00 ns, 65.0679 ns/op
WorkloadActual  14: 8192000 op, 532654217.00 ns, 65.0213 ns/op
WorkloadActual  15: 8192000 op, 546981203.00 ns, 66.7702 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 536637356.00 ns, 65.5075 ns/op
WorkloadResult   2: 8192000 op, 531906812.00 ns, 64.9300 ns/op
WorkloadResult   3: 8192000 op, 531754216.00 ns, 64.9114 ns/op
WorkloadResult   4: 8192000 op, 532189730.00 ns, 64.9646 ns/op
WorkloadResult   5: 8192000 op, 532319946.00 ns, 64.9805 ns/op
WorkloadResult   6: 8192000 op, 532210977.00 ns, 64.9672 ns/op
WorkloadResult   7: 8192000 op, 531735254.00 ns, 64.9091 ns/op
WorkloadResult   8: 8192000 op, 531142454.00 ns, 64.8367 ns/op
WorkloadResult   9: 8192000 op, 535088403.00 ns, 65.3184 ns/op
WorkloadResult  10: 8192000 op, 532047862.00 ns, 64.9472 ns/op
WorkloadResult  11: 8192000 op, 532872019.00 ns, 65.0479 ns/op
WorkloadResult  12: 8192000 op, 533006406.00 ns, 65.0643 ns/op
WorkloadResult  13: 8192000 op, 532624327.00 ns, 65.0176 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4381 has exited with code 0.

Mean = 65.031 ns, StdErr = 0.051 ns (0.08%), N = 13, StdDev = 0.184 ns
Min = 64.837 ns, Q1 = 64.930 ns, Median = 64.967 ns, Q3 = 65.048 ns, Max = 65.507 ns
IQR = 0.118 ns, LowerFence = 64.753 ns, UpperFence = 65.225 ns
ConfidenceInterval = [64.811 ns; 65.251 ns] (CI 99.9%), Margin = 0.220 ns (0.34% of Mean)
Skewness = 1.47, Kurtosis = 4.05, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-04 15:36 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 184157.00 ns, 184.1570 ns/op
WorkloadJitting  1: 1000 op, 1239893.00 ns, 1.2399 us/op

OverheadJitting  2: 16000 op, 227232.00 ns, 14.2020 ns/op
WorkloadJitting  2: 16000 op, 11718678.00 ns, 732.4174 ns/op

WorkloadPilot    1: 16000 op, 9962519.00 ns, 622.6574 ns/op
WorkloadPilot    2: 32000 op, 17429419.00 ns, 544.6693 ns/op
WorkloadPilot    3: 64000 op, 30462602.00 ns, 475.9782 ns/op
WorkloadPilot    4: 128000 op, 56040730.00 ns, 437.8182 ns/op
WorkloadPilot    5: 256000 op, 58960371.00 ns, 230.3139 ns/op
WorkloadPilot    6: 512000 op, 59423137.00 ns, 116.0608 ns/op
WorkloadPilot    7: 1024000 op, 119358524.00 ns, 116.5611 ns/op
WorkloadPilot    8: 2048000 op, 236829611.00 ns, 115.6395 ns/op
WorkloadPilot    9: 4096000 op, 471609162.00 ns, 115.1390 ns/op
WorkloadPilot   10: 8192000 op, 969825342.00 ns, 118.3869 ns/op

OverheadWarmup   1: 8192000 op, 23896.00 ns, 0.0029 ns/op
OverheadWarmup   2: 8192000 op, 32158.00 ns, 0.0039 ns/op
OverheadWarmup   3: 8192000 op, 31157.00 ns, 0.0038 ns/op
OverheadWarmup   4: 8192000 op, 31577.00 ns, 0.0039 ns/op
OverheadWarmup   5: 8192000 op, 31979.00 ns, 0.0039 ns/op
OverheadWarmup   6: 8192000 op, 30786.00 ns, 0.0038 ns/op

OverheadActual   1: 8192000 op, 31818.00 ns, 0.0039 ns/op
OverheadActual   2: 8192000 op, 48783.00 ns, 0.0060 ns/op
OverheadActual   3: 8192000 op, 33801.00 ns, 0.0041 ns/op
OverheadActual   4: 8192000 op, 31527.00 ns, 0.0038 ns/op
OverheadActual   5: 8192000 op, 32249.00 ns, 0.0039 ns/op
OverheadActual   6: 8192000 op, 30536.00 ns, 0.0037 ns/op
OverheadActual   7: 8192000 op, 28543.00 ns, 0.0035 ns/op
OverheadActual   8: 8192000 op, 27451.00 ns, 0.0034 ns/op
OverheadActual   9: 8192000 op, 21312.00 ns, 0.0026 ns/op
OverheadActual  10: 8192000 op, 21131.00 ns, 0.0026 ns/op
OverheadActual  11: 8192000 op, 22414.00 ns, 0.0027 ns/op
OverheadActual  12: 8192000 op, 21222.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 21172.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadActual  15: 8192000 op, 21182.00 ns, 0.0026 ns/op
OverheadActual  16: 8192000 op, 21292.00 ns, 0.0026 ns/op
OverheadActual  17: 8192000 op, 21192.00 ns, 0.0026 ns/op
OverheadActual  18: 8192000 op, 21162.00 ns, 0.0026 ns/op
OverheadActual  19: 8192000 op, 22254.00 ns, 0.0027 ns/op
OverheadActual  20: 8192000 op, 21182.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 953474753.00 ns, 116.3910 ns/op
WorkloadWarmup   2: 8192000 op, 948213761.00 ns, 115.7488 ns/op
WorkloadWarmup   3: 8192000 op, 945131800.00 ns, 115.3725 ns/op
WorkloadWarmup   4: 8192000 op, 939888842.00 ns, 114.7325 ns/op
WorkloadWarmup   5: 8192000 op, 943463083.00 ns, 115.1688 ns/op
WorkloadWarmup   6: 8192000 op, 944762531.00 ns, 115.3275 ns/op
WorkloadWarmup   7: 8192000 op, 939651957.00 ns, 114.7036 ns/op
WorkloadWarmup   8: 8192000 op, 940692460.00 ns, 114.8306 ns/op
WorkloadWarmup   9: 8192000 op, 937985152.00 ns, 114.5001 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 943067718.00 ns, 115.1206 ns/op
WorkloadActual   2: 8192000 op, 944814839.00 ns, 115.3338 ns/op
WorkloadActual   3: 8192000 op, 940493626.00 ns, 114.8064 ns/op
WorkloadActual   4: 8192000 op, 938447071.00 ns, 114.5565 ns/op
WorkloadActual   5: 8192000 op, 939621726.00 ns, 114.6999 ns/op
WorkloadActual   6: 8192000 op, 939586633.00 ns, 114.6956 ns/op
WorkloadActual   7: 8192000 op, 947505381.00 ns, 115.6623 ns/op
WorkloadActual   8: 8192000 op, 940705263.00 ns, 114.8322 ns/op
WorkloadActual   9: 8192000 op, 938010417.00 ns, 114.5032 ns/op
WorkloadActual  10: 8192000 op, 938851591.00 ns, 114.6059 ns/op
WorkloadActual  11: 8192000 op, 939547311.00 ns, 114.6908 ns/op
WorkloadActual  12: 8192000 op, 942791261.00 ns, 115.0868 ns/op
WorkloadActual  13: 8192000 op, 941674253.00 ns, 114.9505 ns/op
WorkloadActual  14: 8192000 op, 944802674.00 ns, 115.3324 ns/op
WorkloadActual  15: 8192000 op, 942898141.00 ns, 115.0999 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 943045935.00 ns, 115.1179 ns/op
WorkloadResult   2: 8192000 op, 944793056.00 ns, 115.3312 ns/op
WorkloadResult   3: 8192000 op, 940471843.00 ns, 114.8037 ns/op
WorkloadResult   4: 8192000 op, 938425288.00 ns, 114.5539 ns/op
WorkloadResult   5: 8192000 op, 939599943.00 ns, 114.6973 ns/op
WorkloadResult   6: 8192000 op, 939564850.00 ns, 114.6930 ns/op
WorkloadResult   7: 8192000 op, 947483598.00 ns, 115.6596 ns/op
WorkloadResult   8: 8192000 op, 940683480.00 ns, 114.8295 ns/op
WorkloadResult   9: 8192000 op, 937988634.00 ns, 114.5006 ns/op
WorkloadResult  10: 8192000 op, 938829808.00 ns, 114.6032 ns/op
WorkloadResult  11: 8192000 op, 939525528.00 ns, 114.6882 ns/op
WorkloadResult  12: 8192000 op, 942769478.00 ns, 115.0842 ns/op
WorkloadResult  13: 8192000 op, 941652470.00 ns, 114.9478 ns/op
WorkloadResult  14: 8192000 op, 944780891.00 ns, 115.3297 ns/op
WorkloadResult  15: 8192000 op, 942876358.00 ns, 115.0972 ns/op
// GC:  129 0 0 2162688000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4393 has exited with code 0.

Mean = 114.929 ns, StdErr = 0.086 ns (0.08%), N = 15, StdDev = 0.335 ns
Min = 114.501 ns, Q1 = 114.691 ns, Median = 114.830 ns, Q3 = 115.108 ns, Max = 115.660 ns
IQR = 0.417 ns, LowerFence = 114.065 ns, UpperFence = 115.733 ns
ConfidenceInterval = [114.571 ns; 115.287 ns] (CI 99.9%), Margin = 0.358 ns (0.31% of Mean)
Skewness = 0.58, Kurtosis = 2.2, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-04 15:37 (0h 2m from now) **
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

OverheadJitting  1: 1000 op, 190627.00 ns, 190.6270 ns/op
WorkloadJitting  1: 1000 op, 1199011.00 ns, 1.1990 us/op

OverheadJitting  2: 16000 op, 218639.00 ns, 13.6649 ns/op
WorkloadJitting  2: 16000 op, 8781506.00 ns, 548.8441 ns/op

WorkloadPilot    1: 16000 op, 7286811.00 ns, 455.4257 ns/op
WorkloadPilot    2: 32000 op, 12966365.00 ns, 405.1989 ns/op
WorkloadPilot    3: 64000 op, 25526710.00 ns, 398.8548 ns/op
WorkloadPilot    4: 128000 op, 52112471.00 ns, 407.1287 ns/op
WorkloadPilot    5: 256000 op, 58840202.00 ns, 229.8445 ns/op
WorkloadPilot    6: 512000 op, 37623168.00 ns, 73.4828 ns/op
WorkloadPilot    7: 1024000 op, 75050992.00 ns, 73.2920 ns/op
WorkloadPilot    8: 2048000 op, 150274724.00 ns, 73.3763 ns/op
WorkloadPilot    9: 4096000 op, 300762297.00 ns, 73.4283 ns/op
WorkloadPilot   10: 8192000 op, 600461971.00 ns, 73.2986 ns/op

OverheadWarmup   1: 8192000 op, 24727.00 ns, 0.0030 ns/op
OverheadWarmup   2: 8192000 op, 21362.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 21262.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 21182.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadWarmup   7: 8192000 op, 21222.00 ns, 0.0026 ns/op

OverheadActual   1: 8192000 op, 21362.00 ns, 0.0026 ns/op
OverheadActual   2: 8192000 op, 23245.00 ns, 0.0028 ns/op
OverheadActual   3: 8192000 op, 21433.00 ns, 0.0026 ns/op
OverheadActual   4: 8192000 op, 21382.00 ns, 0.0026 ns/op
OverheadActual   5: 8192000 op, 21302.00 ns, 0.0026 ns/op
OverheadActual   6: 8192000 op, 21202.00 ns, 0.0026 ns/op
OverheadActual   7: 8192000 op, 21242.00 ns, 0.0026 ns/op
OverheadActual   8: 8192000 op, 21212.00 ns, 0.0026 ns/op
OverheadActual   9: 8192000 op, 21192.00 ns, 0.0026 ns/op
OverheadActual  10: 8192000 op, 22684.00 ns, 0.0028 ns/op
OverheadActual  11: 8192000 op, 21242.00 ns, 0.0026 ns/op
OverheadActual  12: 8192000 op, 21242.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 21252.00 ns, 0.0026 ns/op
OverheadActual  15: 8192000 op, 21202.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 612751503.00 ns, 74.7988 ns/op
WorkloadWarmup   2: 8192000 op, 608705864.00 ns, 74.3049 ns/op
WorkloadWarmup   3: 8192000 op, 598984614.00 ns, 73.1182 ns/op
WorkloadWarmup   4: 8192000 op, 596515412.00 ns, 72.8168 ns/op
WorkloadWarmup   5: 8192000 op, 596198696.00 ns, 72.7782 ns/op
WorkloadWarmup   6: 8192000 op, 599786498.00 ns, 73.2161 ns/op
WorkloadWarmup   7: 8192000 op, 599264664.00 ns, 73.1524 ns/op
WorkloadWarmup   8: 8192000 op, 600267111.00 ns, 73.2748 ns/op
WorkloadWarmup   9: 8192000 op, 599676583.00 ns, 73.2027 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 607347492.00 ns, 74.1391 ns/op
WorkloadActual   2: 8192000 op, 600492840.00 ns, 73.3023 ns/op
WorkloadActual   3: 8192000 op, 599083181.00 ns, 73.1303 ns/op
WorkloadActual   4: 8192000 op, 598277121.00 ns, 73.0319 ns/op
WorkloadActual   5: 8192000 op, 599449395.00 ns, 73.1750 ns/op
WorkloadActual   6: 8192000 op, 602026227.00 ns, 73.4895 ns/op
WorkloadActual   7: 8192000 op, 606089412.00 ns, 73.9855 ns/op
WorkloadActual   8: 8192000 op, 599725179.00 ns, 73.2086 ns/op
WorkloadActual   9: 8192000 op, 599277357.00 ns, 73.1540 ns/op
WorkloadActual  10: 8192000 op, 598643104.00 ns, 73.0766 ns/op
WorkloadActual  11: 8192000 op, 599408224.00 ns, 73.1699 ns/op
WorkloadActual  12: 8192000 op, 599120391.00 ns, 73.1348 ns/op
WorkloadActual  13: 8192000 op, 599260532.00 ns, 73.1519 ns/op
WorkloadActual  14: 8192000 op, 600175657.00 ns, 73.2636 ns/op
WorkloadActual  15: 8192000 op, 600974337.00 ns, 73.3611 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 600471598.00 ns, 73.2998 ns/op
WorkloadResult   2: 8192000 op, 599061939.00 ns, 73.1277 ns/op
WorkloadResult   3: 8192000 op, 598255879.00 ns, 73.0293 ns/op
WorkloadResult   4: 8192000 op, 599428153.00 ns, 73.1724 ns/op
WorkloadResult   5: 8192000 op, 602004985.00 ns, 73.4869 ns/op
WorkloadResult   6: 8192000 op, 599703937.00 ns, 73.2060 ns/op
WorkloadResult   7: 8192000 op, 599256115.00 ns, 73.1514 ns/op
WorkloadResult   8: 8192000 op, 598621862.00 ns, 73.0740 ns/op
WorkloadResult   9: 8192000 op, 599386982.00 ns, 73.1674 ns/op
WorkloadResult  10: 8192000 op, 599099149.00 ns, 73.1322 ns/op
WorkloadResult  11: 8192000 op, 599239290.00 ns, 73.1493 ns/op
WorkloadResult  12: 8192000 op, 600154415.00 ns, 73.2610 ns/op
WorkloadResult  13: 8192000 op, 600953095.00 ns, 73.3585 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4412 has exited with code 0.

Mean = 73.201 ns, StdErr = 0.034 ns (0.05%), N = 13, StdDev = 0.123 ns
Min = 73.029 ns, Q1 = 73.132 ns, Median = 73.167 ns, Q3 = 73.261 ns, Max = 73.487 ns
IQR = 0.129 ns, LowerFence = 72.939 ns, UpperFence = 73.454 ns
ConfidenceInterval = [73.053 ns; 73.349 ns] (CI 99.9%), Margin = 0.148 ns (0.20% of Mean)
Skewness = 0.83, Kurtosis = 2.85, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-04 15:36 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 221323.00 ns, 221.3230 ns/op
WorkloadJitting  1: 1000 op, 1587576.00 ns, 1.5876 us/op

OverheadJitting  2: 16000 op, 200211.00 ns, 12.5132 ns/op
WorkloadJitting  2: 16000 op, 16782166.00 ns, 1.0489 us/op

WorkloadPilot    1: 16000 op, 14533717.00 ns, 908.3573 ns/op
WorkloadPilot    2: 32000 op, 25871714.00 ns, 808.4911 ns/op
WorkloadPilot    3: 64000 op, 51707963.00 ns, 807.9369 ns/op
WorkloadPilot    4: 128000 op, 95679252.00 ns, 747.4942 ns/op
WorkloadPilot    5: 256000 op, 88923591.00 ns, 347.3578 ns/op
WorkloadPilot    6: 512000 op, 86102342.00 ns, 168.1686 ns/op
WorkloadPilot    7: 1024000 op, 170869705.00 ns, 166.8649 ns/op
WorkloadPilot    8: 2048000 op, 342881295.00 ns, 167.4225 ns/op
WorkloadPilot    9: 4096000 op, 687519522.00 ns, 167.8514 ns/op

OverheadWarmup   1: 4096000 op, 14251.00 ns, 0.0035 ns/op
OverheadWarmup   2: 4096000 op, 16555.00 ns, 0.0040 ns/op
OverheadWarmup   3: 4096000 op, 16464.00 ns, 0.0040 ns/op
OverheadWarmup   4: 4096000 op, 16415.00 ns, 0.0040 ns/op
OverheadWarmup   5: 4096000 op, 16625.00 ns, 0.0041 ns/op
OverheadWarmup   6: 4096000 op, 16324.00 ns, 0.0040 ns/op

OverheadActual   1: 4096000 op, 16415.00 ns, 0.0040 ns/op
OverheadActual   2: 4096000 op, 16544.00 ns, 0.0040 ns/op
OverheadActual   3: 4096000 op, 19499.00 ns, 0.0048 ns/op
OverheadActual   4: 4096000 op, 16145.00 ns, 0.0039 ns/op
OverheadActual   5: 4096000 op, 16324.00 ns, 0.0040 ns/op
OverheadActual   6: 4096000 op, 16304.00 ns, 0.0040 ns/op
OverheadActual   7: 4096000 op, 31858.00 ns, 0.0078 ns/op
OverheadActual   8: 4096000 op, 15903.00 ns, 0.0039 ns/op
OverheadActual   9: 4096000 op, 16315.00 ns, 0.0040 ns/op
OverheadActual  10: 4096000 op, 16284.00 ns, 0.0040 ns/op
OverheadActual  11: 4096000 op, 18358.00 ns, 0.0045 ns/op
OverheadActual  12: 4096000 op, 15724.00 ns, 0.0038 ns/op
OverheadActual  13: 4096000 op, 15914.00 ns, 0.0039 ns/op
OverheadActual  14: 4096000 op, 16305.00 ns, 0.0040 ns/op
OverheadActual  15: 4096000 op, 16044.00 ns, 0.0039 ns/op

WorkloadWarmup   1: 4096000 op, 698391975.00 ns, 170.5059 ns/op
WorkloadWarmup   2: 4096000 op, 698446908.00 ns, 170.5193 ns/op
WorkloadWarmup   3: 4096000 op, 685999697.00 ns, 167.4804 ns/op
WorkloadWarmup   4: 4096000 op, 685280696.00 ns, 167.3049 ns/op
WorkloadWarmup   5: 4096000 op, 686417665.00 ns, 167.5824 ns/op
WorkloadWarmup   6: 4096000 op, 685152142.00 ns, 167.2735 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 693725830.00 ns, 169.3667 ns/op
WorkloadActual   2: 4096000 op, 685655129.00 ns, 167.3963 ns/op
WorkloadActual   3: 4096000 op, 684830269.00 ns, 167.1949 ns/op
WorkloadActual   4: 4096000 op, 688062271.00 ns, 167.9840 ns/op
WorkloadActual   5: 4096000 op, 688857055.00 ns, 168.1780 ns/op
WorkloadActual   6: 4096000 op, 684573719.00 ns, 167.1323 ns/op
WorkloadActual   7: 4096000 op, 686917100.00 ns, 167.7044 ns/op
WorkloadActual   8: 4096000 op, 687489159.00 ns, 167.8440 ns/op
WorkloadActual   9: 4096000 op, 686617289.00 ns, 167.6312 ns/op
WorkloadActual  10: 4096000 op, 689966111.00 ns, 168.4488 ns/op
WorkloadActual  11: 4096000 op, 685992591.00 ns, 167.4787 ns/op
WorkloadActual  12: 4096000 op, 684993764.00 ns, 167.2348 ns/op
WorkloadActual  13: 4096000 op, 685704889.00 ns, 167.4084 ns/op
WorkloadActual  14: 4096000 op, 688684185.00 ns, 168.1358 ns/op
WorkloadActual  15: 4096000 op, 688195466.00 ns, 168.0165 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 685638824.00 ns, 167.3923 ns/op
WorkloadResult   2: 4096000 op, 684813964.00 ns, 167.1909 ns/op
WorkloadResult   3: 4096000 op, 688045966.00 ns, 167.9800 ns/op
WorkloadResult   4: 4096000 op, 688840750.00 ns, 168.1740 ns/op
WorkloadResult   5: 4096000 op, 684557414.00 ns, 167.1283 ns/op
WorkloadResult   6: 4096000 op, 686900795.00 ns, 167.7004 ns/op
WorkloadResult   7: 4096000 op, 687472854.00 ns, 167.8401 ns/op
WorkloadResult   8: 4096000 op, 686600984.00 ns, 167.6272 ns/op
WorkloadResult   9: 4096000 op, 689949806.00 ns, 168.4448 ns/op
WorkloadResult  10: 4096000 op, 685976286.00 ns, 167.4747 ns/op
WorkloadResult  11: 4096000 op, 684977459.00 ns, 167.2308 ns/op
WorkloadResult  12: 4096000 op, 685688584.00 ns, 167.4044 ns/op
WorkloadResult  13: 4096000 op, 688667880.00 ns, 168.1318 ns/op
WorkloadResult  14: 4096000 op, 688179161.00 ns, 168.0125 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4426 has exited with code 0.

Mean = 167.695 ns, StdErr = 0.110 ns (0.07%), N = 14, StdDev = 0.412 ns
Min = 167.128 ns, Q1 = 167.395 ns, Median = 167.664 ns, Q3 = 168.004 ns, Max = 168.445 ns
IQR = 0.609 ns, LowerFence = 166.482 ns, UpperFence = 168.918 ns
ConfidenceInterval = [167.231 ns; 168.160 ns] (CI 99.9%), Margin = 0.465 ns (0.28% of Mean)
Skewness = 0.2, Kurtosis = 1.62, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-04 15:36 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 243235.00 ns, 243.2350 ns/op
WorkloadJitting  1: 1000 op, 1103929.00 ns, 1.1039 us/op

OverheadJitting  2: 16000 op, 286120.00 ns, 17.8825 ns/op
WorkloadJitting  2: 16000 op, 6631385.00 ns, 414.4616 ns/op

WorkloadPilot    1: 16000 op, 5124681.00 ns, 320.2926 ns/op
WorkloadPilot    2: 32000 op, 9566695.00 ns, 298.9592 ns/op
WorkloadPilot    3: 64000 op, 19066640.00 ns, 297.9163 ns/op
WorkloadPilot    4: 128000 op, 38039820.00 ns, 297.1861 ns/op
WorkloadPilot    5: 256000 op, 70949852.00 ns, 277.1479 ns/op
WorkloadPilot    6: 512000 op, 40097351.00 ns, 78.3151 ns/op
WorkloadPilot    7: 1024000 op, 75656874.00 ns, 73.8837 ns/op
WorkloadPilot    8: 2048000 op, 148928734.00 ns, 72.7191 ns/op
WorkloadPilot    9: 4096000 op, 298486876.00 ns, 72.8728 ns/op
WorkloadPilot   10: 8192000 op, 595657482.00 ns, 72.7121 ns/op

OverheadWarmup   1: 8192000 op, 27422.00 ns, 0.0033 ns/op
OverheadWarmup   2: 8192000 op, 21332.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 20972.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 21002.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 21052.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 20992.00 ns, 0.0026 ns/op
OverheadWarmup   7: 8192000 op, 20961.00 ns, 0.0026 ns/op
OverheadWarmup   8: 8192000 op, 20961.00 ns, 0.0026 ns/op
OverheadWarmup   9: 8192000 op, 26099.00 ns, 0.0032 ns/op

OverheadActual   1: 8192000 op, 21071.00 ns, 0.0026 ns/op
OverheadActual   2: 8192000 op, 21182.00 ns, 0.0026 ns/op
OverheadActual   3: 8192000 op, 21172.00 ns, 0.0026 ns/op
OverheadActual   4: 8192000 op, 21101.00 ns, 0.0026 ns/op
OverheadActual   5: 8192000 op, 21061.00 ns, 0.0026 ns/op
OverheadActual   6: 8192000 op, 20951.00 ns, 0.0026 ns/op
OverheadActual   7: 8192000 op, 20962.00 ns, 0.0026 ns/op
OverheadActual   8: 8192000 op, 26389.00 ns, 0.0032 ns/op
OverheadActual   9: 8192000 op, 29384.00 ns, 0.0036 ns/op
OverheadActual  10: 8192000 op, 21031.00 ns, 0.0026 ns/op
OverheadActual  11: 8192000 op, 21022.00 ns, 0.0026 ns/op
OverheadActual  12: 8192000 op, 20972.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 21052.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 20972.00 ns, 0.0026 ns/op
OverheadActual  15: 8192000 op, 21042.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 612674414.00 ns, 74.7894 ns/op
WorkloadWarmup   2: 8192000 op, 604795875.00 ns, 73.8276 ns/op
WorkloadWarmup   3: 8192000 op, 599315269.00 ns, 73.1586 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 598685813.00 ns, 73.0818 ns/op
WorkloadActual   2: 8192000 op, 598246814.00 ns, 73.0282 ns/op
WorkloadActual   3: 8192000 op, 596665287.00 ns, 72.8351 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 598664761.00 ns, 73.0792 ns/op
WorkloadResult   2: 8192000 op, 598225762.00 ns, 73.0256 ns/op
WorkloadResult   3: 8192000 op, 596644235.00 ns, 72.8325 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4440 has exited with code 0.

Mean = 72.979 ns, StdErr = 0.075 ns (0.10%), N = 3, StdDev = 0.130 ns
Min = 72.833 ns, Q1 = 72.929 ns, Median = 73.026 ns, Q3 = 73.052 ns, Max = 73.079 ns
IQR = 0.123 ns, LowerFence = 72.744 ns, UpperFence = 73.237 ns
ConfidenceInterval = [70.612 ns; 75.346 ns] (CI 99.9%), Margin = 2.367 ns (3.24% of Mean)
Skewness = -0.31, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-04 15:36 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 222755.00 ns, 222.7550 ns/op
WorkloadJitting  1: 1000 op, 1239222.00 ns, 1.2392 us/op

OverheadJitting  2: 16000 op, 265389.00 ns, 16.5868 ns/op
WorkloadJitting  2: 16000 op, 11633921.00 ns, 727.1201 ns/op

WorkloadPilot    1: 16000 op, 9694357.00 ns, 605.8973 ns/op
WorkloadPilot    2: 32000 op, 17719166.00 ns, 553.7239 ns/op
WorkloadPilot    3: 64000 op, 30592799.00 ns, 478.0125 ns/op
WorkloadPilot    4: 128000 op, 53697498.00 ns, 419.5117 ns/op
WorkloadPilot    5: 256000 op, 69709958.00 ns, 272.3045 ns/op
WorkloadPilot    6: 512000 op, 57696456.00 ns, 112.6884 ns/op
WorkloadPilot    7: 1024000 op, 113446157.00 ns, 110.7873 ns/op
WorkloadPilot    8: 2048000 op, 226686526.00 ns, 110.6868 ns/op
WorkloadPilot    9: 4096000 op, 481447467.00 ns, 117.5409 ns/op
WorkloadPilot   10: 8192000 op, 910713557.00 ns, 111.1711 ns/op

OverheadWarmup   1: 8192000 op, 26580.00 ns, 0.0032 ns/op
OverheadWarmup   2: 8192000 op, 21292.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 21101.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 20892.00 ns, 0.0026 ns/op
OverheadWarmup   5: 8192000 op, 21001.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 20921.00 ns, 0.0026 ns/op
OverheadWarmup   7: 8192000 op, 20922.00 ns, 0.0026 ns/op
OverheadWarmup   8: 8192000 op, 29274.00 ns, 0.0036 ns/op
OverheadWarmup   9: 8192000 op, 35684.00 ns, 0.0044 ns/op
OverheadWarmup  10: 8192000 op, 29314.00 ns, 0.0036 ns/op

OverheadActual   1: 8192000 op, 21022.00 ns, 0.0026 ns/op
OverheadActual   2: 8192000 op, 21042.00 ns, 0.0026 ns/op
OverheadActual   3: 8192000 op, 21142.00 ns, 0.0026 ns/op
OverheadActual   4: 8192000 op, 20982.00 ns, 0.0026 ns/op
OverheadActual   5: 8192000 op, 21042.00 ns, 0.0026 ns/op
OverheadActual   6: 8192000 op, 20992.00 ns, 0.0026 ns/op
OverheadActual   7: 8192000 op, 24957.00 ns, 0.0030 ns/op
OverheadActual   8: 8192000 op, 21002.00 ns, 0.0026 ns/op
OverheadActual   9: 8192000 op, 20971.00 ns, 0.0026 ns/op
OverheadActual  10: 8192000 op, 23796.00 ns, 0.0029 ns/op
OverheadActual  11: 8192000 op, 20982.00 ns, 0.0026 ns/op
OverheadActual  12: 8192000 op, 20972.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 21032.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 21001.00 ns, 0.0026 ns/op
OverheadActual  15: 8192000 op, 24757.00 ns, 0.0030 ns/op

WorkloadWarmup   1: 8192000 op, 913918637.00 ns, 111.5623 ns/op
WorkloadWarmup   2: 8192000 op, 914573380.00 ns, 111.6423 ns/op
WorkloadWarmup   3: 8192000 op, 909636492.00 ns, 111.0396 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 907509676.00 ns, 110.7800 ns/op
WorkloadActual   2: 8192000 op, 911988917.00 ns, 111.3268 ns/op
WorkloadActual   3: 8192000 op, 909724795.00 ns, 111.0504 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 907488654.00 ns, 110.7774 ns/op
WorkloadResult   2: 8192000 op, 911967895.00 ns, 111.3242 ns/op
WorkloadResult   3: 8192000 op, 909703773.00 ns, 111.0478 ns/op
// GC:  129 0 0 2162688000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4447 has exited with code 0.

Mean = 111.050 ns, StdErr = 0.158 ns (0.14%), N = 3, StdDev = 0.273 ns
Min = 110.777 ns, Q1 = 110.913 ns, Median = 111.048 ns, Q3 = 111.186 ns, Max = 111.324 ns
IQR = 0.273 ns, LowerFence = 110.503 ns, UpperFence = 111.596 ns
ConfidenceInterval = [106.062 ns; 116.038 ns] (CI 99.9%), Margin = 4.988 ns (4.49% of Mean)
Skewness = 0.01, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-04 15:36 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 220261.00 ns, 220.2610 ns/op
WorkloadJitting  1: 1000 op, 1203508.00 ns, 1.2035 us/op

OverheadJitting  2: 16000 op, 276446.00 ns, 17.2779 ns/op
WorkloadJitting  2: 16000 op, 8957400.00 ns, 559.8375 ns/op

WorkloadPilot    1: 16000 op, 7505608.00 ns, 469.1005 ns/op
WorkloadPilot    2: 32000 op, 13568378.00 ns, 424.0118 ns/op
WorkloadPilot    3: 64000 op, 26896136.00 ns, 420.2521 ns/op
WorkloadPilot    4: 128000 op, 52466648.00 ns, 409.8957 ns/op
WorkloadPilot    5: 256000 op, 56095040.00 ns, 219.1213 ns/op
WorkloadPilot    6: 512000 op, 37897416.00 ns, 74.0184 ns/op
WorkloadPilot    7: 1024000 op, 74861459.00 ns, 73.1069 ns/op
WorkloadPilot    8: 2048000 op, 149625312.00 ns, 73.0592 ns/op
WorkloadPilot    9: 4096000 op, 299436213.00 ns, 73.1045 ns/op
WorkloadPilot   10: 8192000 op, 597890936.00 ns, 72.9847 ns/op

OverheadWarmup   1: 8192000 op, 26250.00 ns, 0.0032 ns/op
OverheadWarmup   2: 8192000 op, 21282.00 ns, 0.0026 ns/op
OverheadWarmup   3: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 35113.00 ns, 0.0043 ns/op
OverheadWarmup   5: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadWarmup   6: 8192000 op, 21212.00 ns, 0.0026 ns/op
OverheadWarmup   7: 8192000 op, 21222.00 ns, 0.0026 ns/op
OverheadWarmup   8: 8192000 op, 21282.00 ns, 0.0026 ns/op
OverheadWarmup   9: 8192000 op, 24637.00 ns, 0.0030 ns/op
OverheadWarmup  10: 8192000 op, 34412.00 ns, 0.0042 ns/op

OverheadActual   1: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadActual   2: 8192000 op, 21312.00 ns, 0.0026 ns/op
OverheadActual   3: 8192000 op, 21352.00 ns, 0.0026 ns/op
OverheadActual   4: 8192000 op, 21312.00 ns, 0.0026 ns/op
OverheadActual   5: 8192000 op, 21342.00 ns, 0.0026 ns/op
OverheadActual   6: 8192000 op, 21222.00 ns, 0.0026 ns/op
OverheadActual   7: 8192000 op, 24657.00 ns, 0.0030 ns/op
OverheadActual   8: 8192000 op, 24166.00 ns, 0.0029 ns/op
OverheadActual   9: 8192000 op, 24136.00 ns, 0.0029 ns/op
OverheadActual  10: 8192000 op, 21272.00 ns, 0.0026 ns/op
OverheadActual  11: 8192000 op, 21261.00 ns, 0.0026 ns/op
OverheadActual  12: 8192000 op, 21322.00 ns, 0.0026 ns/op
OverheadActual  13: 8192000 op, 21251.00 ns, 0.0026 ns/op
OverheadActual  14: 8192000 op, 21282.00 ns, 0.0026 ns/op
OverheadActual  15: 8192000 op, 24677.00 ns, 0.0030 ns/op
OverheadActual  16: 8192000 op, 24107.00 ns, 0.0029 ns/op
OverheadActual  17: 8192000 op, 41582.00 ns, 0.0051 ns/op
OverheadActual  18: 8192000 op, 21232.00 ns, 0.0026 ns/op
OverheadActual  19: 8192000 op, 21212.00 ns, 0.0026 ns/op
OverheadActual  20: 8192000 op, 21252.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 609926648.00 ns, 74.4539 ns/op
WorkloadWarmup   2: 8192000 op, 606162566.00 ns, 73.9945 ns/op
WorkloadWarmup   3: 8192000 op, 601406870.00 ns, 73.4139 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 604586528.00 ns, 73.8021 ns/op
WorkloadActual   2: 8192000 op, 598571048.00 ns, 73.0678 ns/op
WorkloadActual   3: 8192000 op, 597403995.00 ns, 72.9253 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 604565216.00 ns, 73.7995 ns/op
WorkloadResult   2: 8192000 op, 598549736.00 ns, 73.0652 ns/op
WorkloadResult   3: 8192000 op, 597382683.00 ns, 72.9227 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4458 has exited with code 0.

Mean = 73.262 ns, StdErr = 0.272 ns (0.37%), N = 3, StdDev = 0.471 ns
Min = 72.923 ns, Q1 = 72.994 ns, Median = 73.065 ns, Q3 = 73.432 ns, Max = 73.799 ns
IQR = 0.438 ns, LowerFence = 72.336 ns, UpperFence = 74.090 ns
ConfidenceInterval = [64.679 ns; 81.846 ns] (CI 99.9%), Margin = 8.584 ns (11.72% of Mean)
Skewness = 0.35, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-04 15:36 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 281974.00 ns, 281.9740 ns/op
WorkloadJitting  1: 1000 op, 1546865.00 ns, 1.5469 us/op

OverheadJitting  2: 16000 op, 241733.00 ns, 15.1083 ns/op
WorkloadJitting  2: 16000 op, 16307556.00 ns, 1.0192 us/op

WorkloadPilot    1: 16000 op, 19417448.00 ns, 1.2136 us/op
WorkloadPilot    2: 32000 op, 26205880.00 ns, 818.9338 ns/op
WorkloadPilot    3: 64000 op, 51390824.00 ns, 802.9816 ns/op
WorkloadPilot    4: 128000 op, 92825818.00 ns, 725.2017 ns/op
WorkloadPilot    5: 256000 op, 89668975.00 ns, 350.2694 ns/op
WorkloadPilot    6: 512000 op, 79355667.00 ns, 154.9915 ns/op
WorkloadPilot    7: 1024000 op, 158739647.00 ns, 155.0192 ns/op
WorkloadPilot    8: 2048000 op, 317615839.00 ns, 155.0859 ns/op
WorkloadPilot    9: 4096000 op, 634838968.00 ns, 154.9900 ns/op

OverheadWarmup   1: 4096000 op, 14522.00 ns, 0.0035 ns/op
OverheadWarmup   2: 4096000 op, 10746.00 ns, 0.0026 ns/op
OverheadWarmup   3: 4096000 op, 10696.00 ns, 0.0026 ns/op
OverheadWarmup   4: 4096000 op, 10666.00 ns, 0.0026 ns/op
OverheadWarmup   5: 4096000 op, 10736.00 ns, 0.0026 ns/op
OverheadWarmup   6: 4096000 op, 10656.00 ns, 0.0026 ns/op
OverheadWarmup   7: 4096000 op, 10676.00 ns, 0.0026 ns/op
OverheadWarmup   8: 4096000 op, 10666.00 ns, 0.0026 ns/op

OverheadActual   1: 4096000 op, 10926.00 ns, 0.0027 ns/op
OverheadActual   2: 4096000 op, 10966.00 ns, 0.0027 ns/op
OverheadActual   3: 4096000 op, 10817.00 ns, 0.0026 ns/op
OverheadActual   4: 4096000 op, 10766.00 ns, 0.0026 ns/op
OverheadActual   5: 4096000 op, 10767.00 ns, 0.0026 ns/op
OverheadActual   6: 4096000 op, 10706.00 ns, 0.0026 ns/op
OverheadActual   7: 4096000 op, 10677.00 ns, 0.0026 ns/op
OverheadActual   8: 4096000 op, 10697.00 ns, 0.0026 ns/op
OverheadActual   9: 4096000 op, 13481.00 ns, 0.0033 ns/op
OverheadActual  10: 4096000 op, 12158.00 ns, 0.0030 ns/op
OverheadActual  11: 4096000 op, 10696.00 ns, 0.0026 ns/op
OverheadActual  12: 4096000 op, 10706.00 ns, 0.0026 ns/op
OverheadActual  13: 4096000 op, 10696.00 ns, 0.0026 ns/op
OverheadActual  14: 4096000 op, 10726.00 ns, 0.0026 ns/op
OverheadActual  15: 4096000 op, 10716.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 4096000 op, 673455287.00 ns, 164.4178 ns/op
WorkloadWarmup   2: 4096000 op, 645227679.00 ns, 157.5263 ns/op
WorkloadWarmup   3: 4096000 op, 643097903.00 ns, 157.0063 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 641712208.00 ns, 156.6680 ns/op
WorkloadActual   2: 4096000 op, 637140537.00 ns, 155.5519 ns/op
WorkloadActual   3: 4096000 op, 636741832.00 ns, 155.4545 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 641701482.00 ns, 156.6654 ns/op
WorkloadResult   2: 4096000 op, 637129811.00 ns, 155.5493 ns/op
WorkloadResult   3: 4096000 op, 636731106.00 ns, 155.4519 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4467 has exited with code 0.

Mean = 155.889 ns, StdErr = 0.389 ns (0.25%), N = 3, StdDev = 0.674 ns
Min = 155.452 ns, Q1 = 155.501 ns, Median = 155.549 ns, Q3 = 156.107 ns, Max = 156.665 ns
IQR = 0.607 ns, LowerFence = 154.590 ns, UpperFence = 157.017 ns
ConfidenceInterval = [143.588 ns; 168.190 ns] (CI 99.9%), Margin = 12.301 ns (7.89% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-04 15:36 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 65.031 ns, StdErr = 0.051 ns (0.08%), N = 13, StdDev = 0.184 ns
Min = 64.837 ns, Q1 = 64.930 ns, Median = 64.967 ns, Q3 = 65.048 ns, Max = 65.507 ns
IQR = 0.118 ns, LowerFence = 64.753 ns, UpperFence = 65.225 ns
ConfidenceInterval = [64.811 ns; 65.251 ns] (CI 99.9%), Margin = 0.220 ns (0.34% of Mean)
Skewness = 1.47, Kurtosis = 4.05, MValue = 2
-------------------- Histogram --------------------
[64.734 ns ; 65.610 ns) | @@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 114.929 ns, StdErr = 0.086 ns (0.08%), N = 15, StdDev = 0.335 ns
Min = 114.501 ns, Q1 = 114.691 ns, Median = 114.830 ns, Q3 = 115.108 ns, Max = 115.660 ns
IQR = 0.417 ns, LowerFence = 114.065 ns, UpperFence = 115.733 ns
ConfidenceInterval = [114.571 ns; 115.287 ns] (CI 99.9%), Margin = 0.358 ns (0.31% of Mean)
Skewness = 0.58, Kurtosis = 2.2, MValue = 2
-------------------- Histogram --------------------
[114.322 ns ; 115.838 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 73.201 ns, StdErr = 0.034 ns (0.05%), N = 13, StdDev = 0.123 ns
Min = 73.029 ns, Q1 = 73.132 ns, Median = 73.167 ns, Q3 = 73.261 ns, Max = 73.487 ns
IQR = 0.129 ns, LowerFence = 72.939 ns, UpperFence = 73.454 ns
ConfidenceInterval = [73.053 ns; 73.349 ns] (CI 99.9%), Margin = 0.148 ns (0.20% of Mean)
Skewness = 0.83, Kurtosis = 2.85, MValue = 2
-------------------- Histogram --------------------
[72.960 ns ; 73.556 ns) | @@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 167.695 ns, StdErr = 0.110 ns (0.07%), N = 14, StdDev = 0.412 ns
Min = 167.128 ns, Q1 = 167.395 ns, Median = 167.664 ns, Q3 = 168.004 ns, Max = 168.445 ns
IQR = 0.609 ns, LowerFence = 166.482 ns, UpperFence = 168.918 ns
ConfidenceInterval = [167.231 ns; 168.160 ns] (CI 99.9%), Margin = 0.465 ns (0.28% of Mean)
Skewness = 0.2, Kurtosis = 1.62, MValue = 2
-------------------- Histogram --------------------
[166.904 ns ; 168.669 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 72.979 ns, StdErr = 0.075 ns (0.10%), N = 3, StdDev = 0.130 ns
Min = 72.833 ns, Q1 = 72.929 ns, Median = 73.026 ns, Q3 = 73.052 ns, Max = 73.079 ns
IQR = 0.123 ns, LowerFence = 72.744 ns, UpperFence = 73.237 ns
ConfidenceInterval = [70.612 ns; 75.346 ns] (CI 99.9%), Margin = 2.367 ns (3.24% of Mean)
Skewness = -0.31, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[72.714 ns ; 73.197 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 111.050 ns, StdErr = 0.158 ns (0.14%), N = 3, StdDev = 0.273 ns
Min = 110.777 ns, Q1 = 110.913 ns, Median = 111.048 ns, Q3 = 111.186 ns, Max = 111.324 ns
IQR = 0.273 ns, LowerFence = 110.503 ns, UpperFence = 111.596 ns
ConfidenceInterval = [106.062 ns; 116.038 ns] (CI 99.9%), Margin = 4.988 ns (4.49% of Mean)
Skewness = 0.01, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[110.529 ns ; 111.573 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 73.262 ns, StdErr = 0.272 ns (0.37%), N = 3, StdDev = 0.471 ns
Min = 72.923 ns, Q1 = 72.994 ns, Median = 73.065 ns, Q3 = 73.432 ns, Max = 73.799 ns
IQR = 0.438 ns, LowerFence = 72.336 ns, UpperFence = 74.090 ns
ConfidenceInterval = [64.679 ns; 81.846 ns] (CI 99.9%), Margin = 8.584 ns (11.72% of Mean)
Skewness = 0.35, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[72.494 ns ; 74.228 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 155.889 ns, StdErr = 0.389 ns (0.25%), N = 3, StdDev = 0.674 ns
Min = 155.452 ns, Q1 = 155.501 ns, Median = 155.549 ns, Q3 = 156.107 ns, Max = 156.665 ns
IQR = 0.607 ns, LowerFence = 154.590 ns, UpperFence = 157.017 ns
ConfidenceInterval = [143.588 ns; 168.190 ns] (CI 99.9%), Margin = 12.301 ns (7.89% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[155.445 ns ; 156.672 ns) | @@@
---------------------------------------------------

// * Summary *

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.87GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error     | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|----------:|---------:|-------:|----------:|
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  65.03 ns |  0.220 ns | 0.184 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 114.93 ns |  0.358 ns | 0.335 ns | 0.0157 |     264 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  73.20 ns |  0.148 ns | 0.123 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 167.70 ns |  0.465 ns | 0.412 ns | 0.0115 |     192 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  72.98 ns |  2.367 ns | 0.130 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 111.05 ns |  4.988 ns | 0.273 ns | 0.0157 |     264 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  73.26 ns |  8.584 ns | 0.471 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 155.89 ns | 12.301 ns | 0.674 ns | 0.0115 |     192 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput                        -> 2 outliers were removed (66.77 ns, 67.57 ns)
  CoreDispatchBenchmarks.'Request  Request': RunStrategy=Throughput                     -> 2 outliers were removed (73.99 ns, 74.14 ns)
  CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': RunStrategy=Throughput -> 1 outlier  was  removed (169.37 ns)
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
Run time: 00:01:40 (100.92 sec), executed benchmarks: 8

Global total time: 00:01:55 (115.74 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
