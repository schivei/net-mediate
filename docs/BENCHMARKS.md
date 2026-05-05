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

Run: 2026-05-05 12:03 UTC | Branch: copilot/implementar-long-term | Commit: 692888b

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
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  73.15 ns |  0.732 ns | 0.684 ns | 0.0018 |      32 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 127.97 ns |  1.502 ns | 1.331 ns | 0.0161 |     272 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  90.91 ns |  0.294 ns | 0.275 ns | 0.0061 |     104 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 168.96 ns |  1.156 ns | 1.081 ns | 0.0117 |     200 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  69.36 ns |  2.085 ns | 0.114 ns | 0.0018 |      32 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 135.85 ns | 46.516 ns | 2.550 ns | 0.0161 |     272 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  91.32 ns |  4.233 ns | 0.232 ns | 0.0061 |     104 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 175.35 ns | 31.198 ns | 1.710 ns | 0.0117 |     200 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.84 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 13.41 sec and exited with 0
// ***** Done, took 00:00:15 (15.32 sec)   *****
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

OverheadJitting  1: 1000 op, 217736.00 ns, 217.7360 ns/op
WorkloadJitting  1: 1000 op, 1315186.00 ns, 1.3152 us/op

OverheadJitting  2: 16000 op, 234037.00 ns, 14.6273 ns/op
WorkloadJitting  2: 16000 op, 9067896.00 ns, 566.7435 ns/op

WorkloadPilot    1: 16000 op, 7776473.00 ns, 486.0296 ns/op
WorkloadPilot    2: 32000 op, 15093800.00 ns, 471.6813 ns/op
WorkloadPilot    3: 64000 op, 30070000.00 ns, 469.8438 ns/op
WorkloadPilot    4: 128000 op, 67853064.00 ns, 530.1021 ns/op
WorkloadPilot    5: 256000 op, 101050466.00 ns, 394.7284 ns/op
WorkloadPilot    6: 512000 op, 40497715.00 ns, 79.0971 ns/op
WorkloadPilot    7: 1024000 op, 75042452.00 ns, 73.2836 ns/op
WorkloadPilot    8: 2048000 op, 148360564.00 ns, 72.4417 ns/op
WorkloadPilot    9: 4096000 op, 296832085.00 ns, 72.4688 ns/op
WorkloadPilot   10: 8192000 op, 594733778.00 ns, 72.5993 ns/op

OverheadWarmup   1: 8192000 op, 20569.00 ns, 0.0025 ns/op
OverheadWarmup   2: 8192000 op, 29585.00 ns, 0.0036 ns/op
OverheadWarmup   3: 8192000 op, 17423.00 ns, 0.0021 ns/op
OverheadWarmup   4: 8192000 op, 16812.00 ns, 0.0021 ns/op
OverheadWarmup   5: 8192000 op, 28273.00 ns, 0.0035 ns/op
OverheadWarmup   6: 8192000 op, 27802.00 ns, 0.0034 ns/op

OverheadActual   1: 8192000 op, 29204.00 ns, 0.0036 ns/op
OverheadActual   2: 8192000 op, 29044.00 ns, 0.0035 ns/op
OverheadActual   3: 8192000 op, 36919.00 ns, 0.0045 ns/op
OverheadActual   4: 8192000 op, 27582.00 ns, 0.0034 ns/op
OverheadActual   5: 8192000 op, 29345.00 ns, 0.0036 ns/op
OverheadActual   6: 8192000 op, 47028.00 ns, 0.0057 ns/op
OverheadActual   7: 8192000 op, 29155.00 ns, 0.0036 ns/op
OverheadActual   8: 8192000 op, 27501.00 ns, 0.0034 ns/op
OverheadActual   9: 8192000 op, 28713.00 ns, 0.0035 ns/op
OverheadActual  10: 8192000 op, 29365.00 ns, 0.0036 ns/op
OverheadActual  11: 8192000 op, 36829.00 ns, 0.0045 ns/op
OverheadActual  12: 8192000 op, 29114.00 ns, 0.0036 ns/op
OverheadActual  13: 8192000 op, 29224.00 ns, 0.0036 ns/op
OverheadActual  14: 8192000 op, 28713.00 ns, 0.0035 ns/op
OverheadActual  15: 8192000 op, 28002.00 ns, 0.0034 ns/op

WorkloadWarmup   1: 8192000 op, 623335094.00 ns, 76.0907 ns/op
WorkloadWarmup   2: 8192000 op, 603286501.00 ns, 73.6434 ns/op
WorkloadWarmup   3: 8192000 op, 596769219.00 ns, 72.8478 ns/op
WorkloadWarmup   4: 8192000 op, 596282127.00 ns, 72.7883 ns/op
WorkloadWarmup   5: 8192000 op, 596042206.00 ns, 72.7591 ns/op
WorkloadWarmup   6: 8192000 op, 593979715.00 ns, 72.5073 ns/op
WorkloadWarmup   7: 8192000 op, 596206623.00 ns, 72.7791 ns/op
WorkloadWarmup   8: 8192000 op, 594021153.00 ns, 72.5123 ns/op
WorkloadWarmup   9: 8192000 op, 595304980.00 ns, 72.6691 ns/op
WorkloadWarmup  10: 8192000 op, 595359691.00 ns, 72.6757 ns/op
WorkloadWarmup  11: 8192000 op, 594338054.00 ns, 72.5510 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 599900191.00 ns, 73.2300 ns/op
WorkloadActual   2: 8192000 op, 596108810.00 ns, 72.7672 ns/op
WorkloadActual   3: 8192000 op, 595961676.00 ns, 72.7492 ns/op
WorkloadActual   4: 8192000 op, 595133950.00 ns, 72.6482 ns/op
WorkloadActual   5: 8192000 op, 600905508.00 ns, 73.3527 ns/op
WorkloadActual   6: 8192000 op, 595799703.00 ns, 72.7295 ns/op
WorkloadActual   7: 8192000 op, 593262677.00 ns, 72.4198 ns/op
WorkloadActual   8: 8192000 op, 595317311.00 ns, 72.6706 ns/op
WorkloadActual   9: 8192000 op, 595559564.00 ns, 72.7001 ns/op
WorkloadActual  10: 8192000 op, 604445719.00 ns, 73.7849 ns/op
WorkloadActual  11: 8192000 op, 603774387.00 ns, 73.7029 ns/op
WorkloadActual  12: 8192000 op, 609313386.00 ns, 74.3791 ns/op
WorkloadActual  13: 8192000 op, 596386988.00 ns, 72.8011 ns/op
WorkloadActual  14: 8192000 op, 611591307.00 ns, 74.6571 ns/op
WorkloadActual  15: 8192000 op, 595624576.00 ns, 72.7081 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 599871036.00 ns, 73.2264 ns/op
WorkloadResult   2: 8192000 op, 596079655.00 ns, 72.7636 ns/op
WorkloadResult   3: 8192000 op, 595932521.00 ns, 72.7457 ns/op
WorkloadResult   4: 8192000 op, 595104795.00 ns, 72.6446 ns/op
WorkloadResult   5: 8192000 op, 600876353.00 ns, 73.3492 ns/op
WorkloadResult   6: 8192000 op, 595770548.00 ns, 72.7259 ns/op
WorkloadResult   7: 8192000 op, 593233522.00 ns, 72.4162 ns/op
WorkloadResult   8: 8192000 op, 595288156.00 ns, 72.6670 ns/op
WorkloadResult   9: 8192000 op, 595530409.00 ns, 72.6966 ns/op
WorkloadResult  10: 8192000 op, 604416564.00 ns, 73.7813 ns/op
WorkloadResult  11: 8192000 op, 603745232.00 ns, 73.6994 ns/op
WorkloadResult  12: 8192000 op, 609284231.00 ns, 74.3755 ns/op
WorkloadResult  13: 8192000 op, 596357833.00 ns, 72.7976 ns/op
WorkloadResult  14: 8192000 op, 611562152.00 ns, 74.6536 ns/op
WorkloadResult  15: 8192000 op, 595595421.00 ns, 72.7045 ns/op
// GC:  15 0 0 262144032 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4622 has exited with code 0.

Mean = 73.150 ns, StdErr = 0.177 ns (0.24%), N = 15, StdDev = 0.684 ns
Min = 72.416 ns, Q1 = 72.701 ns, Median = 72.764 ns, Q3 = 73.524 ns, Max = 74.654 ns
IQR = 0.824 ns, LowerFence = 71.465 ns, UpperFence = 74.760 ns
ConfidenceInterval = [72.418 ns; 73.881 ns] (CI 99.9%), Margin = 0.732 ns (1.00% of Mean)
Skewness = 0.96, Kurtosis = 2.5, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-05 12:04 (0h 2m from now) **
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

OverheadJitting  1: 1000 op, 228867.00 ns, 228.8670 ns/op
WorkloadJitting  1: 1000 op, 1402549.00 ns, 1.4025 us/op

OverheadJitting  2: 16000 op, 222966.00 ns, 13.9354 ns/op
WorkloadJitting  2: 16000 op, 13871647.00 ns, 866.9779 ns/op

WorkloadPilot    1: 16000 op, 11923067.00 ns, 745.1917 ns/op
WorkloadPilot    2: 32000 op, 22444427.00 ns, 701.3883 ns/op
WorkloadPilot    3: 64000 op, 42318333.00 ns, 661.2240 ns/op
WorkloadPilot    4: 128000 op, 93807292.00 ns, 732.8695 ns/op
WorkloadPilot    5: 256000 op, 69856182.00 ns, 272.8757 ns/op
WorkloadPilot    6: 512000 op, 64636894.00 ns, 126.2439 ns/op
WorkloadPilot    7: 1024000 op, 129027068.00 ns, 126.0030 ns/op
WorkloadPilot    8: 2048000 op, 258562234.00 ns, 126.2511 ns/op
WorkloadPilot    9: 4096000 op, 523349684.00 ns, 127.7709 ns/op

OverheadWarmup   1: 4096000 op, 20247.00 ns, 0.0049 ns/op
OverheadWarmup   2: 4096000 op, 8435.00 ns, 0.0021 ns/op
OverheadWarmup   3: 4096000 op, 8456.00 ns, 0.0021 ns/op
OverheadWarmup   4: 4096000 op, 8567.00 ns, 0.0021 ns/op
OverheadWarmup   5: 4096000 op, 8456.00 ns, 0.0021 ns/op
OverheadWarmup   6: 4096000 op, 8446.00 ns, 0.0021 ns/op
OverheadWarmup   7: 4096000 op, 8446.00 ns, 0.0021 ns/op
OverheadWarmup   8: 4096000 op, 8456.00 ns, 0.0021 ns/op

OverheadActual   1: 4096000 op, 8586.00 ns, 0.0021 ns/op
OverheadActual   2: 4096000 op, 8486.00 ns, 0.0021 ns/op
OverheadActual   3: 4096000 op, 8456.00 ns, 0.0021 ns/op
OverheadActual   4: 4096000 op, 8455.00 ns, 0.0021 ns/op
OverheadActual   5: 4096000 op, 8536.00 ns, 0.0021 ns/op
OverheadActual   6: 4096000 op, 8466.00 ns, 0.0021 ns/op
OverheadActual   7: 4096000 op, 8405.00 ns, 0.0021 ns/op
OverheadActual   8: 4096000 op, 8586.00 ns, 0.0021 ns/op
OverheadActual   9: 4096000 op, 10349.00 ns, 0.0025 ns/op
OverheadActual  10: 4096000 op, 8556.00 ns, 0.0021 ns/op
OverheadActual  11: 4096000 op, 8476.00 ns, 0.0021 ns/op
OverheadActual  12: 4096000 op, 15519.00 ns, 0.0038 ns/op
OverheadActual  13: 4096000 op, 15218.00 ns, 0.0037 ns/op
OverheadActual  14: 4096000 op, 14808.00 ns, 0.0036 ns/op
OverheadActual  15: 4096000 op, 15058.00 ns, 0.0037 ns/op
OverheadActual  16: 4096000 op, 8446.00 ns, 0.0021 ns/op
OverheadActual  17: 4096000 op, 8566.00 ns, 0.0021 ns/op
OverheadActual  18: 4096000 op, 8465.00 ns, 0.0021 ns/op
OverheadActual  19: 4096000 op, 8596.00 ns, 0.0021 ns/op
OverheadActual  20: 4096000 op, 8476.00 ns, 0.0021 ns/op

WorkloadWarmup   1: 4096000 op, 528976203.00 ns, 129.1446 ns/op
WorkloadWarmup   2: 4096000 op, 533251446.00 ns, 130.1883 ns/op
WorkloadWarmup   3: 4096000 op, 525286342.00 ns, 128.2437 ns/op
WorkloadWarmup   4: 4096000 op, 529247956.00 ns, 129.2109 ns/op
WorkloadWarmup   5: 4096000 op, 527799622.00 ns, 128.8573 ns/op
WorkloadWarmup   6: 4096000 op, 524246171.00 ns, 127.9898 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 524564204.00 ns, 128.0674 ns/op
WorkloadActual   2: 4096000 op, 524277650.00 ns, 127.9975 ns/op
WorkloadActual   3: 4096000 op, 524098054.00 ns, 127.9536 ns/op
WorkloadActual   4: 4096000 op, 521487741.00 ns, 127.3163 ns/op
WorkloadActual   5: 4096000 op, 543981018.00 ns, 132.8079 ns/op
WorkloadActual   6: 4096000 op, 524211547.00 ns, 127.9813 ns/op
WorkloadActual   7: 4096000 op, 527563018.00 ns, 128.7996 ns/op
WorkloadActual   8: 4096000 op, 530067345.00 ns, 129.4110 ns/op
WorkloadActual   9: 4096000 op, 528765404.00 ns, 129.0931 ns/op
WorkloadActual  10: 4096000 op, 533296095.00 ns, 130.1992 ns/op
WorkloadActual  11: 4096000 op, 513132804.00 ns, 125.2766 ns/op
WorkloadActual  12: 4096000 op, 526513520.00 ns, 128.5433 ns/op
WorkloadActual  13: 4096000 op, 525847136.00 ns, 128.3806 ns/op
WorkloadActual  14: 4096000 op, 518244920.00 ns, 126.5246 ns/op
WorkloadActual  15: 4096000 op, 516215170.00 ns, 126.0291 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 524555658.00 ns, 128.0653 ns/op
WorkloadResult   2: 4096000 op, 524269104.00 ns, 127.9954 ns/op
WorkloadResult   3: 4096000 op, 524089508.00 ns, 127.9515 ns/op
WorkloadResult   4: 4096000 op, 521479195.00 ns, 127.3143 ns/op
WorkloadResult   5: 4096000 op, 524203001.00 ns, 127.9792 ns/op
WorkloadResult   6: 4096000 op, 527554472.00 ns, 128.7975 ns/op
WorkloadResult   7: 4096000 op, 530058799.00 ns, 129.4089 ns/op
WorkloadResult   8: 4096000 op, 528756858.00 ns, 129.0910 ns/op
WorkloadResult   9: 4096000 op, 533287549.00 ns, 130.1972 ns/op
WorkloadResult  10: 4096000 op, 513124258.00 ns, 125.2745 ns/op
WorkloadResult  11: 4096000 op, 526504974.00 ns, 128.5413 ns/op
WorkloadResult  12: 4096000 op, 525838590.00 ns, 128.3786 ns/op
WorkloadResult  13: 4096000 op, 518236374.00 ns, 126.5226 ns/op
WorkloadResult  14: 4096000 op, 516206624.00 ns, 126.0270 ns/op
// GC:  66 0 0 1114112000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4635 has exited with code 0.

Mean = 127.967 ns, StdErr = 0.356 ns (0.28%), N = 14, StdDev = 1.331 ns
Min = 125.274 ns, Q1 = 127.474 ns, Median = 128.030 ns, Q3 = 128.733 ns, Max = 130.197 ns
IQR = 1.260 ns, LowerFence = 125.584 ns, UpperFence = 130.623 ns
ConfidenceInterval = [126.466 ns; 129.469 ns] (CI 99.9%), Margin = 1.502 ns (1.17% of Mean)
Skewness = -0.42, Kurtosis = 2.33, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-05 12:04 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 209141.00 ns, 209.1410 ns/op
WorkloadJitting  1: 1000 op, 1375599.00 ns, 1.3756 us/op

OverheadJitting  2: 16000 op, 213158.00 ns, 13.3224 ns/op
WorkloadJitting  2: 16000 op, 11220202.00 ns, 701.2626 ns/op

WorkloadPilot    1: 16000 op, 9677862.00 ns, 604.8664 ns/op
WorkloadPilot    2: 32000 op, 18006646.00 ns, 562.7077 ns/op
WorkloadPilot    3: 64000 op, 35437686.00 ns, 553.7138 ns/op
WorkloadPilot    4: 128000 op, 70135611.00 ns, 547.9345 ns/op
WorkloadPilot    5: 256000 op, 101447543.00 ns, 396.2795 ns/op
WorkloadPilot    6: 512000 op, 48106203.00 ns, 93.9574 ns/op
WorkloadPilot    7: 1024000 op, 93112799.00 ns, 90.9305 ns/op
WorkloadPilot    8: 2048000 op, 186673494.00 ns, 91.1492 ns/op
WorkloadPilot    9: 4096000 op, 368106352.00 ns, 89.8697 ns/op
WorkloadPilot   10: 8192000 op, 745502841.00 ns, 91.0038 ns/op

OverheadWarmup   1: 8192000 op, 52498.00 ns, 0.0064 ns/op
OverheadWarmup   2: 8192000 op, 16531.00 ns, 0.0020 ns/op
OverheadWarmup   3: 8192000 op, 16662.00 ns, 0.0020 ns/op
OverheadWarmup   4: 8192000 op, 16601.00 ns, 0.0020 ns/op
OverheadWarmup   5: 8192000 op, 16470.00 ns, 0.0020 ns/op
OverheadWarmup   6: 8192000 op, 16550.00 ns, 0.0020 ns/op
OverheadWarmup   7: 8192000 op, 16671.00 ns, 0.0020 ns/op
OverheadWarmup   8: 8192000 op, 16460.00 ns, 0.0020 ns/op

OverheadActual   1: 8192000 op, 19898.00 ns, 0.0024 ns/op
OverheadActual   2: 8192000 op, 17232.00 ns, 0.0021 ns/op
OverheadActual   3: 8192000 op, 16951.00 ns, 0.0021 ns/op
OverheadActual   4: 8192000 op, 16540.00 ns, 0.0020 ns/op
OverheadActual   5: 8192000 op, 16410.00 ns, 0.0020 ns/op
OverheadActual   6: 8192000 op, 16451.00 ns, 0.0020 ns/op
OverheadActual   7: 8192000 op, 16531.00 ns, 0.0020 ns/op
OverheadActual   8: 8192000 op, 17333.00 ns, 0.0021 ns/op
OverheadActual   9: 8192000 op, 20508.00 ns, 0.0025 ns/op
OverheadActual  10: 8192000 op, 16450.00 ns, 0.0020 ns/op
OverheadActual  11: 8192000 op, 16440.00 ns, 0.0020 ns/op
OverheadActual  12: 8192000 op, 16641.00 ns, 0.0020 ns/op
OverheadActual  13: 8192000 op, 16651.00 ns, 0.0020 ns/op
OverheadActual  14: 8192000 op, 16441.00 ns, 0.0020 ns/op
OverheadActual  15: 8192000 op, 16611.00 ns, 0.0020 ns/op

WorkloadWarmup   1: 8192000 op, 760488610.00 ns, 92.8331 ns/op
WorkloadWarmup   2: 8192000 op, 748479720.00 ns, 91.3672 ns/op
WorkloadWarmup   3: 8192000 op, 744364015.00 ns, 90.8647 ns/op
WorkloadWarmup   4: 8192000 op, 753917770.00 ns, 92.0310 ns/op
WorkloadWarmup   5: 8192000 op, 746427262.00 ns, 91.1166 ns/op
WorkloadWarmup   6: 8192000 op, 744979729.00 ns, 90.9399 ns/op
WorkloadWarmup   7: 8192000 op, 745362984.00 ns, 90.9867 ns/op
WorkloadWarmup   8: 8192000 op, 747025919.00 ns, 91.1897 ns/op
WorkloadWarmup   9: 8192000 op, 744611221.00 ns, 90.8949 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 746650630.00 ns, 91.1439 ns/op
WorkloadActual   2: 8192000 op, 747311800.00 ns, 91.2246 ns/op
WorkloadActual   3: 8192000 op, 744236171.00 ns, 90.8491 ns/op
WorkloadActual   4: 8192000 op, 748391250.00 ns, 91.3564 ns/op
WorkloadActual   5: 8192000 op, 742504116.00 ns, 90.6377 ns/op
WorkloadActual   6: 8192000 op, 746543142.00 ns, 91.1308 ns/op
WorkloadActual   7: 8192000 op, 749113307.00 ns, 91.4445 ns/op
WorkloadActual   8: 8192000 op, 743584549.00 ns, 90.7696 ns/op
WorkloadActual   9: 8192000 op, 742632991.00 ns, 90.6534 ns/op
WorkloadActual  10: 8192000 op, 743860164.00 ns, 90.8032 ns/op
WorkloadActual  11: 8192000 op, 744109690.00 ns, 90.8337 ns/op
WorkloadActual  12: 8192000 op, 744587843.00 ns, 90.8921 ns/op
WorkloadActual  13: 8192000 op, 742826062.00 ns, 90.6770 ns/op
WorkloadActual  14: 8192000 op, 742268551.00 ns, 90.6090 ns/op
WorkloadActual  15: 8192000 op, 743001650.00 ns, 90.6984 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 746634019.00 ns, 91.1418 ns/op
WorkloadResult   2: 8192000 op, 747295189.00 ns, 91.2226 ns/op
WorkloadResult   3: 8192000 op, 744219560.00 ns, 90.8471 ns/op
WorkloadResult   4: 8192000 op, 748374639.00 ns, 91.3543 ns/op
WorkloadResult   5: 8192000 op, 742487505.00 ns, 90.6357 ns/op
WorkloadResult   6: 8192000 op, 746526531.00 ns, 91.1287 ns/op
WorkloadResult   7: 8192000 op, 749096696.00 ns, 91.4425 ns/op
WorkloadResult   8: 8192000 op, 743567938.00 ns, 90.7676 ns/op
WorkloadResult   9: 8192000 op, 742616380.00 ns, 90.6514 ns/op
WorkloadResult  10: 8192000 op, 743843553.00 ns, 90.8012 ns/op
WorkloadResult  11: 8192000 op, 744093079.00 ns, 90.8317 ns/op
WorkloadResult  12: 8192000 op, 744571232.00 ns, 90.8900 ns/op
WorkloadResult  13: 8192000 op, 742809451.00 ns, 90.6750 ns/op
WorkloadResult  14: 8192000 op, 742251940.00 ns, 90.6069 ns/op
WorkloadResult  15: 8192000 op, 742985039.00 ns, 90.6964 ns/op
// GC:  50 0 0 851968000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4649 has exited with code 0.

Mean = 90.913 ns, StdErr = 0.071 ns (0.08%), N = 15, StdDev = 0.275 ns
Min = 90.607 ns, Q1 = 90.686 ns, Median = 90.832 ns, Q3 = 91.135 ns, Max = 91.442 ns
IQR = 0.450 ns, LowerFence = 90.011 ns, UpperFence = 91.810 ns
ConfidenceInterval = [90.619 ns; 91.207 ns] (CI 99.9%), Margin = 0.294 ns (0.32% of Mean)
Skewness = 0.61, Kurtosis = 1.82, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-05 12:04 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 214701.00 ns, 214.7010 ns/op
WorkloadJitting  1: 1000 op, 1950783.00 ns, 1.9508 us/op

OverheadJitting  2: 16000 op, 215192.00 ns, 13.4495 ns/op
WorkloadJitting  2: 16000 op, 20535412.00 ns, 1.2835 us/op

WorkloadPilot    1: 16000 op, 17847101.00 ns, 1.1154 us/op
WorkloadPilot    2: 32000 op, 33317314.00 ns, 1.0412 us/op
WorkloadPilot    3: 64000 op, 66488454.00 ns, 1.0389 us/op
WorkloadPilot    4: 128000 op, 112857913.00 ns, 881.7024 ns/op
WorkloadPilot    5: 256000 op, 46279422.00 ns, 180.7790 ns/op
WorkloadPilot    6: 512000 op, 86670144.00 ns, 169.2776 ns/op
WorkloadPilot    7: 1024000 op, 173585166.00 ns, 169.5168 ns/op
WorkloadPilot    8: 2048000 op, 349059922.00 ns, 170.4394 ns/op
WorkloadPilot    9: 4096000 op, 694544998.00 ns, 169.5666 ns/op

OverheadWarmup   1: 4096000 op, 23494.00 ns, 0.0057 ns/op
OverheadWarmup   2: 4096000 op, 8766.00 ns, 0.0021 ns/op
OverheadWarmup   3: 4096000 op, 8746.00 ns, 0.0021 ns/op
OverheadWarmup   4: 4096000 op, 9127.00 ns, 0.0022 ns/op
OverheadWarmup   5: 4096000 op, 8847.00 ns, 0.0022 ns/op
OverheadWarmup   6: 4096000 op, 8646.00 ns, 0.0021 ns/op
OverheadWarmup   7: 4096000 op, 8546.00 ns, 0.0021 ns/op
OverheadWarmup   8: 4096000 op, 8506.00 ns, 0.0021 ns/op
OverheadWarmup   9: 4096000 op, 8586.00 ns, 0.0021 ns/op
OverheadWarmup  10: 4096000 op, 8455.00 ns, 0.0021 ns/op

OverheadActual   1: 4096000 op, 8516.00 ns, 0.0021 ns/op
OverheadActual   2: 4096000 op, 8817.00 ns, 0.0022 ns/op
OverheadActual   3: 4096000 op, 8657.00 ns, 0.0021 ns/op
OverheadActual   4: 4096000 op, 8787.00 ns, 0.0021 ns/op
OverheadActual   5: 4096000 op, 8846.00 ns, 0.0022 ns/op
OverheadActual   6: 4096000 op, 13605.00 ns, 0.0033 ns/op
OverheadActual   7: 4096000 op, 11392.00 ns, 0.0028 ns/op
OverheadActual   8: 4096000 op, 8526.00 ns, 0.0021 ns/op
OverheadActual   9: 4096000 op, 8556.00 ns, 0.0021 ns/op
OverheadActual  10: 4096000 op, 8495.00 ns, 0.0021 ns/op
OverheadActual  11: 4096000 op, 8536.00 ns, 0.0021 ns/op
OverheadActual  12: 4096000 op, 8446.00 ns, 0.0021 ns/op
OverheadActual  13: 4096000 op, 8507.00 ns, 0.0021 ns/op
OverheadActual  14: 4096000 op, 8536.00 ns, 0.0021 ns/op
OverheadActual  15: 4096000 op, 8585.00 ns, 0.0021 ns/op

WorkloadWarmup   1: 4096000 op, 704251786.00 ns, 171.9365 ns/op
WorkloadWarmup   2: 4096000 op, 705174610.00 ns, 172.1618 ns/op
WorkloadWarmup   3: 4096000 op, 697797622.00 ns, 170.3607 ns/op
WorkloadWarmup   4: 4096000 op, 694071052.00 ns, 169.4509 ns/op
WorkloadWarmup   5: 4096000 op, 694700969.00 ns, 169.6047 ns/op
WorkloadWarmup   6: 4096000 op, 695378093.00 ns, 169.7700 ns/op
WorkloadWarmup   7: 4096000 op, 692377571.00 ns, 169.0375 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 696776886.00 ns, 170.1115 ns/op
WorkloadActual   2: 4096000 op, 699513191.00 ns, 170.7796 ns/op
WorkloadActual   3: 4096000 op, 694744845.00 ns, 169.6154 ns/op
WorkloadActual   4: 4096000 op, 696465789.00 ns, 170.0356 ns/op
WorkloadActual   5: 4096000 op, 693895108.00 ns, 169.4080 ns/op
WorkloadActual   6: 4096000 op, 692731095.00 ns, 169.1238 ns/op
WorkloadActual   7: 4096000 op, 691669531.00 ns, 168.8646 ns/op
WorkloadActual   8: 4096000 op, 692510632.00 ns, 169.0700 ns/op
WorkloadActual   9: 4096000 op, 689433796.00 ns, 168.3188 ns/op
WorkloadActual  10: 4096000 op, 691445323.00 ns, 168.8099 ns/op
WorkloadActual  11: 4096000 op, 692180806.00 ns, 168.9895 ns/op
WorkloadActual  12: 4096000 op, 689403088.00 ns, 168.3113 ns/op
WorkloadActual  13: 4096000 op, 681761166.00 ns, 166.4456 ns/op
WorkloadActual  14: 4096000 op, 692977404.00 ns, 169.1839 ns/op
WorkloadActual  15: 4096000 op, 685370718.00 ns, 167.3268 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 696768330.00 ns, 170.1095 ns/op
WorkloadResult   2: 4096000 op, 699504635.00 ns, 170.7775 ns/op
WorkloadResult   3: 4096000 op, 694736289.00 ns, 169.6134 ns/op
WorkloadResult   4: 4096000 op, 696457233.00 ns, 170.0335 ns/op
WorkloadResult   5: 4096000 op, 693886552.00 ns, 169.4059 ns/op
WorkloadResult   6: 4096000 op, 692722539.00 ns, 169.1217 ns/op
WorkloadResult   7: 4096000 op, 691660975.00 ns, 168.8625 ns/op
WorkloadResult   8: 4096000 op, 692502076.00 ns, 169.0679 ns/op
WorkloadResult   9: 4096000 op, 689425240.00 ns, 168.3167 ns/op
WorkloadResult  10: 4096000 op, 691436767.00 ns, 168.8078 ns/op
WorkloadResult  11: 4096000 op, 692172250.00 ns, 168.9874 ns/op
WorkloadResult  12: 4096000 op, 689394532.00 ns, 168.3092 ns/op
WorkloadResult  13: 4096000 op, 681752610.00 ns, 166.4435 ns/op
WorkloadResult  14: 4096000 op, 692968848.00 ns, 169.1818 ns/op
WorkloadResult  15: 4096000 op, 685362162.00 ns, 167.3247 ns/op
// GC:  48 0 0 819200000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4690 has exited with code 0.

Mean = 168.958 ns, StdErr = 0.279 ns (0.17%), N = 15, StdDev = 1.081 ns
Min = 166.444 ns, Q1 = 168.562 ns, Median = 169.068 ns, Q3 = 169.510 ns, Max = 170.777 ns
IQR = 0.947 ns, LowerFence = 167.141 ns, UpperFence = 170.931 ns
ConfidenceInterval = [167.802 ns; 170.113 ns] (CI 99.9%), Margin = 1.156 ns (0.68% of Mean)
Skewness = -0.61, Kurtosis = 3.01, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-05 12:04 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 202418.00 ns, 202.4180 ns/op
WorkloadJitting  1: 1000 op, 1129069.00 ns, 1.1291 us/op

OverheadJitting  2: 16000 op, 202679.00 ns, 12.6674 ns/op
WorkloadJitting  2: 16000 op, 8236777.00 ns, 514.7986 ns/op

WorkloadPilot    1: 16000 op, 7054898.00 ns, 440.9311 ns/op
WorkloadPilot    2: 32000 op, 13583704.00 ns, 424.4908 ns/op
WorkloadPilot    3: 64000 op, 26773502.00 ns, 418.3360 ns/op
WorkloadPilot    4: 128000 op, 54179777.00 ns, 423.2795 ns/op
WorkloadPilot    5: 256000 op, 61143771.00 ns, 238.8429 ns/op
WorkloadPilot    6: 512000 op, 37049308.00 ns, 72.3619 ns/op
WorkloadPilot    7: 1024000 op, 70797328.00 ns, 69.1380 ns/op
WorkloadPilot    8: 2048000 op, 142004060.00 ns, 69.3379 ns/op
WorkloadPilot    9: 4096000 op, 284971270.00 ns, 69.5731 ns/op
WorkloadPilot   10: 8192000 op, 571840255.00 ns, 69.8047 ns/op

OverheadWarmup   1: 8192000 op, 44243.00 ns, 0.0054 ns/op
OverheadWarmup   2: 8192000 op, 36078.00 ns, 0.0044 ns/op
OverheadWarmup   3: 8192000 op, 35887.00 ns, 0.0044 ns/op
OverheadWarmup   4: 8192000 op, 35887.00 ns, 0.0044 ns/op
OverheadWarmup   5: 8192000 op, 31910.00 ns, 0.0039 ns/op
OverheadWarmup   6: 8192000 op, 31129.00 ns, 0.0038 ns/op
OverheadWarmup   7: 8192000 op, 31168.00 ns, 0.0038 ns/op
OverheadWarmup   8: 8192000 op, 36037.00 ns, 0.0044 ns/op
OverheadWarmup   9: 8192000 op, 54372.00 ns, 0.0066 ns/op
OverheadWarmup  10: 8192000 op, 33723.00 ns, 0.0041 ns/op

OverheadActual   1: 8192000 op, 35457.00 ns, 0.0043 ns/op
OverheadActual   2: 8192000 op, 35797.00 ns, 0.0044 ns/op
OverheadActual   3: 8192000 op, 35627.00 ns, 0.0043 ns/op
OverheadActual   4: 8192000 op, 31619.00 ns, 0.0039 ns/op
OverheadActual   5: 8192000 op, 36037.00 ns, 0.0044 ns/op
OverheadActual   6: 8192000 op, 35737.00 ns, 0.0044 ns/op
OverheadActual   7: 8192000 op, 41228.00 ns, 0.0050 ns/op
OverheadActual   8: 8192000 op, 36017.00 ns, 0.0044 ns/op
OverheadActual   9: 8192000 op, 31609.00 ns, 0.0039 ns/op
OverheadActual  10: 8192000 op, 35607.00 ns, 0.0043 ns/op
OverheadActual  11: 8192000 op, 35817.00 ns, 0.0044 ns/op
OverheadActual  12: 8192000 op, 37280.00 ns, 0.0046 ns/op
OverheadActual  13: 8192000 op, 31389.00 ns, 0.0038 ns/op
OverheadActual  14: 8192000 op, 35907.00 ns, 0.0044 ns/op
OverheadActual  15: 8192000 op, 35015.00 ns, 0.0043 ns/op
OverheadActual  16: 8192000 op, 31358.00 ns, 0.0038 ns/op
OverheadActual  17: 8192000 op, 31378.00 ns, 0.0038 ns/op
OverheadActual  18: 8192000 op, 31529.00 ns, 0.0038 ns/op
OverheadActual  19: 8192000 op, 31970.00 ns, 0.0039 ns/op
OverheadActual  20: 8192000 op, 32911.00 ns, 0.0040 ns/op

WorkloadWarmup   1: 8192000 op, 576497144.00 ns, 70.3732 ns/op
WorkloadWarmup   2: 8192000 op, 577630969.00 ns, 70.5116 ns/op
WorkloadWarmup   3: 8192000 op, 569328824.00 ns, 69.4981 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 568596186.00 ns, 69.4087 ns/op
WorkloadActual   2: 8192000 op, 568976646.00 ns, 69.4552 ns/op
WorkloadActual   3: 8192000 op, 567198675.00 ns, 69.2381 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 568560654.00 ns, 69.4044 ns/op
WorkloadResult   2: 8192000 op, 568941114.00 ns, 69.4508 ns/op
WorkloadResult   3: 8192000 op, 567163143.00 ns, 69.2338 ns/op
// GC:  15 0 0 262144000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4705 has exited with code 0.

Mean = 69.363 ns, StdErr = 0.066 ns (0.10%), N = 3, StdDev = 0.114 ns
Min = 69.234 ns, Q1 = 69.319 ns, Median = 69.404 ns, Q3 = 69.428 ns, Max = 69.451 ns
IQR = 0.109 ns, LowerFence = 69.156 ns, UpperFence = 69.590 ns
ConfidenceInterval = [67.278 ns; 71.448 ns] (CI 99.9%), Margin = 2.085 ns (3.01% of Mean)
Skewness = -0.31, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-05 12:04 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 178634.00 ns, 178.6340 ns/op
WorkloadJitting  1: 1000 op, 1469084.00 ns, 1.4691 us/op

OverheadJitting  2: 16000 op, 181600.00 ns, 11.3500 ns/op
WorkloadJitting  2: 16000 op, 14271729.00 ns, 891.9831 ns/op

WorkloadPilot    1: 16000 op, 12330824.00 ns, 770.6765 ns/op
WorkloadPilot    2: 32000 op, 23171805.00 ns, 724.1189 ns/op
WorkloadPilot    3: 64000 op, 42419979.00 ns, 662.8122 ns/op
WorkloadPilot    4: 128000 op, 69502109.00 ns, 542.9852 ns/op
WorkloadPilot    5: 256000 op, 37138234.00 ns, 145.0712 ns/op
WorkloadPilot    6: 512000 op, 66229064.00 ns, 129.3536 ns/op
WorkloadPilot    7: 1024000 op, 130410284.00 ns, 127.3538 ns/op
WorkloadPilot    8: 2048000 op, 267814287.00 ns, 130.7687 ns/op
WorkloadPilot    9: 4096000 op, 544001765.00 ns, 132.8129 ns/op

OverheadWarmup   1: 4096000 op, 24536.00 ns, 0.0060 ns/op
OverheadWarmup   2: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 9629.00 ns, 0.0024 ns/op
OverheadWarmup   4: 4096000 op, 9588.00 ns, 0.0023 ns/op
OverheadWarmup   5: 4096000 op, 35386.00 ns, 0.0086 ns/op
OverheadWarmup   6: 4096000 op, 9618.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual   2: 4096000 op, 9889.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9718.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9889.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   6: 4096000 op, 9627.00 ns, 0.0024 ns/op
OverheadActual   7: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual   8: 4096000 op, 9588.00 ns, 0.0023 ns/op
OverheadActual   9: 4096000 op, 26649.00 ns, 0.0065 ns/op
OverheadActual  10: 4096000 op, 18966.00 ns, 0.0046 ns/op
OverheadActual  11: 4096000 op, 11813.00 ns, 0.0029 ns/op
OverheadActual  12: 4096000 op, 9608.00 ns, 0.0023 ns/op
OverheadActual  13: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual  14: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual  15: 4096000 op, 9618.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 4096000 op, 544577099.00 ns, 132.9534 ns/op
WorkloadWarmup   2: 4096000 op, 554747302.00 ns, 135.4364 ns/op
WorkloadWarmup   3: 4096000 op, 550124960.00 ns, 134.3079 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 550080576.00 ns, 134.2970 ns/op
WorkloadActual   2: 4096000 op, 568522802.00 ns, 138.7995 ns/op
WorkloadActual   3: 4096000 op, 550809739.00 ns, 134.4750 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 550070938.00 ns, 134.2947 ns/op
WorkloadResult   2: 4096000 op, 568513164.00 ns, 138.7972 ns/op
WorkloadResult   3: 4096000 op, 550800101.00 ns, 134.4727 ns/op
// GC:  66 0 0 1114112000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4716 has exited with code 0.

Mean = 135.855 ns, StdErr = 1.472 ns (1.08%), N = 3, StdDev = 2.550 ns
Min = 134.295 ns, Q1 = 134.384 ns, Median = 134.473 ns, Q3 = 136.635 ns, Max = 138.797 ns
IQR = 2.251 ns, LowerFence = 131.007 ns, UpperFence = 140.012 ns
ConfidenceInterval = [89.339 ns; 182.371 ns] (CI 99.9%), Margin = 46.516 ns (34.24% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-05 12:04 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 186268.00 ns, 186.2680 ns/op
WorkloadJitting  1: 1000 op, 1352957.00 ns, 1.3530 us/op

OverheadJitting  2: 16000 op, 290332.00 ns, 18.1458 ns/op
WorkloadJitting  2: 16000 op, 11142048.00 ns, 696.3780 ns/op

WorkloadPilot    1: 16000 op, 9153515.00 ns, 572.0947 ns/op
WorkloadPilot    2: 32000 op, 16696963.00 ns, 521.7801 ns/op
WorkloadPilot    3: 64000 op, 33493761.00 ns, 523.3400 ns/op
WorkloadPilot    4: 128000 op, 64254669.00 ns, 501.9896 ns/op
WorkloadPilot    5: 256000 op, 108433724.00 ns, 423.5692 ns/op
WorkloadPilot    6: 512000 op, 48437509.00 ns, 94.6045 ns/op
WorkloadPilot    7: 1024000 op, 92625001.00 ns, 90.4541 ns/op
WorkloadPilot    8: 2048000 op, 190009530.00 ns, 92.7781 ns/op
WorkloadPilot    9: 4096000 op, 371330975.00 ns, 90.6570 ns/op
WorkloadPilot   10: 8192000 op, 739690380.00 ns, 90.2942 ns/op

OverheadWarmup   1: 8192000 op, 22923.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 37150.00 ns, 0.0045 ns/op
OverheadWarmup   3: 8192000 op, 36588.00 ns, 0.0045 ns/op
OverheadWarmup   4: 8192000 op, 36598.00 ns, 0.0045 ns/op
OverheadWarmup   5: 8192000 op, 36568.00 ns, 0.0045 ns/op

OverheadActual   1: 8192000 op, 36548.00 ns, 0.0045 ns/op
OverheadActual   2: 8192000 op, 36759.00 ns, 0.0045 ns/op
OverheadActual   3: 8192000 op, 36859.00 ns, 0.0045 ns/op
OverheadActual   4: 8192000 op, 42639.00 ns, 0.0052 ns/op
OverheadActual   5: 8192000 op, 49563.00 ns, 0.0061 ns/op
OverheadActual   6: 8192000 op, 36558.00 ns, 0.0045 ns/op
OverheadActual   7: 8192000 op, 36509.00 ns, 0.0045 ns/op
OverheadActual   8: 8192000 op, 36438.00 ns, 0.0044 ns/op
OverheadActual   9: 8192000 op, 36478.00 ns, 0.0045 ns/op
OverheadActual  10: 8192000 op, 36408.00 ns, 0.0044 ns/op
OverheadActual  11: 8192000 op, 40275.00 ns, 0.0049 ns/op
OverheadActual  12: 8192000 op, 42099.00 ns, 0.0051 ns/op
OverheadActual  13: 8192000 op, 36558.00 ns, 0.0045 ns/op
OverheadActual  14: 8192000 op, 36448.00 ns, 0.0044 ns/op
OverheadActual  15: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  16: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadActual  17: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadActual  18: 8192000 op, 18856.00 ns, 0.0023 ns/op
OverheadActual  19: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  20: 8192000 op, 22301.00 ns, 0.0027 ns/op

WorkloadWarmup   1: 8192000 op, 745530638.00 ns, 91.0072 ns/op
WorkloadWarmup   2: 8192000 op, 753392903.00 ns, 91.9669 ns/op
WorkloadWarmup   3: 8192000 op, 741529949.00 ns, 90.5188 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 749129150.00 ns, 91.4464 ns/op
WorkloadActual   2: 8192000 op, 749308388.00 ns, 91.4683 ns/op
WorkloadActual   3: 8192000 op, 745930348.00 ns, 91.0560 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 749092656.50 ns, 91.4420 ns/op
WorkloadResult   2: 8192000 op, 749271894.50 ns, 91.4639 ns/op
WorkloadResult   3: 8192000 op, 745893854.50 ns, 91.0515 ns/op
// GC:  50 0 0 851968000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4723 has exited with code 0.

Mean = 91.319 ns, StdErr = 0.134 ns (0.15%), N = 3, StdDev = 0.232 ns
Min = 91.051 ns, Q1 = 91.247 ns, Median = 91.442 ns, Q3 = 91.453 ns, Max = 91.464 ns
IQR = 0.206 ns, LowerFence = 90.937 ns, UpperFence = 91.762 ns
ConfidenceInterval = [87.086 ns; 95.552 ns] (CI 99.9%), Margin = 4.233 ns (4.64% of Mean)
Skewness = -0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-05 12:04 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 189854.00 ns, 189.8540 ns/op
WorkloadJitting  1: 1000 op, 1772410.00 ns, 1.7724 us/op

OverheadJitting  2: 16000 op, 183834.00 ns, 11.4896 ns/op
WorkloadJitting  2: 16000 op, 20138044.00 ns, 1.2586 us/op

WorkloadPilot    1: 16000 op, 18506847.00 ns, 1.1567 us/op
WorkloadPilot    2: 32000 op, 33737053.00 ns, 1.0543 us/op
WorkloadPilot    3: 64000 op, 67364051.00 ns, 1.0526 us/op
WorkloadPilot    4: 128000 op, 114287062.00 ns, 892.8677 ns/op
WorkloadPilot    5: 256000 op, 50459005.00 ns, 197.1055 ns/op
WorkloadPilot    6: 512000 op, 89990311.00 ns, 175.7623 ns/op
WorkloadPilot    7: 1024000 op, 178271059.00 ns, 174.0928 ns/op
WorkloadPilot    8: 2048000 op, 359809122.00 ns, 175.6880 ns/op
WorkloadPilot    9: 4096000 op, 717012927.00 ns, 175.0520 ns/op

OverheadWarmup   1: 4096000 op, 13264.00 ns, 0.0032 ns/op
OverheadWarmup   2: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadWarmup   4: 4096000 op, 9588.00 ns, 0.0023 ns/op
OverheadWarmup   5: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadWarmup   6: 4096000 op, 9578.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual   2: 4096000 op, 9899.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9728.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual   6: 4096000 op, 9608.00 ns, 0.0023 ns/op
OverheadActual   7: 4096000 op, 9608.00 ns, 0.0023 ns/op
OverheadActual   8: 4096000 op, 9688.00 ns, 0.0024 ns/op
OverheadActual   9: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual  10: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual  11: 4096000 op, 12624.00 ns, 0.0031 ns/op
OverheadActual  12: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual  13: 4096000 op, 9758.00 ns, 0.0024 ns/op
OverheadActual  14: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual  15: 4096000 op, 9738.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 4096000 op, 719673947.00 ns, 175.7016 ns/op
WorkloadWarmup   2: 4096000 op, 722505436.00 ns, 176.3929 ns/op
WorkloadWarmup   3: 4096000 op, 717684592.00 ns, 175.2160 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 710374671.00 ns, 173.4313 ns/op
WorkloadActual   2: 4096000 op, 720500323.00 ns, 175.9034 ns/op
WorkloadActual   3: 4096000 op, 723821249.00 ns, 176.7142 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 710365013.00 ns, 173.4290 ns/op
WorkloadResult   2: 4096000 op, 720490665.00 ns, 175.9010 ns/op
WorkloadResult   3: 4096000 op, 723811591.00 ns, 176.7118 ns/op
// GC:  48 0 0 819200000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4732 has exited with code 0.

Mean = 175.347 ns, StdErr = 0.987 ns (0.56%), N = 3, StdDev = 1.710 ns
Min = 173.429 ns, Q1 = 174.665 ns, Median = 175.901 ns, Q3 = 176.306 ns, Max = 176.712 ns
IQR = 1.641 ns, LowerFence = 172.203 ns, UpperFence = 178.769 ns
ConfidenceInterval = [144.150 ns; 206.545 ns] (CI 99.9%), Margin = 31.198 ns (17.79% of Mean)
Skewness = -0.29, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-05 12:03 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 73.150 ns, StdErr = 0.177 ns (0.24%), N = 15, StdDev = 0.684 ns
Min = 72.416 ns, Q1 = 72.701 ns, Median = 72.764 ns, Q3 = 73.524 ns, Max = 74.654 ns
IQR = 0.824 ns, LowerFence = 71.465 ns, UpperFence = 74.760 ns
ConfidenceInterval = [72.418 ns; 73.881 ns] (CI 99.9%), Margin = 0.732 ns (1.00% of Mean)
Skewness = 0.96, Kurtosis = 2.5, MValue = 2
-------------------- Histogram --------------------
[72.267 ns ; 75.018 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 127.967 ns, StdErr = 0.356 ns (0.28%), N = 14, StdDev = 1.331 ns
Min = 125.274 ns, Q1 = 127.474 ns, Median = 128.030 ns, Q3 = 128.733 ns, Max = 130.197 ns
IQR = 1.260 ns, LowerFence = 125.584 ns, UpperFence = 130.623 ns
ConfidenceInterval = [126.466 ns; 129.469 ns] (CI 99.9%), Margin = 1.502 ns (1.17% of Mean)
Skewness = -0.42, Kurtosis = 2.33, MValue = 2
-------------------- Histogram --------------------
[124.549 ns ; 127.236 ns) | @@@
[127.236 ns ; 130.922 ns) | @@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 90.913 ns, StdErr = 0.071 ns (0.08%), N = 15, StdDev = 0.275 ns
Min = 90.607 ns, Q1 = 90.686 ns, Median = 90.832 ns, Q3 = 91.135 ns, Max = 91.442 ns
IQR = 0.450 ns, LowerFence = 90.011 ns, UpperFence = 91.810 ns
ConfidenceInterval = [90.619 ns; 91.207 ns] (CI 99.9%), Margin = 0.294 ns (0.32% of Mean)
Skewness = 0.61, Kurtosis = 1.82, MValue = 2
-------------------- Histogram --------------------
[90.461 ns ; 91.589 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 168.958 ns, StdErr = 0.279 ns (0.17%), N = 15, StdDev = 1.081 ns
Min = 166.444 ns, Q1 = 168.562 ns, Median = 169.068 ns, Q3 = 169.510 ns, Max = 170.777 ns
IQR = 0.947 ns, LowerFence = 167.141 ns, UpperFence = 170.931 ns
ConfidenceInterval = [167.802 ns; 170.113 ns] (CI 99.9%), Margin = 1.156 ns (0.68% of Mean)
Skewness = -0.61, Kurtosis = 3.01, MValue = 2
-------------------- Histogram --------------------
[165.868 ns ; 171.353 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 69.363 ns, StdErr = 0.066 ns (0.10%), N = 3, StdDev = 0.114 ns
Min = 69.234 ns, Q1 = 69.319 ns, Median = 69.404 ns, Q3 = 69.428 ns, Max = 69.451 ns
IQR = 0.109 ns, LowerFence = 69.156 ns, UpperFence = 69.590 ns
ConfidenceInterval = [67.278 ns; 71.448 ns] (CI 99.9%), Margin = 2.085 ns (3.01% of Mean)
Skewness = -0.31, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[69.130 ns ; 69.555 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 135.855 ns, StdErr = 1.472 ns (1.08%), N = 3, StdDev = 2.550 ns
Min = 134.295 ns, Q1 = 134.384 ns, Median = 134.473 ns, Q3 = 136.635 ns, Max = 138.797 ns
IQR = 2.251 ns, LowerFence = 131.007 ns, UpperFence = 140.012 ns
ConfidenceInterval = [89.339 ns; 182.371 ns] (CI 99.9%), Margin = 46.516 ns (34.24% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[134.226 ns ; 138.866 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 91.319 ns, StdErr = 0.134 ns (0.15%), N = 3, StdDev = 0.232 ns
Min = 91.051 ns, Q1 = 91.247 ns, Median = 91.442 ns, Q3 = 91.453 ns, Max = 91.464 ns
IQR = 0.206 ns, LowerFence = 90.937 ns, UpperFence = 91.762 ns
ConfidenceInterval = [87.086 ns; 95.552 ns] (CI 99.9%), Margin = 4.233 ns (4.64% of Mean)
Skewness = -0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[91.047 ns ; 91.469 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 175.347 ns, StdErr = 0.987 ns (0.56%), N = 3, StdDev = 1.710 ns
Min = 173.429 ns, Q1 = 174.665 ns, Median = 175.901 ns, Q3 = 176.306 ns, Max = 176.712 ns
IQR = 1.641 ns, LowerFence = 172.203 ns, UpperFence = 178.769 ns
ConfidenceInterval = [144.150 ns; 206.545 ns] (CI 99.9%), Margin = 31.198 ns (17.79% of Mean)
Skewness = -0.29, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[171.873 ns ; 178.268 ns) | @@@
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
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  73.15 ns |  0.732 ns | 0.684 ns | 0.0018 |      32 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 127.97 ns |  1.502 ns | 1.331 ns | 0.0161 |     272 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  90.91 ns |  0.294 ns | 0.275 ns | 0.0061 |     104 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 168.96 ns |  1.156 ns | 1.081 ns | 0.0117 |     200 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  69.36 ns |  2.085 ns | 0.114 ns | 0.0018 |      32 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 135.85 ns | 46.516 ns | 2.550 ns | 0.0161 |     272 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  91.32 ns |  4.233 ns | 0.232 ns | 0.0061 |     104 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 175.35 ns | 31.198 ns | 1.710 ns | 0.0117 |     200 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Notification  Notify': RunStrategy=Throughput                 -> 1 outlier  was  removed, 2 outliers were detected (125.28 ns, 132.81 ns)
  CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': RunStrategy=Throughput -> 1 outlier  was  detected (166.45 ns)
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
Run time: 00:01:34 (94.61 sec), executed benchmarks: 8

Global total time: 00:01:50 (110.05 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
