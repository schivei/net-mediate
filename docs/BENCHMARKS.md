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

Run: 2026-05-04 19:43 UTC | Branch: copilot/implement-medium-term | Commit: f314860

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
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  69.12 ns |  0.097 ns | 0.086 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 113.39 ns |  0.143 ns | 0.134 ns | 0.0157 |     264 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  79.69 ns |  0.136 ns | 0.120 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 155.10 ns |  0.263 ns | 0.205 ns | 0.0115 |     192 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  66.73 ns |  1.158 ns | 0.063 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 115.83 ns |  5.184 ns | 0.284 ns | 0.0157 |     264 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  81.50 ns |  8.191 ns | 0.449 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 158.18 ns | 11.406 ns | 0.625 ns | 0.0115 |     192 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.62 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 12.1 sec and exited with 0
// ***** Done, took 00:00:13 (13.78 sec)   *****
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

OverheadJitting  1: 1000 op, 170458.00 ns, 170.4580 ns/op
WorkloadJitting  1: 1000 op, 1021973.00 ns, 1.0220 us/op

OverheadJitting  2: 16000 op, 181168.00 ns, 11.3230 ns/op
WorkloadJitting  2: 16000 op, 7463411.00 ns, 466.4632 ns/op

WorkloadPilot    1: 16000 op, 6276871.00 ns, 392.3044 ns/op
WorkloadPilot    2: 32000 op, 12046076.00 ns, 376.4399 ns/op
WorkloadPilot    3: 64000 op, 23972020.00 ns, 374.5628 ns/op
WorkloadPilot    4: 128000 op, 50291151.00 ns, 392.8996 ns/op
WorkloadPilot    5: 256000 op, 61873355.00 ns, 241.6928 ns/op
WorkloadPilot    6: 512000 op, 35652846.00 ns, 69.6345 ns/op
WorkloadPilot    7: 1024000 op, 72447780.00 ns, 70.7498 ns/op
WorkloadPilot    8: 2048000 op, 142398009.00 ns, 69.5303 ns/op
WorkloadPilot    9: 4096000 op, 283199152.00 ns, 69.1404 ns/op
WorkloadPilot   10: 8192000 op, 564416044.00 ns, 68.8984 ns/op

OverheadWarmup   1: 8192000 op, 37049.00 ns, 0.0045 ns/op
OverheadWarmup   2: 8192000 op, 29294.00 ns, 0.0036 ns/op
OverheadWarmup   3: 8192000 op, 29315.00 ns, 0.0036 ns/op
OverheadWarmup   4: 8192000 op, 29375.00 ns, 0.0036 ns/op
OverheadWarmup   5: 8192000 op, 29224.00 ns, 0.0036 ns/op
OverheadWarmup   6: 8192000 op, 29045.00 ns, 0.0035 ns/op
OverheadWarmup   7: 8192000 op, 29325.00 ns, 0.0036 ns/op
OverheadWarmup   8: 8192000 op, 29064.00 ns, 0.0035 ns/op

OverheadActual   1: 8192000 op, 38532.00 ns, 0.0047 ns/op
OverheadActual   2: 8192000 op, 29896.00 ns, 0.0036 ns/op
OverheadActual   3: 8192000 op, 29255.00 ns, 0.0036 ns/op
OverheadActual   4: 8192000 op, 29244.00 ns, 0.0036 ns/op
OverheadActual   5: 8192000 op, 29435.00 ns, 0.0036 ns/op
OverheadActual   6: 8192000 op, 29274.00 ns, 0.0036 ns/op
OverheadActual   7: 8192000 op, 29225.00 ns, 0.0036 ns/op
OverheadActual   8: 8192000 op, 29335.00 ns, 0.0036 ns/op
OverheadActual   9: 8192000 op, 36127.00 ns, 0.0044 ns/op
OverheadActual  10: 8192000 op, 29274.00 ns, 0.0036 ns/op
OverheadActual  11: 8192000 op, 29004.00 ns, 0.0035 ns/op
OverheadActual  12: 8192000 op, 28784.00 ns, 0.0035 ns/op
OverheadActual  13: 8192000 op, 28563.00 ns, 0.0035 ns/op
OverheadActual  14: 8192000 op, 28302.00 ns, 0.0035 ns/op
OverheadActual  15: 8192000 op, 28723.00 ns, 0.0035 ns/op

WorkloadWarmup   1: 8192000 op, 574371977.00 ns, 70.1138 ns/op
WorkloadWarmup   2: 8192000 op, 572379236.00 ns, 69.8705 ns/op
WorkloadWarmup   3: 8192000 op, 567455394.00 ns, 69.2695 ns/op
WorkloadWarmup   4: 8192000 op, 565877765.00 ns, 69.0769 ns/op
WorkloadWarmup   5: 8192000 op, 564292212.00 ns, 68.8833 ns/op
WorkloadWarmup   6: 8192000 op, 565764425.00 ns, 69.0630 ns/op
WorkloadWarmup   7: 8192000 op, 565602030.00 ns, 69.0432 ns/op
WorkloadWarmup   8: 8192000 op, 566291424.00 ns, 69.1274 ns/op
WorkloadWarmup   9: 8192000 op, 564735156.00 ns, 68.9374 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 567342753.00 ns, 69.2557 ns/op
WorkloadActual   2: 8192000 op, 566738918.00 ns, 69.1820 ns/op
WorkloadActual   3: 8192000 op, 565722765.00 ns, 69.0580 ns/op
WorkloadActual   4: 8192000 op, 566033093.00 ns, 69.0958 ns/op
WorkloadActual   5: 8192000 op, 564808352.00 ns, 68.9463 ns/op
WorkloadActual   6: 8192000 op, 566891913.00 ns, 69.2007 ns/op
WorkloadActual   7: 8192000 op, 570796226.00 ns, 69.6773 ns/op
WorkloadActual   8: 8192000 op, 566844144.00 ns, 69.1948 ns/op
WorkloadActual   9: 8192000 op, 566946053.00 ns, 69.2073 ns/op
WorkloadActual  10: 8192000 op, 566096842.00 ns, 69.1036 ns/op
WorkloadActual  11: 8192000 op, 565689113.00 ns, 69.0538 ns/op
WorkloadActual  12: 8192000 op, 565985936.00 ns, 69.0901 ns/op
WorkloadActual  13: 8192000 op, 566429356.00 ns, 69.1442 ns/op
WorkloadActual  14: 8192000 op, 565355121.00 ns, 69.0131 ns/op
WorkloadActual  15: 8192000 op, 566672394.00 ns, 69.1739 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 567313498.00 ns, 69.2521 ns/op
WorkloadResult   2: 8192000 op, 566709663.00 ns, 69.1784 ns/op
WorkloadResult   3: 8192000 op, 565693510.00 ns, 69.0544 ns/op
WorkloadResult   4: 8192000 op, 566003838.00 ns, 69.0923 ns/op
WorkloadResult   5: 8192000 op, 564779097.00 ns, 68.9428 ns/op
WorkloadResult   6: 8192000 op, 566862658.00 ns, 69.1971 ns/op
WorkloadResult   7: 8192000 op, 566814889.00 ns, 69.1913 ns/op
WorkloadResult   8: 8192000 op, 566916798.00 ns, 69.2037 ns/op
WorkloadResult   9: 8192000 op, 566067587.00 ns, 69.1000 ns/op
WorkloadResult  10: 8192000 op, 565659858.00 ns, 69.0503 ns/op
WorkloadResult  11: 8192000 op, 565956681.00 ns, 69.0865 ns/op
WorkloadResult  12: 8192000 op, 566400101.00 ns, 69.1406 ns/op
WorkloadResult  13: 8192000 op, 565325866.00 ns, 69.0095 ns/op
WorkloadResult  14: 8192000 op, 566643139.00 ns, 69.1703 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4365 has exited with code 0.

Mean = 69.119 ns, StdErr = 0.023 ns (0.03%), N = 14, StdDev = 0.086 ns
Min = 68.943 ns, Q1 = 69.062 ns, Median = 69.120 ns, Q3 = 69.188 ns, Max = 69.252 ns
IQR = 0.126 ns, LowerFence = 68.874 ns, UpperFence = 69.377 ns
ConfidenceInterval = [69.022 ns; 69.217 ns] (CI 99.9%), Margin = 0.097 ns (0.14% of Mean)
Skewness = -0.37, Kurtosis = 2.06, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-04 19:44 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 174294.00 ns, 174.2940 ns/op
WorkloadJitting  1: 1000 op, 1314308.00 ns, 1.3143 us/op

OverheadJitting  2: 16000 op, 189763.00 ns, 11.8602 ns/op
WorkloadJitting  2: 16000 op, 13559964.00 ns, 847.4978 ns/op

WorkloadPilot    1: 16000 op, 12149568.00 ns, 759.3480 ns/op
WorkloadPilot    2: 32000 op, 22596784.00 ns, 706.1495 ns/op
WorkloadPilot    3: 64000 op, 41879065.00 ns, 654.3604 ns/op
WorkloadPilot    4: 128000 op, 76469225.00 ns, 597.4158 ns/op
WorkloadPilot    5: 256000 op, 31617413.00 ns, 123.5055 ns/op
WorkloadPilot    6: 512000 op, 58173822.00 ns, 113.6207 ns/op
WorkloadPilot    7: 1024000 op, 115986522.00 ns, 113.2681 ns/op
WorkloadPilot    8: 2048000 op, 231922309.00 ns, 113.2433 ns/op
WorkloadPilot    9: 4096000 op, 464573988.00 ns, 113.4214 ns/op
WorkloadPilot   10: 8192000 op, 927026584.00 ns, 113.1624 ns/op

OverheadWarmup   1: 8192000 op, 23013.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 20739.00 ns, 0.0025 ns/op
OverheadWarmup   3: 8192000 op, 21039.00 ns, 0.0026 ns/op
OverheadWarmup   4: 8192000 op, 23303.00 ns, 0.0028 ns/op
OverheadWarmup   5: 8192000 op, 20157.00 ns, 0.0025 ns/op
OverheadWarmup   6: 8192000 op, 23063.00 ns, 0.0028 ns/op
OverheadWarmup   7: 8192000 op, 21099.00 ns, 0.0026 ns/op

OverheadActual   1: 8192000 op, 23263.00 ns, 0.0028 ns/op
OverheadActual   2: 8192000 op, 24115.00 ns, 0.0029 ns/op
OverheadActual   3: 8192000 op, 21129.00 ns, 0.0026 ns/op
OverheadActual   4: 8192000 op, 23313.00 ns, 0.0028 ns/op
OverheadActual   5: 8192000 op, 20728.00 ns, 0.0025 ns/op
OverheadActual   6: 8192000 op, 21089.00 ns, 0.0026 ns/op
OverheadActual   7: 8192000 op, 20768.00 ns, 0.0025 ns/op
OverheadActual   8: 8192000 op, 20990.00 ns, 0.0026 ns/op
OverheadActual   9: 8192000 op, 23253.00 ns, 0.0028 ns/op
OverheadActual  10: 8192000 op, 21390.00 ns, 0.0026 ns/op
OverheadActual  11: 8192000 op, 23343.00 ns, 0.0028 ns/op
OverheadActual  12: 8192000 op, 46667.00 ns, 0.0057 ns/op
OverheadActual  13: 8192000 op, 22653.00 ns, 0.0028 ns/op
OverheadActual  14: 8192000 op, 23314.00 ns, 0.0028 ns/op
OverheadActual  15: 8192000 op, 20739.00 ns, 0.0025 ns/op
OverheadActual  16: 8192000 op, 20929.00 ns, 0.0026 ns/op
OverheadActual  17: 8192000 op, 20869.00 ns, 0.0025 ns/op
OverheadActual  18: 8192000 op, 20799.00 ns, 0.0025 ns/op
OverheadActual  19: 8192000 op, 20779.00 ns, 0.0025 ns/op
OverheadActual  20: 8192000 op, 20909.00 ns, 0.0026 ns/op

WorkloadWarmup   1: 8192000 op, 949668962.00 ns, 115.9264 ns/op
WorkloadWarmup   2: 8192000 op, 934709833.00 ns, 114.1003 ns/op
WorkloadWarmup   3: 8192000 op, 928255603.00 ns, 113.3125 ns/op
WorkloadWarmup   4: 8192000 op, 926195667.00 ns, 113.0610 ns/op
WorkloadWarmup   5: 8192000 op, 924627638.00 ns, 112.8696 ns/op
WorkloadWarmup   6: 8192000 op, 925245177.00 ns, 112.9450 ns/op
WorkloadWarmup   7: 8192000 op, 924461260.00 ns, 112.8493 ns/op
WorkloadWarmup   8: 8192000 op, 924910886.00 ns, 112.9042 ns/op
WorkloadWarmup   9: 8192000 op, 924732864.00 ns, 112.8824 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 928659564.00 ns, 113.3618 ns/op
WorkloadActual   2: 8192000 op, 929567039.00 ns, 113.4725 ns/op
WorkloadActual   3: 8192000 op, 927248538.00 ns, 113.1895 ns/op
WorkloadActual   4: 8192000 op, 927348409.00 ns, 113.2017 ns/op
WorkloadActual   5: 8192000 op, 928699610.00 ns, 113.3667 ns/op
WorkloadActual   6: 8192000 op, 928025056.00 ns, 113.2843 ns/op
WorkloadActual   7: 8192000 op, 930431345.00 ns, 113.5780 ns/op
WorkloadActual   8: 8192000 op, 928855101.00 ns, 113.3856 ns/op
WorkloadActual   9: 8192000 op, 927791931.00 ns, 113.2559 ns/op
WorkloadActual  10: 8192000 op, 928603352.00 ns, 113.3549 ns/op
WorkloadActual  11: 8192000 op, 928590978.00 ns, 113.3534 ns/op
WorkloadActual  12: 8192000 op, 930418224.00 ns, 113.5764 ns/op
WorkloadActual  13: 8192000 op, 930572090.00 ns, 113.5952 ns/op
WorkloadActual  14: 8192000 op, 930163518.00 ns, 113.5454 ns/op
WorkloadActual  15: 8192000 op, 929081083.00 ns, 113.4132 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 928638455.00 ns, 113.3592 ns/op
WorkloadResult   2: 8192000 op, 929545930.00 ns, 113.4700 ns/op
WorkloadResult   3: 8192000 op, 927227429.00 ns, 113.1869 ns/op
WorkloadResult   4: 8192000 op, 927327300.00 ns, 113.1991 ns/op
WorkloadResult   5: 8192000 op, 928678501.00 ns, 113.3641 ns/op
WorkloadResult   6: 8192000 op, 928003947.00 ns, 113.2817 ns/op
WorkloadResult   7: 8192000 op, 930410236.00 ns, 113.5755 ns/op
WorkloadResult   8: 8192000 op, 928833992.00 ns, 113.3831 ns/op
WorkloadResult   9: 8192000 op, 927770822.00 ns, 113.2533 ns/op
WorkloadResult  10: 8192000 op, 928582243.00 ns, 113.3523 ns/op
WorkloadResult  11: 8192000 op, 928569869.00 ns, 113.3508 ns/op
WorkloadResult  12: 8192000 op, 930397115.00 ns, 113.5739 ns/op
WorkloadResult  13: 8192000 op, 930550981.00 ns, 113.5926 ns/op
WorkloadResult  14: 8192000 op, 930142409.00 ns, 113.5428 ns/op
WorkloadResult  15: 8192000 op, 929059974.00 ns, 113.4106 ns/op
// GC:  129 0 0 2162688000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4377 has exited with code 0.

Mean = 113.393 ns, StdErr = 0.035 ns (0.03%), N = 15, StdDev = 0.134 ns
Min = 113.187 ns, Q1 = 113.316 ns, Median = 113.364 ns, Q3 = 113.506 ns, Max = 113.593 ns
IQR = 0.190 ns, LowerFence = 113.031 ns, UpperFence = 113.792 ns
ConfidenceInterval = [113.250 ns; 113.536 ns] (CI 99.9%), Margin = 0.143 ns (0.13% of Mean)
Skewness = 0.09, Kurtosis = 1.66, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-04 19:44 (0h 2m from now) **
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

OverheadJitting  1: 1000 op, 172621.00 ns, 172.6210 ns/op
WorkloadJitting  1: 1000 op, 1148469.00 ns, 1.1485 us/op

OverheadJitting  2: 16000 op, 180336.00 ns, 11.2710 ns/op
WorkloadJitting  2: 16000 op, 9728310.00 ns, 608.0194 ns/op

WorkloadPilot    1: 16000 op, 8512175.00 ns, 532.0109 ns/op
WorkloadPilot    2: 32000 op, 15954367.00 ns, 498.5740 ns/op
WorkloadPilot    3: 64000 op, 31829967.00 ns, 497.3432 ns/op
WorkloadPilot    4: 128000 op, 64556665.00 ns, 504.3489 ns/op
WorkloadPilot    5: 256000 op, 45967921.00 ns, 179.5622 ns/op
WorkloadPilot    6: 512000 op, 41377941.00 ns, 80.8163 ns/op
WorkloadPilot    7: 1024000 op, 82172987.00 ns, 80.2471 ns/op
WorkloadPilot    8: 2048000 op, 163937725.00 ns, 80.0477 ns/op
WorkloadPilot    9: 4096000 op, 326607448.00 ns, 79.7381 ns/op
WorkloadPilot   10: 8192000 op, 653460523.00 ns, 79.7681 ns/op

OverheadWarmup   1: 8192000 op, 20278.00 ns, 0.0025 ns/op
OverheadWarmup   2: 8192000 op, 16481.00 ns, 0.0020 ns/op
OverheadWarmup   3: 8192000 op, 16561.00 ns, 0.0020 ns/op
OverheadWarmup   4: 8192000 op, 16380.00 ns, 0.0020 ns/op
OverheadWarmup   5: 8192000 op, 16501.00 ns, 0.0020 ns/op
OverheadWarmup   6: 8192000 op, 16410.00 ns, 0.0020 ns/op

OverheadActual   1: 8192000 op, 16490.00 ns, 0.0020 ns/op
OverheadActual   2: 8192000 op, 16460.00 ns, 0.0020 ns/op
OverheadActual   3: 8192000 op, 19466.00 ns, 0.0024 ns/op
OverheadActual   4: 8192000 op, 16470.00 ns, 0.0020 ns/op
OverheadActual   5: 8192000 op, 16431.00 ns, 0.0020 ns/op
OverheadActual   6: 8192000 op, 16611.00 ns, 0.0020 ns/op
OverheadActual   7: 8192000 op, 16281.00 ns, 0.0020 ns/op
OverheadActual   8: 8192000 op, 28022.00 ns, 0.0034 ns/op
OverheadActual   9: 8192000 op, 16531.00 ns, 0.0020 ns/op
OverheadActual  10: 8192000 op, 16621.00 ns, 0.0020 ns/op
OverheadActual  11: 8192000 op, 19305.00 ns, 0.0024 ns/op
OverheadActual  12: 8192000 op, 16531.00 ns, 0.0020 ns/op
OverheadActual  13: 8192000 op, 16331.00 ns, 0.0020 ns/op
OverheadActual  14: 8192000 op, 16521.00 ns, 0.0020 ns/op
OverheadActual  15: 8192000 op, 16361.00 ns, 0.0020 ns/op

WorkloadWarmup   1: 8192000 op, 679121691.00 ns, 82.9006 ns/op
WorkloadWarmup   2: 8192000 op, 659658809.00 ns, 80.5248 ns/op
WorkloadWarmup   3: 8192000 op, 652963969.00 ns, 79.7075 ns/op
WorkloadWarmup   4: 8192000 op, 653864329.00 ns, 79.8174 ns/op
WorkloadWarmup   5: 8192000 op, 651699996.00 ns, 79.5532 ns/op
WorkloadWarmup   6: 8192000 op, 651686290.00 ns, 79.5515 ns/op
WorkloadWarmup   7: 8192000 op, 652834019.00 ns, 79.6917 ns/op
WorkloadWarmup   8: 8192000 op, 652110501.00 ns, 79.6033 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 658684995.00 ns, 80.4059 ns/op
WorkloadActual   2: 8192000 op, 652254720.00 ns, 79.6209 ns/op
WorkloadActual   3: 8192000 op, 652443512.00 ns, 79.6440 ns/op
WorkloadActual   4: 8192000 op, 653312380.00 ns, 79.7500 ns/op
WorkloadActual   5: 8192000 op, 652174561.00 ns, 79.6112 ns/op
WorkloadActual   6: 8192000 op, 651398074.00 ns, 79.5164 ns/op
WorkloadActual   7: 8192000 op, 653025235.00 ns, 79.7150 ns/op
WorkloadActual   8: 8192000 op, 655166112.00 ns, 79.9763 ns/op
WorkloadActual   9: 8192000 op, 652997925.00 ns, 79.7117 ns/op
WorkloadActual  10: 8192000 op, 652119977.00 ns, 79.6045 ns/op
WorkloadActual  11: 8192000 op, 652624815.00 ns, 79.6661 ns/op
WorkloadActual  12: 8192000 op, 652465981.00 ns, 79.6467 ns/op
WorkloadActual  13: 8192000 op, 654219085.00 ns, 79.8607 ns/op
WorkloadActual  14: 8192000 op, 653710529.00 ns, 79.7986 ns/op
WorkloadActual  15: 8192000 op, 652082547.00 ns, 79.5999 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 652238199.00 ns, 79.6189 ns/op
WorkloadResult   2: 8192000 op, 652426991.00 ns, 79.6420 ns/op
WorkloadResult   3: 8192000 op, 653295859.00 ns, 79.7480 ns/op
WorkloadResult   4: 8192000 op, 652158040.00 ns, 79.6091 ns/op
WorkloadResult   5: 8192000 op, 651381553.00 ns, 79.5143 ns/op
WorkloadResult   6: 8192000 op, 653008714.00 ns, 79.7130 ns/op
WorkloadResult   7: 8192000 op, 655149591.00 ns, 79.9743 ns/op
WorkloadResult   8: 8192000 op, 652981404.00 ns, 79.7096 ns/op
WorkloadResult   9: 8192000 op, 652103456.00 ns, 79.6025 ns/op
WorkloadResult  10: 8192000 op, 652608294.00 ns, 79.6641 ns/op
WorkloadResult  11: 8192000 op, 652449460.00 ns, 79.6447 ns/op
WorkloadResult  12: 8192000 op, 654202564.00 ns, 79.8587 ns/op
WorkloadResult  13: 8192000 op, 653694008.00 ns, 79.7966 ns/op
WorkloadResult  14: 8192000 op, 652066026.00 ns, 79.5979 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4394 has exited with code 0.

Mean = 79.692 ns, StdErr = 0.032 ns (0.04%), N = 14, StdDev = 0.120 ns
Min = 79.514 ns, Q1 = 79.612 ns, Median = 79.654 ns, Q3 = 79.739 ns, Max = 79.974 ns
IQR = 0.128 ns, LowerFence = 79.420 ns, UpperFence = 79.931 ns
ConfidenceInterval = [79.557 ns; 79.828 ns] (CI 99.9%), Margin = 0.136 ns (0.17% of Mean)
Skewness = 0.81, Kurtosis = 2.87, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-04 19:44 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 168874.00 ns, 168.8740 ns/op
WorkloadJitting  1: 1000 op, 1605810.00 ns, 1.6058 us/op

OverheadJitting  2: 16000 op, 200243.00 ns, 12.5152 ns/op
WorkloadJitting  2: 16000 op, 18970484.00 ns, 1.1857 us/op

WorkloadPilot    1: 16000 op, 16962466.00 ns, 1.0602 us/op
WorkloadPilot    2: 32000 op, 31647921.00 ns, 988.9975 ns/op
WorkloadPilot    3: 64000 op, 62333970.00 ns, 973.9683 ns/op
WorkloadPilot    4: 128000 op, 121778358.00 ns, 951.3934 ns/op
WorkloadPilot    5: 256000 op, 46013310.00 ns, 179.7395 ns/op
WorkloadPilot    6: 512000 op, 79475037.00 ns, 155.2247 ns/op
WorkloadPilot    7: 1024000 op, 159213034.00 ns, 155.4815 ns/op
WorkloadPilot    8: 2048000 op, 317187462.00 ns, 154.8767 ns/op
WorkloadPilot    9: 4096000 op, 632283460.00 ns, 154.3661 ns/op

OverheadWarmup   1: 4096000 op, 20349.00 ns, 0.0050 ns/op
OverheadWarmup   2: 4096000 op, 9227.00 ns, 0.0023 ns/op
OverheadWarmup   3: 4096000 op, 9247.00 ns, 0.0023 ns/op
OverheadWarmup   4: 4096000 op, 9227.00 ns, 0.0023 ns/op
OverheadWarmup   5: 4096000 op, 9297.00 ns, 0.0023 ns/op
OverheadWarmup   6: 4096000 op, 9177.00 ns, 0.0022 ns/op

OverheadActual   1: 4096000 op, 9267.00 ns, 0.0023 ns/op
OverheadActual   2: 4096000 op, 9247.00 ns, 0.0023 ns/op
OverheadActual   3: 4096000 op, 9277.00 ns, 0.0023 ns/op
OverheadActual   4: 4096000 op, 9307.00 ns, 0.0023 ns/op
OverheadActual   5: 4096000 op, 9297.00 ns, 0.0023 ns/op
OverheadActual   6: 4096000 op, 9267.00 ns, 0.0023 ns/op
OverheadActual   7: 4096000 op, 9268.00 ns, 0.0023 ns/op
OverheadActual   8: 4096000 op, 9026.00 ns, 0.0022 ns/op
OverheadActual   9: 4096000 op, 9277.00 ns, 0.0023 ns/op
OverheadActual  10: 4096000 op, 9188.00 ns, 0.0022 ns/op
OverheadActual  11: 4096000 op, 10239.00 ns, 0.0025 ns/op
OverheadActual  12: 4096000 op, 9247.00 ns, 0.0023 ns/op
OverheadActual  13: 4096000 op, 9267.00 ns, 0.0023 ns/op
OverheadActual  14: 4096000 op, 9277.00 ns, 0.0023 ns/op
OverheadActual  15: 4096000 op, 9287.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 4096000 op, 661236942.00 ns, 161.4348 ns/op
WorkloadWarmup   2: 4096000 op, 640703090.00 ns, 156.4217 ns/op
WorkloadWarmup   3: 4096000 op, 634418071.00 ns, 154.8872 ns/op
WorkloadWarmup   4: 4096000 op, 635726277.00 ns, 155.2066 ns/op
WorkloadWarmup   5: 4096000 op, 633325126.00 ns, 154.6204 ns/op
WorkloadWarmup   6: 4096000 op, 632191144.00 ns, 154.3435 ns/op
WorkloadWarmup   7: 4096000 op, 632769582.00 ns, 154.4848 ns/op
WorkloadWarmup   8: 4096000 op, 633147264.00 ns, 154.5770 ns/op
WorkloadWarmup   9: 4096000 op, 636125189.00 ns, 155.3040 ns/op
WorkloadWarmup  10: 4096000 op, 632837828.00 ns, 154.5014 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 638356152.00 ns, 155.8487 ns/op
WorkloadActual   2: 4096000 op, 635425477.00 ns, 155.1332 ns/op
WorkloadActual   3: 4096000 op, 639124878.00 ns, 156.0363 ns/op
WorkloadActual   4: 4096000 op, 634186201.00 ns, 154.8306 ns/op
WorkloadActual   5: 4096000 op, 635960754.00 ns, 155.2639 ns/op
WorkloadActual   6: 4096000 op, 635013155.00 ns, 155.0325 ns/op
WorkloadActual   7: 4096000 op, 635905747.00 ns, 155.2504 ns/op
WorkloadActual   8: 4096000 op, 636567429.00 ns, 155.4120 ns/op
WorkloadActual   9: 4096000 op, 635998600.00 ns, 155.2731 ns/op
WorkloadActual  10: 4096000 op, 634302020.00 ns, 154.8589 ns/op
WorkloadActual  11: 4096000 op, 635571845.00 ns, 155.1689 ns/op
WorkloadActual  12: 4096000 op, 635661793.00 ns, 155.1909 ns/op
WorkloadActual  13: 4096000 op, 635168553.00 ns, 155.0704 ns/op
WorkloadActual  14: 4096000 op, 638426601.00 ns, 155.8659 ns/op
WorkloadActual  15: 4096000 op, 633771442.00 ns, 154.7294 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 635416209.00 ns, 155.1309 ns/op
WorkloadResult   2: 4096000 op, 634176933.00 ns, 154.8284 ns/op
WorkloadResult   3: 4096000 op, 635951486.00 ns, 155.2616 ns/op
WorkloadResult   4: 4096000 op, 635003887.00 ns, 155.0302 ns/op
WorkloadResult   5: 4096000 op, 635896479.00 ns, 155.2482 ns/op
WorkloadResult   6: 4096000 op, 636558161.00 ns, 155.4097 ns/op
WorkloadResult   7: 4096000 op, 635989332.00 ns, 155.2708 ns/op
WorkloadResult   8: 4096000 op, 634292752.00 ns, 154.8566 ns/op
WorkloadResult   9: 4096000 op, 635562577.00 ns, 155.1666 ns/op
WorkloadResult  10: 4096000 op, 635652525.00 ns, 155.1886 ns/op
WorkloadResult  11: 4096000 op, 635159285.00 ns, 155.0682 ns/op
WorkloadResult  12: 4096000 op, 633762174.00 ns, 154.7271 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4406 has exited with code 0.

Mean = 155.099 ns, StdErr = 0.059 ns (0.04%), N = 12, StdDev = 0.205 ns
Min = 154.727 ns, Q1 = 154.987 ns, Median = 155.149 ns, Q3 = 155.252 ns, Max = 155.410 ns
IQR = 0.265 ns, LowerFence = 154.590 ns, UpperFence = 155.649 ns
ConfidenceInterval = [154.836 ns; 155.362 ns] (CI 99.9%), Margin = 0.263 ns (0.17% of Mean)
Skewness = -0.4, Kurtosis = 1.84, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-04 19:44 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 173784.00 ns, 173.7840 ns/op
WorkloadJitting  1: 1000 op, 1074751.00 ns, 1.0748 us/op

OverheadJitting  2: 16000 op, 199763.00 ns, 12.4852 ns/op
WorkloadJitting  2: 16000 op, 7544641.00 ns, 471.5401 ns/op

WorkloadPilot    1: 16000 op, 6489145.00 ns, 405.5716 ns/op
WorkloadPilot    2: 32000 op, 12545424.00 ns, 392.0445 ns/op
WorkloadPilot    3: 64000 op, 24831295.00 ns, 387.9890 ns/op
WorkloadPilot    4: 128000 op, 49844709.00 ns, 389.4118 ns/op
WorkloadPilot    5: 256000 op, 66418966.00 ns, 259.4491 ns/op
WorkloadPilot    6: 512000 op, 34883105.00 ns, 68.1311 ns/op
WorkloadPilot    7: 1024000 op, 70462757.00 ns, 68.8113 ns/op
WorkloadPilot    8: 2048000 op, 136779569.00 ns, 66.7869 ns/op
WorkloadPilot    9: 4096000 op, 272927204.00 ns, 66.6326 ns/op
WorkloadPilot   10: 8192000 op, 544440169.00 ns, 66.4600 ns/op

OverheadWarmup   1: 8192000 op, 23343.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 36848.00 ns, 0.0045 ns/op
OverheadWarmup   3: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18755.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18845.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 18855.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 22051.00 ns, 0.0027 ns/op
OverheadActual   5: 8192000 op, 18804.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 40396.00 ns, 0.0049 ns/op
OverheadActual  13: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 18815.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 561538349.00 ns, 68.5472 ns/op
WorkloadWarmup   2: 8192000 op, 552350490.00 ns, 67.4256 ns/op
WorkloadWarmup   3: 8192000 op, 548259211.00 ns, 66.9262 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 547255421.00 ns, 66.8036 ns/op
WorkloadActual   2: 8192000 op, 546247214.00 ns, 66.6806 ns/op
WorkloadActual   3: 8192000 op, 546530291.00 ns, 66.7151 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 547236606.00 ns, 66.8013 ns/op
WorkloadResult   2: 8192000 op, 546228399.00 ns, 66.6783 ns/op
WorkloadResult   3: 8192000 op, 546511476.00 ns, 66.7128 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4419 has exited with code 0.

Mean = 66.731 ns, StdErr = 0.037 ns (0.05%), N = 3, StdDev = 0.063 ns
Min = 66.678 ns, Q1 = 66.696 ns, Median = 66.713 ns, Q3 = 66.757 ns, Max = 66.801 ns
IQR = 0.062 ns, LowerFence = 66.603 ns, UpperFence = 66.849 ns
ConfidenceInterval = [65.573 ns; 67.889 ns] (CI 99.9%), Margin = 1.158 ns (1.74% of Mean)
Skewness = 0.26, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-04 19:44 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 176188.00 ns, 176.1880 ns/op
WorkloadJitting  1: 1000 op, 1364271.00 ns, 1.3643 us/op

OverheadJitting  2: 16000 op, 202507.00 ns, 12.6567 ns/op
WorkloadJitting  2: 16000 op, 13091180.00 ns, 818.1988 ns/op

WorkloadPilot    1: 16000 op, 11693217.00 ns, 730.8261 ns/op
WorkloadPilot    2: 32000 op, 22392051.00 ns, 699.7516 ns/op
WorkloadPilot    3: 64000 op, 40885091.00 ns, 638.8295 ns/op
WorkloadPilot    4: 128000 op, 77779010.00 ns, 607.6485 ns/op
WorkloadPilot    5: 256000 op, 34522142.00 ns, 134.8521 ns/op
WorkloadPilot    6: 512000 op, 59908240.00 ns, 117.0083 ns/op
WorkloadPilot    7: 1024000 op, 119121243.00 ns, 116.3293 ns/op
WorkloadPilot    8: 2048000 op, 237403384.00 ns, 115.9196 ns/op
WorkloadPilot    9: 4096000 op, 474087889.00 ns, 115.7441 ns/op
WorkloadPilot   10: 8192000 op, 946381086.00 ns, 115.5250 ns/op

OverheadWarmup   1: 8192000 op, 23253.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 36639.00 ns, 0.0045 ns/op
OverheadWarmup   3: 8192000 op, 36518.00 ns, 0.0045 ns/op
OverheadWarmup   4: 8192000 op, 53650.00 ns, 0.0065 ns/op
OverheadWarmup   5: 8192000 op, 31438.00 ns, 0.0038 ns/op

OverheadActual   1: 8192000 op, 36377.00 ns, 0.0044 ns/op
OverheadActual   2: 8192000 op, 49342.00 ns, 0.0060 ns/op
OverheadActual   3: 8192000 op, 36448.00 ns, 0.0044 ns/op
OverheadActual   4: 8192000 op, 41548.00 ns, 0.0051 ns/op
OverheadActual   5: 8192000 op, 28553.00 ns, 0.0035 ns/op
OverheadActual   6: 8192000 op, 31478.00 ns, 0.0038 ns/op
OverheadActual   7: 8192000 op, 36468.00 ns, 0.0045 ns/op
OverheadActual   8: 8192000 op, 36608.00 ns, 0.0045 ns/op
OverheadActual   9: 8192000 op, 36237.00 ns, 0.0044 ns/op
OverheadActual  10: 8192000 op, 36157.00 ns, 0.0044 ns/op
OverheadActual  11: 8192000 op, 18706.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 21930.00 ns, 0.0027 ns/op
OverheadActual  13: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  16: 8192000 op, 18725.00 ns, 0.0023 ns/op
OverheadActual  17: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual  18: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  19: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadActual  20: 8192000 op, 21961.00 ns, 0.0027 ns/op

WorkloadWarmup   1: 8192000 op, 961896930.00 ns, 117.4191 ns/op
WorkloadWarmup   2: 8192000 op, 952810434.00 ns, 116.3099 ns/op
WorkloadWarmup   3: 8192000 op, 948775399.00 ns, 115.8173 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 946497200.00 ns, 115.5392 ns/op
WorkloadActual   2: 8192000 op, 951134673.00 ns, 116.1053 ns/op
WorkloadActual   3: 8192000 op, 949167851.00 ns, 115.8652 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 946471943.00 ns, 115.5361 ns/op
WorkloadResult   2: 8192000 op, 951109416.00 ns, 116.1022 ns/op
WorkloadResult   3: 8192000 op, 949142594.00 ns, 115.8621 ns/op
// GC:  129 0 0 2162688000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4428 has exited with code 0.

Mean = 115.833 ns, StdErr = 0.164 ns (0.14%), N = 3, StdDev = 0.284 ns
Min = 115.536 ns, Q1 = 115.699 ns, Median = 115.862 ns, Q3 = 115.982 ns, Max = 116.102 ns
IQR = 0.283 ns, LowerFence = 115.275 ns, UpperFence = 116.407 ns
ConfidenceInterval = [110.650 ns; 121.017 ns] (CI 99.9%), Margin = 5.184 ns (4.48% of Mean)
Skewness = -0.1, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-04 19:44 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 175517.00 ns, 175.5170 ns/op
WorkloadJitting  1: 1000 op, 1217848.00 ns, 1.2178 us/op

OverheadJitting  2: 16000 op, 180276.00 ns, 11.2673 ns/op
WorkloadJitting  2: 16000 op, 9979674.00 ns, 623.7296 ns/op

WorkloadPilot    1: 16000 op, 8680506.00 ns, 542.5316 ns/op
WorkloadPilot    2: 32000 op, 16272013.00 ns, 508.5004 ns/op
WorkloadPilot    3: 64000 op, 32742645.00 ns, 511.6038 ns/op
WorkloadPilot    4: 128000 op, 64049316.00 ns, 500.3853 ns/op
WorkloadPilot    5: 256000 op, 45486207.00 ns, 177.6805 ns/op
WorkloadPilot    6: 512000 op, 43040352.00 ns, 84.0632 ns/op
WorkloadPilot    7: 1024000 op, 83584655.00 ns, 81.6256 ns/op
WorkloadPilot    8: 2048000 op, 166782060.00 ns, 81.4366 ns/op
WorkloadPilot    9: 4096000 op, 332917897.00 ns, 81.2788 ns/op
WorkloadPilot   10: 8192000 op, 669088031.00 ns, 81.6758 ns/op

OverheadWarmup   1: 8192000 op, 23154.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadWarmup   7: 8192000 op, 18795.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 22302.00 ns, 0.0027 ns/op
OverheadActual   3: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18766.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 21941.00 ns, 0.0027 ns/op
OverheadActual  11: 8192000 op, 18806.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 18815.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 680649911.00 ns, 83.0871 ns/op
WorkloadWarmup   2: 8192000 op, 672381930.00 ns, 82.0779 ns/op
WorkloadWarmup   3: 8192000 op, 669394756.00 ns, 81.7132 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 671943764.00 ns, 82.0244 ns/op
WorkloadActual   2: 8192000 op, 665340106.00 ns, 81.2183 ns/op
WorkloadActual   3: 8192000 op, 665835137.00 ns, 81.2787 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 671924949.00 ns, 82.0221 ns/op
WorkloadResult   2: 8192000 op, 665321291.00 ns, 81.2160 ns/op
WorkloadResult   3: 8192000 op, 665816322.00 ns, 81.2764 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4441 has exited with code 0.

Mean = 81.505 ns, StdErr = 0.259 ns (0.32%), N = 3, StdDev = 0.449 ns
Min = 81.216 ns, Q1 = 81.246 ns, Median = 81.276 ns, Q3 = 81.649 ns, Max = 82.022 ns
IQR = 0.403 ns, LowerFence = 80.642 ns, UpperFence = 82.254 ns
ConfidenceInterval = [73.314 ns; 89.696 ns] (CI 99.9%), Margin = 8.191 ns (10.05% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-04 19:43 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 174875.00 ns, 174.8750 ns/op
WorkloadJitting  1: 1000 op, 1732977.00 ns, 1.7330 us/op

OverheadJitting  2: 16000 op, 189252.00 ns, 11.8283 ns/op
WorkloadJitting  2: 16000 op, 19277972.00 ns, 1.2049 us/op

WorkloadPilot    1: 16000 op, 17743044.00 ns, 1.1089 us/op
WorkloadPilot    2: 32000 op, 33208695.00 ns, 1.0378 us/op
WorkloadPilot    3: 64000 op, 65263903.00 ns, 1.0197 us/op
WorkloadPilot    4: 128000 op, 67639899.00 ns, 528.4367 ns/op
WorkloadPilot    5: 256000 op, 43452313.00 ns, 169.7356 ns/op
WorkloadPilot    6: 512000 op, 80698899.00 ns, 157.6150 ns/op
WorkloadPilot    7: 1024000 op, 162131012.00 ns, 158.3311 ns/op
WorkloadPilot    8: 2048000 op, 326539887.00 ns, 159.4433 ns/op
WorkloadPilot    9: 4096000 op, 647054211.00 ns, 157.9722 ns/op

OverheadWarmup   1: 4096000 op, 12814.00 ns, 0.0031 ns/op
OverheadWarmup   2: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 9588.00 ns, 0.0023 ns/op
OverheadWarmup   4: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadWarmup   5: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadWarmup   6: 4096000 op, 9578.00 ns, 0.0023 ns/op
OverheadWarmup   7: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadWarmup   8: 4096000 op, 9628.00 ns, 0.0024 ns/op

OverheadActual   1: 4096000 op, 10440.00 ns, 0.0025 ns/op
OverheadActual   2: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   6: 4096000 op, 9608.00 ns, 0.0023 ns/op
OverheadActual   7: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual   8: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual   9: 4096000 op, 11542.00 ns, 0.0028 ns/op
OverheadActual  10: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual  11: 4096000 op, 9578.00 ns, 0.0023 ns/op
OverheadActual  12: 4096000 op, 9568.00 ns, 0.0023 ns/op
OverheadActual  13: 4096000 op, 9597.00 ns, 0.0023 ns/op
OverheadActual  14: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual  15: 4096000 op, 9628.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 4096000 op, 655410193.00 ns, 160.0123 ns/op
WorkloadWarmup   2: 4096000 op, 653416019.00 ns, 159.5254 ns/op
WorkloadWarmup   3: 4096000 op, 644751482.00 ns, 157.4100 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 650564783.00 ns, 158.8293 ns/op
WorkloadActual   2: 4096000 op, 647737565.00 ns, 158.1391 ns/op
WorkloadActual   3: 4096000 op, 645452840.00 ns, 157.5813 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 650555155.00 ns, 158.8269 ns/op
WorkloadResult   2: 4096000 op, 647727937.00 ns, 158.1367 ns/op
WorkloadResult   3: 4096000 op, 645443212.00 ns, 157.5789 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4451 has exited with code 0.

Mean = 158.181 ns, StdErr = 0.361 ns (0.23%), N = 3, StdDev = 0.625 ns
Min = 157.579 ns, Q1 = 157.858 ns, Median = 158.137 ns, Q3 = 158.482 ns, Max = 158.827 ns
IQR = 0.624 ns, LowerFence = 156.922 ns, UpperFence = 159.418 ns
ConfidenceInterval = [146.775 ns; 169.587 ns] (CI 99.9%), Margin = 11.406 ns (7.21% of Mean)
Skewness = 0.07, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-04 19:43 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 69.119 ns, StdErr = 0.023 ns (0.03%), N = 14, StdDev = 0.086 ns
Min = 68.943 ns, Q1 = 69.062 ns, Median = 69.120 ns, Q3 = 69.188 ns, Max = 69.252 ns
IQR = 0.126 ns, LowerFence = 68.874 ns, UpperFence = 69.377 ns
ConfidenceInterval = [69.022 ns; 69.217 ns] (CI 99.9%), Margin = 0.097 ns (0.14% of Mean)
Skewness = -0.37, Kurtosis = 2.06, MValue = 2
-------------------- Histogram --------------------
[68.896 ns ; 69.299 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 113.393 ns, StdErr = 0.035 ns (0.03%), N = 15, StdDev = 0.134 ns
Min = 113.187 ns, Q1 = 113.316 ns, Median = 113.364 ns, Q3 = 113.506 ns, Max = 113.593 ns
IQR = 0.190 ns, LowerFence = 113.031 ns, UpperFence = 113.792 ns
ConfidenceInterval = [113.250 ns; 113.536 ns] (CI 99.9%), Margin = 0.143 ns (0.13% of Mean)
Skewness = 0.09, Kurtosis = 1.66, MValue = 2
-------------------- Histogram --------------------
[113.115 ns ; 113.664 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 79.692 ns, StdErr = 0.032 ns (0.04%), N = 14, StdDev = 0.120 ns
Min = 79.514 ns, Q1 = 79.612 ns, Median = 79.654 ns, Q3 = 79.739 ns, Max = 79.974 ns
IQR = 0.128 ns, LowerFence = 79.420 ns, UpperFence = 79.931 ns
ConfidenceInterval = [79.557 ns; 79.828 ns] (CI 99.9%), Margin = 0.136 ns (0.17% of Mean)
Skewness = 0.81, Kurtosis = 2.87, MValue = 2
-------------------- Histogram --------------------
[79.449 ns ; 80.040 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 155.099 ns, StdErr = 0.059 ns (0.04%), N = 12, StdDev = 0.205 ns
Min = 154.727 ns, Q1 = 154.987 ns, Median = 155.149 ns, Q3 = 155.252 ns, Max = 155.410 ns
IQR = 0.265 ns, LowerFence = 154.590 ns, UpperFence = 155.649 ns
ConfidenceInterval = [154.836 ns; 155.362 ns] (CI 99.9%), Margin = 0.263 ns (0.17% of Mean)
Skewness = -0.4, Kurtosis = 1.84, MValue = 2
-------------------- Histogram --------------------
[154.609 ns ; 155.528 ns) | @@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 66.731 ns, StdErr = 0.037 ns (0.05%), N = 3, StdDev = 0.063 ns
Min = 66.678 ns, Q1 = 66.696 ns, Median = 66.713 ns, Q3 = 66.757 ns, Max = 66.801 ns
IQR = 0.062 ns, LowerFence = 66.603 ns, UpperFence = 66.849 ns
ConfidenceInterval = [65.573 ns; 67.889 ns] (CI 99.9%), Margin = 1.158 ns (1.74% of Mean)
Skewness = 0.26, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[66.620 ns ; 66.859 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 115.833 ns, StdErr = 0.164 ns (0.14%), N = 3, StdDev = 0.284 ns
Min = 115.536 ns, Q1 = 115.699 ns, Median = 115.862 ns, Q3 = 115.982 ns, Max = 116.102 ns
IQR = 0.283 ns, LowerFence = 115.275 ns, UpperFence = 116.407 ns
ConfidenceInterval = [110.650 ns; 121.017 ns] (CI 99.9%), Margin = 5.184 ns (4.48% of Mean)
Skewness = -0.1, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[115.278 ns ; 116.361 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 81.505 ns, StdErr = 0.259 ns (0.32%), N = 3, StdDev = 0.449 ns
Min = 81.216 ns, Q1 = 81.246 ns, Median = 81.276 ns, Q3 = 81.649 ns, Max = 82.022 ns
IQR = 0.403 ns, LowerFence = 80.642 ns, UpperFence = 82.254 ns
ConfidenceInterval = [73.314 ns; 89.696 ns] (CI 99.9%), Margin = 8.191 ns (10.05% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[81.210 ns ; 82.028 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 158.181 ns, StdErr = 0.361 ns (0.23%), N = 3, StdDev = 0.625 ns
Min = 157.579 ns, Q1 = 157.858 ns, Median = 158.137 ns, Q3 = 158.482 ns, Max = 158.827 ns
IQR = 0.624 ns, LowerFence = 156.922 ns, UpperFence = 159.418 ns
ConfidenceInterval = [146.775 ns; 169.587 ns] (CI 99.9%), Margin = 11.406 ns (7.21% of Mean)
Skewness = 0.07, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[157.010 ns ; 159.396 ns) | @@@
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
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  69.12 ns |  0.097 ns | 0.086 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 113.39 ns |  0.143 ns | 0.134 ns | 0.0157 |     264 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  79.69 ns |  0.136 ns | 0.120 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 155.10 ns |  0.263 ns | 0.205 ns | 0.0115 |     192 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  66.73 ns |  1.158 ns | 0.063 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 115.83 ns |  5.184 ns | 0.284 ns | 0.0157 |     264 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  81.50 ns |  8.191 ns | 0.449 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 158.18 ns | 11.406 ns | 0.625 ns | 0.0115 |     192 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput                        -> 1 outlier  was  removed (69.68 ns)
  CoreDispatchBenchmarks.'Request  Request': RunStrategy=Throughput                     -> 1 outlier  was  removed (80.41 ns)
  CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': RunStrategy=Throughput -> 3 outliers were removed (155.85 ns..156.04 ns)
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
Run time: 00:01:44 (104.01 sec), executed benchmarks: 8

Global total time: 00:01:57 (117.9 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
