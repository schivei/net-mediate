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

## Latest CI Benchmark Run

Run: 2026-05-04 14:49 UTC | Branch: copilot/implement-medium-term | Commit: 13b506d

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error     | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|----------:|---------:|-------:|----------:|
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  66.86 ns |  0.132 ns | 0.117 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 130.64 ns |  0.621 ns | 0.580 ns | 0.0156 |     264 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  84.46 ns |  0.181 ns | 0.160 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 163.30 ns |  0.694 ns | 0.615 ns | 0.0115 |     192 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  68.39 ns |  3.358 ns | 0.184 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 123.12 ns |  6.300 ns | 0.345 ns | 0.0156 |     264 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  81.53 ns |  6.187 ns | 0.339 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 163.16 ns | 10.295 ns | 0.564 ns | 0.0115 |     192 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.91 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 12.43 sec and exited with 0
// ***** Done, took 00:00:14 (14.41 sec)   *****
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

OverheadJitting  1: 1000 op, 181798.00 ns, 181.7980 ns/op
WorkloadJitting  1: 1000 op, 1123279.00 ns, 1.1233 us/op

OverheadJitting  2: 16000 op, 212665.00 ns, 13.2916 ns/op
WorkloadJitting  2: 16000 op, 7495581.00 ns, 468.4738 ns/op

WorkloadPilot    1: 16000 op, 6475153.00 ns, 404.6971 ns/op
WorkloadPilot    2: 32000 op, 12853636.00 ns, 401.6761 ns/op
WorkloadPilot    3: 64000 op, 24391184.00 ns, 381.1123 ns/op
WorkloadPilot    4: 128000 op, 51322820.00 ns, 400.9595 ns/op
WorkloadPilot    5: 256000 op, 54965227.00 ns, 214.7079 ns/op
WorkloadPilot    6: 512000 op, 34484296.00 ns, 67.3521 ns/op
WorkloadPilot    7: 1024000 op, 70879796.00 ns, 69.2186 ns/op
WorkloadPilot    8: 2048000 op, 136859800.00 ns, 66.8261 ns/op
WorkloadPilot    9: 4096000 op, 273123902.00 ns, 66.6806 ns/op
WorkloadPilot   10: 8192000 op, 547838573.00 ns, 66.8748 ns/op

OverheadWarmup   1: 8192000 op, 21249.00 ns, 0.0026 ns/op
OverheadWarmup   2: 8192000 op, 18895.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18996.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 36538.00 ns, 0.0045 ns/op
OverheadWarmup   6: 8192000 op, 35186.00 ns, 0.0043 ns/op
OverheadWarmup   7: 8192000 op, 35195.00 ns, 0.0043 ns/op
OverheadWarmup   8: 8192000 op, 34505.00 ns, 0.0042 ns/op

OverheadActual   1: 8192000 op, 35325.00 ns, 0.0043 ns/op
OverheadActual   2: 8192000 op, 35275.00 ns, 0.0043 ns/op
OverheadActual   3: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 36268.00 ns, 0.0044 ns/op
OverheadActual   7: 8192000 op, 34715.00 ns, 0.0042 ns/op
OverheadActual   8: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 19987.00 ns, 0.0024 ns/op
OverheadActual  10: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 18885.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 35335.00 ns, 0.0043 ns/op
OverheadActual  13: 8192000 op, 34854.00 ns, 0.0043 ns/op
OverheadActual  14: 8192000 op, 32350.00 ns, 0.0039 ns/op
OverheadActual  15: 8192000 op, 34845.00 ns, 0.0043 ns/op
OverheadActual  16: 8192000 op, 36187.00 ns, 0.0044 ns/op
OverheadActual  17: 8192000 op, 35867.00 ns, 0.0044 ns/op
OverheadActual  18: 8192000 op, 32500.00 ns, 0.0040 ns/op
OverheadActual  19: 8192000 op, 34745.00 ns, 0.0042 ns/op
OverheadActual  20: 8192000 op, 35196.00 ns, 0.0043 ns/op

WorkloadWarmup   1: 8192000 op, 562141269.00 ns, 68.6208 ns/op
WorkloadWarmup   2: 8192000 op, 554003744.00 ns, 67.6274 ns/op
WorkloadWarmup   3: 8192000 op, 548237994.00 ns, 66.9236 ns/op
WorkloadWarmup   4: 8192000 op, 547096771.00 ns, 66.7843 ns/op
WorkloadWarmup   5: 8192000 op, 547758752.00 ns, 66.8651 ns/op
WorkloadWarmup   6: 8192000 op, 548525659.00 ns, 66.9587 ns/op
WorkloadWarmup   7: 8192000 op, 547365600.00 ns, 66.8171 ns/op
WorkloadWarmup   8: 8192000 op, 546943446.00 ns, 66.7656 ns/op
WorkloadWarmup   9: 8192000 op, 547019487.00 ns, 66.7748 ns/op
WorkloadWarmup  10: 8192000 op, 547627969.00 ns, 66.8491 ns/op
WorkloadWarmup  11: 8192000 op, 547773389.00 ns, 66.8669 ns/op
WorkloadWarmup  12: 8192000 op, 546381160.00 ns, 66.6969 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 549845375.00 ns, 67.1198 ns/op
WorkloadActual   2: 8192000 op, 548867642.00 ns, 67.0004 ns/op
WorkloadActual   3: 8192000 op, 547441285.00 ns, 66.8263 ns/op
WorkloadActual   4: 8192000 op, 548063214.00 ns, 66.9022 ns/op
WorkloadActual   5: 8192000 op, 547387414.00 ns, 66.8198 ns/op
WorkloadActual   6: 8192000 op, 548049288.00 ns, 66.9005 ns/op
WorkloadActual   7: 8192000 op, 547636472.00 ns, 66.8502 ns/op
WorkloadActual   8: 8192000 op, 546636271.00 ns, 66.7281 ns/op
WorkloadActual   9: 8192000 op, 546499360.00 ns, 66.7113 ns/op
WorkloadActual  10: 8192000 op, 546857637.00 ns, 66.7551 ns/op
WorkloadActual  11: 8192000 op, 546686839.00 ns, 66.7342 ns/op
WorkloadActual  12: 8192000 op, 556574305.00 ns, 67.9412 ns/op
WorkloadActual  13: 8192000 op, 548790132.00 ns, 66.9910 ns/op
WorkloadActual  14: 8192000 op, 547899966.00 ns, 66.8823 ns/op
WorkloadActual  15: 8192000 op, 547553397.00 ns, 66.8400 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 549810645.00 ns, 67.1156 ns/op
WorkloadResult   2: 8192000 op, 548832912.00 ns, 66.9962 ns/op
WorkloadResult   3: 8192000 op, 547406555.00 ns, 66.8221 ns/op
WorkloadResult   4: 8192000 op, 548028484.00 ns, 66.8980 ns/op
WorkloadResult   5: 8192000 op, 547352684.00 ns, 66.8155 ns/op
WorkloadResult   6: 8192000 op, 548014558.00 ns, 66.8963 ns/op
WorkloadResult   7: 8192000 op, 547601742.00 ns, 66.8459 ns/op
WorkloadResult   8: 8192000 op, 546601541.00 ns, 66.7238 ns/op
WorkloadResult   9: 8192000 op, 546464630.00 ns, 66.7071 ns/op
WorkloadResult  10: 8192000 op, 546822907.00 ns, 66.7508 ns/op
WorkloadResult  11: 8192000 op, 546652109.00 ns, 66.7300 ns/op
WorkloadResult  12: 8192000 op, 548755402.00 ns, 66.9867 ns/op
WorkloadResult  13: 8192000 op, 547865236.00 ns, 66.8781 ns/op
WorkloadResult  14: 8192000 op, 547518667.00 ns, 66.8358 ns/op
// GC:  11 0 0 196608032 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4364 has exited with code 0.

Mean = 66.857 ns, StdErr = 0.031 ns (0.05%), N = 14, StdDev = 0.117 ns
Min = 66.707 ns, Q1 = 66.767 ns, Median = 66.841 ns, Q3 = 66.898 ns, Max = 67.116 ns
IQR = 0.131 ns, LowerFence = 66.571 ns, UpperFence = 67.093 ns
ConfidenceInterval = [66.725 ns; 66.989 ns] (CI 99.9%), Margin = 0.132 ns (0.20% of Mean)
Skewness = 0.58, Kurtosis = 2.42, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-04 14:50 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 175988.00 ns, 175.9880 ns/op
WorkloadJitting  1: 1000 op, 1524632.00 ns, 1.5246 us/op

OverheadJitting  2: 16000 op, 184634.00 ns, 11.5396 ns/op
WorkloadJitting  2: 16000 op, 14054137.00 ns, 878.3836 ns/op

WorkloadPilot    1: 16000 op, 12489611.00 ns, 780.6007 ns/op
WorkloadPilot    2: 32000 op, 23584421.00 ns, 737.0132 ns/op
WorkloadPilot    3: 64000 op, 43582777.00 ns, 680.9809 ns/op
WorkloadPilot    4: 128000 op, 83860685.00 ns, 655.1616 ns/op
WorkloadPilot    5: 256000 op, 84382879.00 ns, 329.6206 ns/op
WorkloadPilot    6: 512000 op, 66252804.00 ns, 129.4000 ns/op
WorkloadPilot    7: 1024000 op, 130467761.00 ns, 127.4099 ns/op
WorkloadPilot    8: 2048000 op, 260837988.00 ns, 127.3623 ns/op
WorkloadPilot    9: 4096000 op, 534965077.00 ns, 130.6067 ns/op

OverheadWarmup   1: 4096000 op, 11432.00 ns, 0.0028 ns/op
OverheadWarmup   2: 4096000 op, 18344.00 ns, 0.0045 ns/op
OverheadWarmup   3: 4096000 op, 18254.00 ns, 0.0045 ns/op
OverheadWarmup   4: 4096000 op, 18144.00 ns, 0.0044 ns/op
OverheadWarmup   5: 4096000 op, 18054.00 ns, 0.0044 ns/op
OverheadWarmup   6: 4096000 op, 9608.00 ns, 0.0023 ns/op
OverheadWarmup   7: 4096000 op, 9588.00 ns, 0.0023 ns/op
OverheadWarmup   8: 4096000 op, 9578.00 ns, 0.0023 ns/op
OverheadWarmup   9: 4096000 op, 9588.00 ns, 0.0023 ns/op
OverheadWarmup  10: 4096000 op, 9597.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9678.00 ns, 0.0024 ns/op
OverheadActual   2: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9588.00 ns, 0.0023 ns/op
OverheadActual   4: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual   6: 4096000 op, 9629.00 ns, 0.0024 ns/op
OverheadActual   7: 4096000 op, 10490.00 ns, 0.0026 ns/op
OverheadActual   8: 4096000 op, 9748.00 ns, 0.0024 ns/op
OverheadActual   9: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual  10: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual  11: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual  12: 4096000 op, 31017.00 ns, 0.0076 ns/op
OverheadActual  13: 4096000 op, 18074.00 ns, 0.0044 ns/op
OverheadActual  14: 4096000 op, 18284.00 ns, 0.0045 ns/op
OverheadActual  15: 4096000 op, 18024.00 ns, 0.0044 ns/op
OverheadActual  16: 4096000 op, 18104.00 ns, 0.0044 ns/op
OverheadActual  17: 4096000 op, 18054.00 ns, 0.0044 ns/op
OverheadActual  18: 4096000 op, 17953.00 ns, 0.0044 ns/op
OverheadActual  19: 4096000 op, 18093.00 ns, 0.0044 ns/op
OverheadActual  20: 4096000 op, 17773.00 ns, 0.0043 ns/op

WorkloadWarmup   1: 4096000 op, 537760368.00 ns, 131.2892 ns/op
WorkloadWarmup   2: 4096000 op, 540610029.00 ns, 131.9849 ns/op
WorkloadWarmup   3: 4096000 op, 530589631.00 ns, 129.5385 ns/op
WorkloadWarmup   4: 4096000 op, 529335229.00 ns, 129.2322 ns/op
WorkloadWarmup   5: 4096000 op, 528438441.00 ns, 129.0133 ns/op
WorkloadWarmup   6: 4096000 op, 533505672.00 ns, 130.2504 ns/op
WorkloadWarmup   7: 4096000 op, 533935031.00 ns, 130.3552 ns/op
WorkloadWarmup   8: 4096000 op, 530416042.00 ns, 129.4961 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 531956909.00 ns, 129.8723 ns/op
WorkloadActual   2: 4096000 op, 536034497.00 ns, 130.8678 ns/op
WorkloadActual   3: 4096000 op, 537172012.00 ns, 131.1455 ns/op
WorkloadActual   4: 4096000 op, 533057140.00 ns, 130.1409 ns/op
WorkloadActual   5: 4096000 op, 540461841.00 ns, 131.9487 ns/op
WorkloadActual   6: 4096000 op, 533632040.00 ns, 130.2813 ns/op
WorkloadActual   7: 4096000 op, 538316869.00 ns, 131.4250 ns/op
WorkloadActual   8: 4096000 op, 535309993.00 ns, 130.6909 ns/op
WorkloadActual   9: 4096000 op, 532795603.00 ns, 130.0771 ns/op
WorkloadActual  10: 4096000 op, 533841156.00 ns, 130.3323 ns/op
WorkloadActual  11: 4096000 op, 536714156.00 ns, 131.0337 ns/op
WorkloadActual  12: 4096000 op, 533378799.00 ns, 130.2194 ns/op
WorkloadActual  13: 4096000 op, 534861649.00 ns, 130.5815 ns/op
WorkloadActual  14: 4096000 op, 536143304.00 ns, 130.8944 ns/op
WorkloadActual  15: 4096000 op, 532859622.00 ns, 130.0927 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 531946790.00 ns, 129.8698 ns/op
WorkloadResult   2: 4096000 op, 536024378.00 ns, 130.8653 ns/op
WorkloadResult   3: 4096000 op, 537161893.00 ns, 131.1430 ns/op
WorkloadResult   4: 4096000 op, 533047021.00 ns, 130.1384 ns/op
WorkloadResult   5: 4096000 op, 540451722.00 ns, 131.9462 ns/op
WorkloadResult   6: 4096000 op, 533621921.00 ns, 130.2788 ns/op
WorkloadResult   7: 4096000 op, 538306750.00 ns, 131.4225 ns/op
WorkloadResult   8: 4096000 op, 535299874.00 ns, 130.6884 ns/op
WorkloadResult   9: 4096000 op, 532785484.00 ns, 130.0746 ns/op
WorkloadResult  10: 4096000 op, 533831037.00 ns, 130.3298 ns/op
WorkloadResult  11: 4096000 op, 536704037.00 ns, 131.0313 ns/op
WorkloadResult  12: 4096000 op, 533368680.00 ns, 130.2170 ns/op
WorkloadResult  13: 4096000 op, 534851530.00 ns, 130.5790 ns/op
WorkloadResult  14: 4096000 op, 536133185.00 ns, 130.8919 ns/op
WorkloadResult  15: 4096000 op, 532849503.00 ns, 130.0902 ns/op
// GC:  64 0 0 1081344000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4377 has exited with code 0.

Mean = 130.638 ns, StdErr = 0.150 ns (0.11%), N = 15, StdDev = 0.580 ns
Min = 129.870 ns, Q1 = 130.178 ns, Median = 130.579 ns, Q3 = 130.962 ns, Max = 131.946 ns
IQR = 0.784 ns, LowerFence = 129.002 ns, UpperFence = 132.137 ns
ConfidenceInterval = [130.017 ns; 131.258 ns] (CI 99.9%), Margin = 0.621 ns (0.48% of Mean)
Skewness = 0.64, Kurtosis = 2.39, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-04 14:50 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 178633.00 ns, 178.6330 ns/op
WorkloadJitting  1: 1000 op, 1260756.00 ns, 1.2608 us/op

OverheadJitting  2: 16000 op, 186677.00 ns, 11.6673 ns/op
WorkloadJitting  2: 16000 op, 9896579.00 ns, 618.5362 ns/op

WorkloadPilot    1: 16000 op, 8424509.00 ns, 526.5318 ns/op
WorkloadPilot    2: 32000 op, 15881948.00 ns, 496.3109 ns/op
WorkloadPilot    3: 64000 op, 32015408.00 ns, 500.2408 ns/op
WorkloadPilot    4: 128000 op, 63748394.00 ns, 498.0343 ns/op
WorkloadPilot    5: 256000 op, 43937734.00 ns, 171.6318 ns/op
WorkloadPilot    6: 512000 op, 43346254.00 ns, 84.6607 ns/op
WorkloadPilot    7: 1024000 op, 86462798.00 ns, 84.4363 ns/op
WorkloadPilot    8: 2048000 op, 172898100.00 ns, 84.4229 ns/op
WorkloadPilot    9: 4096000 op, 346100676.00 ns, 84.4972 ns/op
WorkloadPilot   10: 8192000 op, 694429346.00 ns, 84.7692 ns/op

OverheadWarmup   1: 8192000 op, 20618.00 ns, 0.0025 ns/op
OverheadWarmup   2: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadWarmup   7: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadWarmup   8: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadWarmup   9: 8192000 op, 19365.00 ns, 0.0024 ns/op

OverheadActual   1: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18764.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 35506.00 ns, 0.0043 ns/op
OverheadActual   7: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 19336.00 ns, 0.0024 ns/op
OverheadActual   9: 8192000 op, 18764.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 18795.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 702509242.00 ns, 85.7555 ns/op
WorkloadWarmup   2: 8192000 op, 700181291.00 ns, 85.4713 ns/op
WorkloadWarmup   3: 8192000 op, 690675781.00 ns, 84.3110 ns/op
WorkloadWarmup   4: 8192000 op, 690814209.00 ns, 84.3279 ns/op
WorkloadWarmup   5: 8192000 op, 690572239.00 ns, 84.2984 ns/op
WorkloadWarmup   6: 8192000 op, 692404078.00 ns, 84.5220 ns/op
WorkloadWarmup   7: 8192000 op, 690227758.00 ns, 84.2563 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 696885608.00 ns, 85.0690 ns/op
WorkloadActual   2: 8192000 op, 692833236.00 ns, 84.5744 ns/op
WorkloadActual   3: 8192000 op, 690045428.00 ns, 84.2341 ns/op
WorkloadActual   4: 8192000 op, 694176542.00 ns, 84.7383 ns/op
WorkloadActual   5: 8192000 op, 691258912.00 ns, 84.3822 ns/op
WorkloadActual   6: 8192000 op, 691885847.00 ns, 84.4587 ns/op
WorkloadActual   7: 8192000 op, 692033351.00 ns, 84.4767 ns/op
WorkloadActual   8: 8192000 op, 692997763.00 ns, 84.5945 ns/op
WorkloadActual   9: 8192000 op, 689906767.00 ns, 84.2171 ns/op
WorkloadActual  10: 8192000 op, 691894392.00 ns, 84.4598 ns/op
WorkloadActual  11: 8192000 op, 690905871.00 ns, 84.3391 ns/op
WorkloadActual  12: 8192000 op, 694147172.00 ns, 84.7348 ns/op
WorkloadActual  13: 8192000 op, 691006848.00 ns, 84.3514 ns/op
WorkloadActual  14: 8192000 op, 692122805.00 ns, 84.4876 ns/op
WorkloadActual  15: 8192000 op, 691285949.00 ns, 84.3855 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 692814441.00 ns, 84.5721 ns/op
WorkloadResult   2: 8192000 op, 690026633.00 ns, 84.2318 ns/op
WorkloadResult   3: 8192000 op, 694157747.00 ns, 84.7361 ns/op
WorkloadResult   4: 8192000 op, 691240117.00 ns, 84.3799 ns/op
WorkloadResult   5: 8192000 op, 691867052.00 ns, 84.4564 ns/op
WorkloadResult   6: 8192000 op, 692014556.00 ns, 84.4744 ns/op
WorkloadResult   7: 8192000 op, 692978968.00 ns, 84.5922 ns/op
WorkloadResult   8: 8192000 op, 689887972.00 ns, 84.2148 ns/op
WorkloadResult   9: 8192000 op, 691875597.00 ns, 84.4575 ns/op
WorkloadResult  10: 8192000 op, 690887076.00 ns, 84.3368 ns/op
WorkloadResult  11: 8192000 op, 694128377.00 ns, 84.7325 ns/op
WorkloadResult  12: 8192000 op, 690988053.00 ns, 84.3491 ns/op
WorkloadResult  13: 8192000 op, 692104010.00 ns, 84.4854 ns/op
WorkloadResult  14: 8192000 op, 691267154.00 ns, 84.3832 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4385 has exited with code 0.

Mean = 84.457 ns, StdErr = 0.043 ns (0.05%), N = 14, StdDev = 0.160 ns
Min = 84.215 ns, Q1 = 84.357 ns, Median = 84.457 ns, Q3 = 84.550 ns, Max = 84.736 ns
IQR = 0.194 ns, LowerFence = 84.066 ns, UpperFence = 84.841 ns
ConfidenceInterval = [84.277 ns; 84.638 ns] (CI 99.9%), Margin = 0.181 ns (0.21% of Mean)
Skewness = 0.29, Kurtosis = 2.04, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-04 14:50 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 202145.00 ns, 202.1450 ns/op
WorkloadJitting  1: 1000 op, 1814698.00 ns, 1.8147 us/op

OverheadJitting  2: 16000 op, 213427.00 ns, 13.3392 ns/op
WorkloadJitting  2: 16000 op, 20022014.00 ns, 1.2514 us/op

WorkloadPilot    1: 16000 op, 17935442.00 ns, 1.1210 us/op
WorkloadPilot    2: 32000 op, 33816343.00 ns, 1.0568 us/op
WorkloadPilot    3: 64000 op, 66456043.00 ns, 1.0384 us/op
WorkloadPilot    4: 128000 op, 117840196.00 ns, 920.6265 ns/op
WorkloadPilot    5: 256000 op, 44821786.00 ns, 175.0851 ns/op
WorkloadPilot    6: 512000 op, 83088868.00 ns, 162.2829 ns/op
WorkloadPilot    7: 1024000 op, 167496664.00 ns, 163.5710 ns/op
WorkloadPilot    8: 2048000 op, 334181922.00 ns, 163.1748 ns/op
WorkloadPilot    9: 4096000 op, 666346050.00 ns, 162.6821 ns/op

OverheadWarmup   1: 4096000 op, 11832.00 ns, 0.0029 ns/op
OverheadWarmup   2: 4096000 op, 9637.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 12294.00 ns, 0.0030 ns/op
OverheadWarmup   4: 4096000 op, 9608.00 ns, 0.0023 ns/op
OverheadWarmup   5: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadWarmup   6: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadWarmup   7: 4096000 op, 9598.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   2: 4096000 op, 9608.00 ns, 0.0023 ns/op
OverheadActual   3: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual   5: 4096000 op, 9617.00 ns, 0.0023 ns/op
OverheadActual   6: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual   7: 4096000 op, 9627.00 ns, 0.0024 ns/op
OverheadActual   8: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual   9: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual  10: 4096000 op, 10669.00 ns, 0.0026 ns/op
OverheadActual  11: 4096000 op, 9627.00 ns, 0.0024 ns/op
OverheadActual  12: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  13: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual  14: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  15: 4096000 op, 18274.00 ns, 0.0045 ns/op

WorkloadWarmup   1: 4096000 op, 683870807.00 ns, 166.9606 ns/op
WorkloadWarmup   2: 4096000 op, 675901956.00 ns, 165.0151 ns/op
WorkloadWarmup   3: 4096000 op, 669479506.00 ns, 163.4471 ns/op
WorkloadWarmup   4: 4096000 op, 666757212.00 ns, 162.7825 ns/op
WorkloadWarmup   5: 4096000 op, 664936074.00 ns, 162.3379 ns/op
WorkloadWarmup   6: 4096000 op, 667114226.00 ns, 162.8697 ns/op
WorkloadWarmup   7: 4096000 op, 670036101.00 ns, 163.5830 ns/op
WorkloadWarmup   8: 4096000 op, 670776778.00 ns, 163.7639 ns/op
WorkloadWarmup   9: 4096000 op, 672806926.00 ns, 164.2595 ns/op
WorkloadWarmup  10: 4096000 op, 670158811.00 ns, 163.6130 ns/op
WorkloadWarmup  11: 4096000 op, 671691663.00 ns, 163.9872 ns/op
WorkloadWarmup  12: 4096000 op, 671467958.00 ns, 163.9326 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 670307158.00 ns, 163.6492 ns/op
WorkloadActual   2: 4096000 op, 669956806.00 ns, 163.5637 ns/op
WorkloadActual   3: 4096000 op, 671338777.00 ns, 163.9011 ns/op
WorkloadActual   4: 4096000 op, 678233390.00 ns, 165.5843 ns/op
WorkloadActual   5: 4096000 op, 663135174.00 ns, 161.8982 ns/op
WorkloadActual   6: 4096000 op, 664176381.00 ns, 162.1524 ns/op
WorkloadActual   7: 4096000 op, 668830691.00 ns, 163.2887 ns/op
WorkloadActual   8: 4096000 op, 670113279.00 ns, 163.6019 ns/op
WorkloadActual   9: 4096000 op, 667378015.00 ns, 162.9341 ns/op
WorkloadActual  10: 4096000 op, 671502539.00 ns, 163.9410 ns/op
WorkloadActual  11: 4096000 op, 667848973.00 ns, 163.0491 ns/op
WorkloadActual  12: 4096000 op, 671139623.00 ns, 163.8524 ns/op
WorkloadActual  13: 4096000 op, 669728725.00 ns, 163.5080 ns/op
WorkloadActual  14: 4096000 op, 669491851.00 ns, 163.4502 ns/op
WorkloadActual  15: 4096000 op, 669439613.00 ns, 163.4374 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 670297531.00 ns, 163.6469 ns/op
WorkloadResult   2: 4096000 op, 669947179.00 ns, 163.5613 ns/op
WorkloadResult   3: 4096000 op, 671329150.00 ns, 163.8987 ns/op
WorkloadResult   4: 4096000 op, 663125547.00 ns, 161.8959 ns/op
WorkloadResult   5: 4096000 op, 664166754.00 ns, 162.1501 ns/op
WorkloadResult   6: 4096000 op, 668821064.00 ns, 163.2864 ns/op
WorkloadResult   7: 4096000 op, 670103652.00 ns, 163.5995 ns/op
WorkloadResult   8: 4096000 op, 667368388.00 ns, 162.9317 ns/op
WorkloadResult   9: 4096000 op, 671492912.00 ns, 163.9387 ns/op
WorkloadResult  10: 4096000 op, 667839346.00 ns, 163.0467 ns/op
WorkloadResult  11: 4096000 op, 671129996.00 ns, 163.8501 ns/op
WorkloadResult  12: 4096000 op, 669719098.00 ns, 163.5056 ns/op
WorkloadResult  13: 4096000 op, 669482224.00 ns, 163.4478 ns/op
WorkloadResult  14: 4096000 op, 669429986.00 ns, 163.4351 ns/op
// GC:  47 0 0 786432032 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4400 has exited with code 0.

Mean = 163.300 ns, StdErr = 0.164 ns (0.10%), N = 14, StdDev = 0.615 ns
Min = 161.896 ns, Q1 = 163.107 ns, Median = 163.477 ns, Q3 = 163.635 ns, Max = 163.939 ns
IQR = 0.528 ns, LowerFence = 162.314 ns, UpperFence = 164.428 ns
ConfidenceInterval = [162.605 ns; 163.994 ns] (CI 99.9%), Margin = 0.694 ns (0.43% of Mean)
Skewness = -1.11, Kurtosis = 3.02, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-04 14:50 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 212265.00 ns, 212.2650 ns/op
WorkloadJitting  1: 1000 op, 1099886.00 ns, 1.0999 us/op

OverheadJitting  2: 16000 op, 212285.00 ns, 13.2678 ns/op
WorkloadJitting  2: 16000 op, 7540609.00 ns, 471.2881 ns/op

WorkloadPilot    1: 16000 op, 6210084.00 ns, 388.1303 ns/op
WorkloadPilot    2: 32000 op, 13698386.00 ns, 428.0746 ns/op
WorkloadPilot    3: 64000 op, 28311468.00 ns, 442.3667 ns/op
WorkloadPilot    4: 128000 op, 47085362.00 ns, 367.8544 ns/op
WorkloadPilot    5: 256000 op, 63744441.00 ns, 249.0017 ns/op
WorkloadPilot    6: 512000 op, 35660135.00 ns, 69.6487 ns/op
WorkloadPilot    7: 1024000 op, 72911391.00 ns, 71.2025 ns/op
WorkloadPilot    8: 2048000 op, 140722333.00 ns, 68.7121 ns/op
WorkloadPilot    9: 4096000 op, 279738251.00 ns, 68.2955 ns/op
WorkloadPilot   10: 8192000 op, 559318768.00 ns, 68.2762 ns/op

OverheadWarmup   1: 8192000 op, 42700.00 ns, 0.0052 ns/op
OverheadWarmup   2: 8192000 op, 45895.00 ns, 0.0056 ns/op
OverheadWarmup   3: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18755.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 22472.00 ns, 0.0027 ns/op
OverheadActual   4: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 22021.00 ns, 0.0027 ns/op
OverheadActual  12: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 18925.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 18766.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 568451098.00 ns, 69.3910 ns/op
WorkloadWarmup   2: 8192000 op, 566940230.00 ns, 69.2066 ns/op
WorkloadWarmup   3: 8192000 op, 561949096.00 ns, 68.5973 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 562023104.00 ns, 68.6063 ns/op
WorkloadActual   2: 8192000 op, 559284264.00 ns, 68.2720 ns/op
WorkloadActual   3: 8192000 op, 559560408.00 ns, 68.3057 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 562004289.00 ns, 68.6040 ns/op
WorkloadResult   2: 8192000 op, 559265449.00 ns, 68.2697 ns/op
WorkloadResult   3: 8192000 op, 559541593.00 ns, 68.3034 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4414 has exited with code 0.

Mean = 68.392 ns, StdErr = 0.106 ns (0.16%), N = 3, StdDev = 0.184 ns
Min = 68.270 ns, Q1 = 68.287 ns, Median = 68.303 ns, Q3 = 68.454 ns, Max = 68.604 ns
IQR = 0.167 ns, LowerFence = 68.036 ns, UpperFence = 68.704 ns
ConfidenceInterval = [65.034 ns; 71.750 ns] (CI 99.9%), Margin = 3.358 ns (4.91% of Mean)
Skewness = 0.37, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-04 14:50 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 209380.00 ns, 209.3800 ns/op
WorkloadJitting  1: 1000 op, 1426947.00 ns, 1.4269 us/op

OverheadJitting  2: 16000 op, 261206.00 ns, 16.3254 ns/op
WorkloadJitting  2: 16000 op, 13618578.00 ns, 851.1611 ns/op

WorkloadPilot    1: 16000 op, 11949039.00 ns, 746.8149 ns/op
WorkloadPilot    2: 32000 op, 22604441.00 ns, 706.3888 ns/op
WorkloadPilot    3: 64000 op, 41613154.00 ns, 650.2055 ns/op
WorkloadPilot    4: 128000 op, 69914262.00 ns, 546.2052 ns/op
WorkloadPilot    5: 256000 op, 39151332.00 ns, 152.9349 ns/op
WorkloadPilot    6: 512000 op, 63681019.00 ns, 124.3770 ns/op
WorkloadPilot    7: 1024000 op, 124272439.00 ns, 121.3598 ns/op
WorkloadPilot    8: 2048000 op, 249346040.00 ns, 121.7510 ns/op
WorkloadPilot    9: 4096000 op, 503328984.00 ns, 122.8831 ns/op

OverheadWarmup   1: 4096000 op, 13025.00 ns, 0.0032 ns/op
OverheadWarmup   2: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadWarmup   4: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadWarmup   5: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadWarmup   6: 4096000 op, 9928.00 ns, 0.0024 ns/op
OverheadWarmup   7: 4096000 op, 9618.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   2: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   6: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   7: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual   8: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual   9: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual  10: 4096000 op, 11691.00 ns, 0.0029 ns/op
OverheadActual  11: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual  12: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual  13: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  14: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual  15: 4096000 op, 9618.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 4096000 op, 504170087.00 ns, 123.0884 ns/op
WorkloadWarmup   2: 4096000 op, 506081432.00 ns, 123.5550 ns/op
WorkloadWarmup   3: 4096000 op, 500036788.00 ns, 122.0793 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 504720526.00 ns, 123.2228 ns/op
WorkloadActual   2: 4096000 op, 505479919.00 ns, 123.4082 ns/op
WorkloadActual   3: 4096000 op, 502740281.00 ns, 122.7393 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 504710888.00 ns, 123.2204 ns/op
WorkloadResult   2: 4096000 op, 505470281.00 ns, 123.4058 ns/op
WorkloadResult   3: 4096000 op, 502730643.00 ns, 122.7370 ns/op
// GC:  64 0 0 1081344000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4424 has exited with code 0.

Mean = 123.121 ns, StdErr = 0.199 ns (0.16%), N = 3, StdDev = 0.345 ns
Min = 122.737 ns, Q1 = 122.979 ns, Median = 123.220 ns, Q3 = 123.313 ns, Max = 123.406 ns
IQR = 0.334 ns, LowerFence = 122.477 ns, UpperFence = 123.815 ns
ConfidenceInterval = [116.821 ns; 129.421 ns] (CI 99.9%), Margin = 6.300 ns (5.12% of Mean)
Skewness = -0.26, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-04 14:50 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 226032.00 ns, 226.0320 ns/op
WorkloadJitting  1: 1000 op, 1222732.00 ns, 1.2227 us/op

OverheadJitting  2: 16000 op, 241259.00 ns, 15.0787 ns/op
WorkloadJitting  2: 16000 op, 10538214.00 ns, 658.6384 ns/op

WorkloadPilot    1: 16000 op, 9159301.00 ns, 572.4563 ns/op
WorkloadPilot    2: 32000 op, 16950338.00 ns, 529.6981 ns/op
WorkloadPilot    3: 64000 op, 34017694.00 ns, 531.5265 ns/op
WorkloadPilot    4: 128000 op, 69802486.00 ns, 545.3319 ns/op
WorkloadPilot    5: 256000 op, 30743244.00 ns, 120.0908 ns/op
WorkloadPilot    6: 512000 op, 41744492.00 ns, 81.5322 ns/op
WorkloadPilot    7: 1024000 op, 84006039.00 ns, 82.0371 ns/op
WorkloadPilot    8: 2048000 op, 167429711.00 ns, 81.7528 ns/op
WorkloadPilot    9: 4096000 op, 333167856.00 ns, 81.3398 ns/op
WorkloadPilot   10: 8192000 op, 670533505.00 ns, 81.8522 ns/op

OverheadWarmup   1: 8192000 op, 23054.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18915.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18856.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18855.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 22432.00 ns, 0.0027 ns/op
OverheadActual   4: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18744.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 22042.00 ns, 0.0027 ns/op
OverheadActual  12: 8192000 op, 18885.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 18925.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 19085.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 18875.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 676351942.00 ns, 82.5625 ns/op
WorkloadWarmup   2: 8192000 op, 672782594.00 ns, 82.1268 ns/op
WorkloadWarmup   3: 8192000 op, 666079353.00 ns, 81.3085 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 671147667.00 ns, 81.9272 ns/op
WorkloadActual   2: 8192000 op, 666159483.00 ns, 81.3183 ns/op
WorkloadActual   3: 8192000 op, 666534440.00 ns, 81.3641 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 671128842.00 ns, 81.9249 ns/op
WorkloadResult   2: 8192000 op, 666140658.00 ns, 81.3160 ns/op
WorkloadResult   3: 8192000 op, 666515615.00 ns, 81.3618 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4434 has exited with code 0.

Mean = 81.534 ns, StdErr = 0.196 ns (0.24%), N = 3, StdDev = 0.339 ns
Min = 81.316 ns, Q1 = 81.339 ns, Median = 81.362 ns, Q3 = 81.643 ns, Max = 81.925 ns
IQR = 0.304 ns, LowerFence = 80.882 ns, UpperFence = 82.100 ns
ConfidenceInterval = [75.348 ns; 87.721 ns] (CI 99.9%), Margin = 6.187 ns (7.59% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-04 14:49 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 204881.00 ns, 204.8810 ns/op
WorkloadJitting  1: 1000 op, 1744497.00 ns, 1.7445 us/op

OverheadJitting  2: 16000 op, 231551.00 ns, 14.4719 ns/op
WorkloadJitting  2: 16000 op, 19614727.00 ns, 1.2259 us/op

WorkloadPilot    1: 16000 op, 17475816.00 ns, 1.0922 us/op
WorkloadPilot    2: 32000 op, 32822789.00 ns, 1.0257 us/op
WorkloadPilot    3: 64000 op, 64847583.00 ns, 1.0132 us/op
WorkloadPilot    4: 128000 op, 125100386.00 ns, 977.3468 ns/op
WorkloadPilot    5: 256000 op, 47998093.00 ns, 187.4926 ns/op
WorkloadPilot    6: 512000 op, 83069768.00 ns, 162.2456 ns/op
WorkloadPilot    7: 1024000 op, 166860537.00 ns, 162.9497 ns/op
WorkloadPilot    8: 2048000 op, 334915067.00 ns, 163.5327 ns/op
WorkloadPilot    9: 4096000 op, 667388031.00 ns, 162.9365 ns/op

OverheadWarmup   1: 4096000 op, 23854.00 ns, 0.0058 ns/op
OverheadWarmup   2: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadWarmup   3: 4096000 op, 9588.00 ns, 0.0023 ns/op
OverheadWarmup   4: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadWarmup   5: 4096000 op, 9588.00 ns, 0.0023 ns/op
OverheadWarmup   6: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadWarmup   7: 4096000 op, 9588.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   2: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 9617.00 ns, 0.0023 ns/op
OverheadActual   6: 4096000 op, 9588.00 ns, 0.0023 ns/op
OverheadActual   7: 4096000 op, 9567.00 ns, 0.0023 ns/op
OverheadActual   8: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual   9: 4096000 op, 9607.00 ns, 0.0023 ns/op
OverheadActual  10: 4096000 op, 11842.00 ns, 0.0029 ns/op
OverheadActual  11: 4096000 op, 9597.00 ns, 0.0023 ns/op
OverheadActual  12: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual  13: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual  14: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  15: 4096000 op, 9608.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 4096000 op, 679443707.00 ns, 165.8798 ns/op
WorkloadWarmup   2: 4096000 op, 672550202.00 ns, 164.1968 ns/op
WorkloadWarmup   3: 4096000 op, 668438868.00 ns, 163.1931 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 670976216.00 ns, 163.8126 ns/op
WorkloadActual   2: 4096000 op, 667106277.00 ns, 162.8677 ns/op
WorkloadActual   3: 4096000 op, 666851483.00 ns, 162.8055 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 670966608.00 ns, 163.8102 ns/op
WorkloadResult   2: 4096000 op, 667096669.00 ns, 162.8654 ns/op
WorkloadResult   3: 4096000 op, 666841875.00 ns, 162.8032 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4443 has exited with code 0.

Mean = 163.160 ns, StdErr = 0.326 ns (0.20%), N = 3, StdDev = 0.564 ns
Min = 162.803 ns, Q1 = 162.834 ns, Median = 162.865 ns, Q3 = 163.338 ns, Max = 163.810 ns
IQR = 0.504 ns, LowerFence = 162.079 ns, UpperFence = 164.093 ns
ConfidenceInterval = [152.865 ns; 173.455 ns] (CI 99.9%), Margin = 10.295 ns (6.31% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-04 14:49 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 66.857 ns, StdErr = 0.031 ns (0.05%), N = 14, StdDev = 0.117 ns
Min = 66.707 ns, Q1 = 66.767 ns, Median = 66.841 ns, Q3 = 66.898 ns, Max = 67.116 ns
IQR = 0.131 ns, LowerFence = 66.571 ns, UpperFence = 67.093 ns
ConfidenceInterval = [66.725 ns; 66.989 ns] (CI 99.9%), Margin = 0.132 ns (0.20% of Mean)
Skewness = 0.58, Kurtosis = 2.42, MValue = 2
-------------------- Histogram --------------------
[66.643 ns ; 67.179 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 130.638 ns, StdErr = 0.150 ns (0.11%), N = 15, StdDev = 0.580 ns
Min = 129.870 ns, Q1 = 130.178 ns, Median = 130.579 ns, Q3 = 130.962 ns, Max = 131.946 ns
IQR = 0.784 ns, LowerFence = 129.002 ns, UpperFence = 132.137 ns
ConfidenceInterval = [130.017 ns; 131.258 ns] (CI 99.9%), Margin = 0.621 ns (0.48% of Mean)
Skewness = 0.64, Kurtosis = 2.39, MValue = 2
-------------------- Histogram --------------------
[129.561 ns ; 132.255 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 84.457 ns, StdErr = 0.043 ns (0.05%), N = 14, StdDev = 0.160 ns
Min = 84.215 ns, Q1 = 84.357 ns, Median = 84.457 ns, Q3 = 84.550 ns, Max = 84.736 ns
IQR = 0.194 ns, LowerFence = 84.066 ns, UpperFence = 84.841 ns
ConfidenceInterval = [84.277 ns; 84.638 ns] (CI 99.9%), Margin = 0.181 ns (0.21% of Mean)
Skewness = 0.29, Kurtosis = 2.04, MValue = 2
-------------------- Histogram --------------------
[84.128 ns ; 84.823 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 163.300 ns, StdErr = 0.164 ns (0.10%), N = 14, StdDev = 0.615 ns
Min = 161.896 ns, Q1 = 163.107 ns, Median = 163.477 ns, Q3 = 163.635 ns, Max = 163.939 ns
IQR = 0.528 ns, LowerFence = 162.314 ns, UpperFence = 164.428 ns
ConfidenceInterval = [162.605 ns; 163.994 ns] (CI 99.9%), Margin = 0.694 ns (0.43% of Mean)
Skewness = -1.11, Kurtosis = 3.02, MValue = 2
-------------------- Histogram --------------------
[161.561 ns ; 164.274 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 68.392 ns, StdErr = 0.106 ns (0.16%), N = 3, StdDev = 0.184 ns
Min = 68.270 ns, Q1 = 68.287 ns, Median = 68.303 ns, Q3 = 68.454 ns, Max = 68.604 ns
IQR = 0.167 ns, LowerFence = 68.036 ns, UpperFence = 68.704 ns
ConfidenceInterval = [65.034 ns; 71.750 ns] (CI 99.9%), Margin = 3.358 ns (4.91% of Mean)
Skewness = 0.37, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[68.269 ns ; 68.604 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 123.121 ns, StdErr = 0.199 ns (0.16%), N = 3, StdDev = 0.345 ns
Min = 122.737 ns, Q1 = 122.979 ns, Median = 123.220 ns, Q3 = 123.313 ns, Max = 123.406 ns
IQR = 0.334 ns, LowerFence = 122.477 ns, UpperFence = 123.815 ns
ConfidenceInterval = [116.821 ns; 129.421 ns] (CI 99.9%), Margin = 6.300 ns (5.12% of Mean)
Skewness = -0.26, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[122.423 ns ; 123.720 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 81.534 ns, StdErr = 0.196 ns (0.24%), N = 3, StdDev = 0.339 ns
Min = 81.316 ns, Q1 = 81.339 ns, Median = 81.362 ns, Q3 = 81.643 ns, Max = 81.925 ns
IQR = 0.304 ns, LowerFence = 80.882 ns, UpperFence = 82.100 ns
ConfidenceInterval = [75.348 ns; 87.721 ns] (CI 99.9%), Margin = 6.187 ns (7.59% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[81.312 ns ; 81.929 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 163.160 ns, StdErr = 0.326 ns (0.20%), N = 3, StdDev = 0.564 ns
Min = 162.803 ns, Q1 = 162.834 ns, Median = 162.865 ns, Q3 = 163.338 ns, Max = 163.810 ns
IQR = 0.504 ns, LowerFence = 162.079 ns, UpperFence = 164.093 ns
ConfidenceInterval = [152.865 ns; 173.455 ns] (CI 99.9%), Margin = 10.295 ns (6.31% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[162.793 ns ; 163.820 ns) | @@@
---------------------------------------------------

// * Summary *

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error     | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|----------:|---------:|-------:|----------:|
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  66.86 ns |  0.132 ns | 0.117 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 130.64 ns |  0.621 ns | 0.580 ns | 0.0156 |     264 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  84.46 ns |  0.181 ns | 0.160 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 163.30 ns |  0.694 ns | 0.615 ns | 0.0115 |     192 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  68.39 ns |  3.358 ns | 0.184 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 123.12 ns |  6.300 ns | 0.345 ns | 0.0156 |     264 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  81.53 ns |  6.187 ns | 0.339 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 163.16 ns | 10.295 ns | 0.564 ns | 0.0115 |     192 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput                        -> 1 outlier  was  removed (67.94 ns)
  CoreDispatchBenchmarks.'Request  Request': RunStrategy=Throughput                     -> 1 outlier  was  removed (85.07 ns)
  CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': RunStrategy=Throughput -> 1 outlier  was  removed, 3 outliers were detected (161.90 ns, 162.15 ns, 165.58 ns)
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
Run time: 00:01:33 (93.12 sec), executed benchmarks: 8

Global total time: 00:01:47 (107.65 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
