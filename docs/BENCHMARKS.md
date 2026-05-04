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

Run: 2026-05-04 17:07 UTC | Branch: copilot/implement-medium-term | Commit: 8dbc9ae

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
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  68.31 ns |  0.308 ns | 0.273 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 124.57 ns |  1.067 ns | 0.998 ns | 0.0157 |     264 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  85.59 ns |  0.487 ns | 0.456 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 162.78 ns |  1.633 ns | 1.528 ns | 0.0115 |     192 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  68.92 ns |  2.955 ns | 0.162 ns | 0.0013 |      24 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 124.48 ns |  6.060 ns | 0.332 ns | 0.0157 |     264 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  90.52 ns |  2.509 ns | 0.138 ns | 0.0057 |      96 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 158.39 ns | 15.019 ns | 0.823 ns | 0.0115 |     192 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.73 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 12.77 sec and exited with 0
// ***** Done, took 00:00:14 (14.56 sec)   *****
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

OverheadJitting  1: 1000 op, 202218.00 ns, 202.2180 ns/op
WorkloadJitting  1: 1000 op, 1107142.00 ns, 1.1071 us/op

OverheadJitting  2: 16000 op, 264004.00 ns, 16.5003 ns/op
WorkloadJitting  2: 16000 op, 11143850.00 ns, 696.4906 ns/op

WorkloadPilot    1: 16000 op, 6678275.00 ns, 417.3922 ns/op
WorkloadPilot    2: 32000 op, 12748813.00 ns, 398.4004 ns/op
WorkloadPilot    3: 64000 op, 25097707.00 ns, 392.1517 ns/op
WorkloadPilot    4: 128000 op, 52819964.00 ns, 412.6560 ns/op
WorkloadPilot    5: 256000 op, 46832492.00 ns, 182.9394 ns/op
WorkloadPilot    6: 512000 op, 36015714.00 ns, 70.3432 ns/op
WorkloadPilot    7: 1024000 op, 72269043.00 ns, 70.5752 ns/op
WorkloadPilot    8: 2048000 op, 140102968.00 ns, 68.4097 ns/op
WorkloadPilot    9: 4096000 op, 280853346.00 ns, 68.5677 ns/op
WorkloadPilot   10: 8192000 op, 559771568.00 ns, 68.3315 ns/op

OverheadWarmup   1: 8192000 op, 34234.00 ns, 0.0042 ns/op
OverheadWarmup   2: 8192000 op, 36489.00 ns, 0.0045 ns/op
OverheadWarmup   3: 8192000 op, 36488.00 ns, 0.0045 ns/op
OverheadWarmup   4: 8192000 op, 36739.00 ns, 0.0045 ns/op
OverheadWarmup   5: 8192000 op, 37030.00 ns, 0.0045 ns/op
OverheadWarmup   6: 8192000 op, 36568.00 ns, 0.0045 ns/op

OverheadActual   1: 8192000 op, 36078.00 ns, 0.0044 ns/op
OverheadActual   2: 8192000 op, 36659.00 ns, 0.0045 ns/op
OverheadActual   3: 8192000 op, 57247.00 ns, 0.0070 ns/op
OverheadActual   4: 8192000 op, 33343.00 ns, 0.0041 ns/op
OverheadActual   5: 8192000 op, 35266.00 ns, 0.0043 ns/op
OverheadActual   6: 8192000 op, 36589.00 ns, 0.0045 ns/op
OverheadActual   7: 8192000 op, 36659.00 ns, 0.0045 ns/op
OverheadActual   8: 8192000 op, 36789.00 ns, 0.0045 ns/op
OverheadActual   9: 8192000 op, 36548.00 ns, 0.0045 ns/op
OverheadActual  10: 8192000 op, 36578.00 ns, 0.0045 ns/op
OverheadActual  11: 8192000 op, 61234.00 ns, 0.0075 ns/op
OverheadActual  12: 8192000 op, 36468.00 ns, 0.0045 ns/op
OverheadActual  13: 8192000 op, 33843.00 ns, 0.0041 ns/op
OverheadActual  14: 8192000 op, 34785.00 ns, 0.0042 ns/op
OverheadActual  15: 8192000 op, 32370.00 ns, 0.0040 ns/op
OverheadActual  16: 8192000 op, 33473.00 ns, 0.0041 ns/op

WorkloadWarmup   1: 8192000 op, 569713293.00 ns, 69.5451 ns/op
WorkloadWarmup   2: 8192000 op, 566790864.00 ns, 69.1883 ns/op
WorkloadWarmup   3: 8192000 op, 563803335.00 ns, 68.8236 ns/op
WorkloadWarmup   4: 8192000 op, 560370832.00 ns, 68.4046 ns/op
WorkloadWarmup   5: 8192000 op, 562259024.00 ns, 68.6351 ns/op
WorkloadWarmup   6: 8192000 op, 563826717.00 ns, 68.8265 ns/op
WorkloadWarmup   7: 8192000 op, 558870003.00 ns, 68.2214 ns/op
WorkloadWarmup   8: 8192000 op, 559618894.00 ns, 68.3129 ns/op
WorkloadWarmup   9: 8192000 op, 559471298.00 ns, 68.2948 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 563766846.00 ns, 68.8192 ns/op
WorkloadActual   2: 8192000 op, 559758345.00 ns, 68.3299 ns/op
WorkloadActual   3: 8192000 op, 558423247.00 ns, 68.1669 ns/op
WorkloadActual   4: 8192000 op, 558777330.00 ns, 68.2101 ns/op
WorkloadActual   5: 8192000 op, 559358677.00 ns, 68.2811 ns/op
WorkloadActual   6: 8192000 op, 560258129.00 ns, 68.3909 ns/op
WorkloadActual   7: 8192000 op, 564142109.00 ns, 68.8650 ns/op
WorkloadActual   8: 8192000 op, 558764596.00 ns, 68.2086 ns/op
WorkloadActual   9: 8192000 op, 557104559.00 ns, 68.0059 ns/op
WorkloadActual  10: 8192000 op, 558488849.00 ns, 68.1749 ns/op
WorkloadActual  11: 8192000 op, 556783419.00 ns, 67.9667 ns/op
WorkloadActual  12: 8192000 op, 557426703.00 ns, 68.0453 ns/op
WorkloadActual  13: 8192000 op, 560901616.00 ns, 68.4694 ns/op
WorkloadActual  14: 8192000 op, 560934086.00 ns, 68.4734 ns/op
WorkloadActual  15: 8192000 op, 573103499.00 ns, 69.9589 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 563730338.00 ns, 68.8147 ns/op
WorkloadResult   2: 8192000 op, 559721837.00 ns, 68.3254 ns/op
WorkloadResult   3: 8192000 op, 558386739.00 ns, 68.1624 ns/op
WorkloadResult   4: 8192000 op, 558740822.00 ns, 68.2057 ns/op
WorkloadResult   5: 8192000 op, 559322169.00 ns, 68.2766 ns/op
WorkloadResult   6: 8192000 op, 560221621.00 ns, 68.3864 ns/op
WorkloadResult   7: 8192000 op, 564105601.00 ns, 68.8605 ns/op
WorkloadResult   8: 8192000 op, 558728088.00 ns, 68.2041 ns/op
WorkloadResult   9: 8192000 op, 557068051.00 ns, 68.0015 ns/op
WorkloadResult  10: 8192000 op, 558452341.00 ns, 68.1705 ns/op
WorkloadResult  11: 8192000 op, 556746911.00 ns, 67.9623 ns/op
WorkloadResult  12: 8192000 op, 557390195.00 ns, 68.0408 ns/op
WorkloadResult  13: 8192000 op, 560865108.00 ns, 68.4650 ns/op
WorkloadResult  14: 8192000 op, 560897578.00 ns, 68.4689 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4437 has exited with code 0.

Mean = 68.310 ns, StdErr = 0.073 ns (0.11%), N = 14, StdDev = 0.273 ns
Min = 67.962 ns, Q1 = 68.164 ns, Median = 68.241 ns, Q3 = 68.445 ns, Max = 68.861 ns
IQR = 0.281 ns, LowerFence = 67.743 ns, UpperFence = 68.867 ns
ConfidenceInterval = [68.003 ns; 68.618 ns] (CI 99.9%), Margin = 0.308 ns (0.45% of Mean)
Skewness = 0.72, Kurtosis = 2.43, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-04 17:07 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 188402.00 ns, 188.4020 ns/op
WorkloadJitting  1: 1000 op, 1444323.00 ns, 1.4443 us/op

OverheadJitting  2: 16000 op, 227355.00 ns, 14.2097 ns/op
WorkloadJitting  2: 16000 op, 14284742.00 ns, 892.7964 ns/op

WorkloadPilot    1: 16000 op, 13066602.00 ns, 816.6626 ns/op
WorkloadPilot    2: 32000 op, 23108323.00 ns, 722.1351 ns/op
WorkloadPilot    3: 64000 op, 42921861.00 ns, 670.6541 ns/op
WorkloadPilot    4: 128000 op, 70476165.00 ns, 550.5950 ns/op
WorkloadPilot    5: 256000 op, 33258798.00 ns, 129.9172 ns/op
WorkloadPilot    6: 512000 op, 61819335.00 ns, 120.7409 ns/op
WorkloadPilot    7: 1024000 op, 123198887.00 ns, 120.3114 ns/op
WorkloadPilot    8: 2048000 op, 247359492.00 ns, 120.7810 ns/op
WorkloadPilot    9: 4096000 op, 499828096.00 ns, 122.0283 ns/op
WorkloadPilot   10: 8192000 op, 984932908.00 ns, 120.2311 ns/op

OverheadWarmup   1: 8192000 op, 41978.00 ns, 0.0051 ns/op
OverheadWarmup   2: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18746.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadWarmup   7: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadWarmup   8: 8192000 op, 18764.00 ns, 0.0023 ns/op
OverheadWarmup   9: 8192000 op, 19387.00 ns, 0.0024 ns/op
OverheadWarmup  10: 8192000 op, 18736.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18906.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 18915.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18896.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18806.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 19407.00 ns, 0.0024 ns/op
OverheadActual   8: 8192000 op, 18746.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18766.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 18816.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18776.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 19667.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 8192000 op, 1002776540.00 ns, 122.4092 ns/op
WorkloadWarmup   2: 8192000 op, 998536017.00 ns, 121.8916 ns/op
WorkloadWarmup   3: 8192000 op, 1000796146.00 ns, 122.1675 ns/op
WorkloadWarmup   4: 8192000 op, 990116365.00 ns, 120.8638 ns/op
WorkloadWarmup   5: 8192000 op, 989873270.00 ns, 120.8341 ns/op
WorkloadWarmup   6: 8192000 op, 992325728.00 ns, 121.1335 ns/op
WorkloadWarmup   7: 8192000 op, 1008687750.00 ns, 123.1308 ns/op
WorkloadWarmup   8: 8192000 op, 1017922117.00 ns, 124.2581 ns/op
WorkloadWarmup   9: 8192000 op, 1007313432.00 ns, 122.9631 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 1018384258.00 ns, 124.3145 ns/op
WorkloadActual   2: 8192000 op, 1021486082.00 ns, 124.6931 ns/op
WorkloadActual   3: 8192000 op, 1025948062.00 ns, 125.2378 ns/op
WorkloadActual   4: 8192000 op, 1026398956.00 ns, 125.2928 ns/op
WorkloadActual   5: 8192000 op, 1023435751.00 ns, 124.9311 ns/op
WorkloadActual   6: 8192000 op, 1009838806.00 ns, 123.2713 ns/op
WorkloadActual   7: 8192000 op, 1016017491.00 ns, 124.0256 ns/op
WorkloadActual   8: 8192000 op, 1012609161.00 ns, 123.6095 ns/op
WorkloadActual   9: 8192000 op, 1015706004.00 ns, 123.9875 ns/op
WorkloadActual  10: 8192000 op, 1009856362.00 ns, 123.2735 ns/op
WorkloadActual  11: 8192000 op, 1010732371.00 ns, 123.3804 ns/op
WorkloadActual  12: 8192000 op, 1026671707.00 ns, 125.3261 ns/op
WorkloadActual  13: 8192000 op, 1037174563.00 ns, 126.6082 ns/op
WorkloadActual  14: 8192000 op, 1022004867.00 ns, 124.7565 ns/op
WorkloadActual  15: 8192000 op, 1031073139.00 ns, 125.8634 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 1018365463.00 ns, 124.3122 ns/op
WorkloadResult   2: 8192000 op, 1021467287.00 ns, 124.6908 ns/op
WorkloadResult   3: 8192000 op, 1025929267.00 ns, 125.2355 ns/op
WorkloadResult   4: 8192000 op, 1026380161.00 ns, 125.2905 ns/op
WorkloadResult   5: 8192000 op, 1023416956.00 ns, 124.9288 ns/op
WorkloadResult   6: 8192000 op, 1009820011.00 ns, 123.2690 ns/op
WorkloadResult   7: 8192000 op, 1015998696.00 ns, 124.0233 ns/op
WorkloadResult   8: 8192000 op, 1012590366.00 ns, 123.6072 ns/op
WorkloadResult   9: 8192000 op, 1015687209.00 ns, 123.9853 ns/op
WorkloadResult  10: 8192000 op, 1009837567.00 ns, 123.2712 ns/op
WorkloadResult  11: 8192000 op, 1010713576.00 ns, 123.3781 ns/op
WorkloadResult  12: 8192000 op, 1026652912.00 ns, 125.3238 ns/op
WorkloadResult  13: 8192000 op, 1037155768.00 ns, 126.6059 ns/op
WorkloadResult  14: 8192000 op, 1021986072.00 ns, 124.7542 ns/op
WorkloadResult  15: 8192000 op, 1031054344.00 ns, 125.8611 ns/op
// GC:  129 0 0 2162688000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4451 has exited with code 0.

Mean = 124.569 ns, StdErr = 0.258 ns (0.21%), N = 15, StdDev = 0.998 ns
Min = 123.269 ns, Q1 = 123.796 ns, Median = 124.691 ns, Q3 = 125.263 ns, Max = 126.606 ns
IQR = 1.467 ns, LowerFence = 121.596 ns, UpperFence = 127.463 ns
ConfidenceInterval = [123.502 ns; 125.636 ns] (CI 99.9%), Margin = 1.067 ns (0.86% of Mean)
Skewness = 0.3, Kurtosis = 1.99, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-04 17:08 (0h 2m from now) **
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

OverheadJitting  1: 1000 op, 178103.00 ns, 178.1030 ns/op
WorkloadJitting  1: 1000 op, 1175520.00 ns, 1.1755 us/op

OverheadJitting  2: 16000 op, 178744.00 ns, 11.1715 ns/op
WorkloadJitting  2: 16000 op, 9927475.00 ns, 620.4672 ns/op

WorkloadPilot    1: 16000 op, 8468174.00 ns, 529.2609 ns/op
WorkloadPilot    2: 32000 op, 22231263.00 ns, 694.7270 ns/op
WorkloadPilot    3: 64000 op, 31819287.00 ns, 497.1764 ns/op
WorkloadPilot    4: 128000 op, 65893752.00 ns, 514.7949 ns/op
WorkloadPilot    5: 256000 op, 43111698.00 ns, 168.4051 ns/op
WorkloadPilot    6: 512000 op, 43546639.00 ns, 85.0520 ns/op
WorkloadPilot    7: 1024000 op, 85071063.00 ns, 83.0772 ns/op
WorkloadPilot    8: 2048000 op, 170599005.00 ns, 83.3003 ns/op
WorkloadPilot    9: 4096000 op, 339950647.00 ns, 82.9958 ns/op
WorkloadPilot   10: 8192000 op, 683372558.00 ns, 83.4195 ns/op

OverheadWarmup   1: 8192000 op, 21089.00 ns, 0.0026 ns/op
OverheadWarmup   2: 8192000 op, 18986.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18875.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18756.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadWarmup   7: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadWarmup   8: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadWarmup   9: 8192000 op, 19266.00 ns, 0.0024 ns/op
OverheadWarmup  10: 8192000 op, 34414.00 ns, 0.0042 ns/op

OverheadActual   1: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 19246.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 18836.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 19326.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 8192000 op, 697842721.00 ns, 85.1859 ns/op
WorkloadWarmup   2: 8192000 op, 688525981.00 ns, 84.0486 ns/op
WorkloadWarmup   3: 8192000 op, 681296183.00 ns, 83.1660 ns/op
WorkloadWarmup   4: 8192000 op, 679281444.00 ns, 82.9201 ns/op
WorkloadWarmup   5: 8192000 op, 683210756.00 ns, 83.3998 ns/op
WorkloadWarmup   6: 8192000 op, 681069278.00 ns, 83.1383 ns/op
WorkloadWarmup   7: 8192000 op, 680496526.00 ns, 83.0684 ns/op
WorkloadWarmup   8: 8192000 op, 686287293.00 ns, 83.7753 ns/op
WorkloadWarmup   9: 8192000 op, 684030749.00 ns, 83.4998 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 690846055.00 ns, 84.3318 ns/op
WorkloadActual   2: 8192000 op, 704681789.00 ns, 86.0207 ns/op
WorkloadActual   3: 8192000 op, 699617131.00 ns, 85.4025 ns/op
WorkloadActual   4: 8192000 op, 698475985.00 ns, 85.2632 ns/op
WorkloadActual   5: 8192000 op, 703356418.00 ns, 85.8589 ns/op
WorkloadActual   6: 8192000 op, 703858228.00 ns, 85.9202 ns/op
WorkloadActual   7: 8192000 op, 706973388.00 ns, 86.3005 ns/op
WorkloadActual   8: 8192000 op, 703885830.00 ns, 85.9236 ns/op
WorkloadActual   9: 8192000 op, 701933031.00 ns, 85.6852 ns/op
WorkloadActual  10: 8192000 op, 700205753.00 ns, 85.4743 ns/op
WorkloadActual  11: 8192000 op, 703173394.00 ns, 85.8366 ns/op
WorkloadActual  12: 8192000 op, 701002813.00 ns, 85.5716 ns/op
WorkloadActual  13: 8192000 op, 700815062.00 ns, 85.5487 ns/op
WorkloadActual  14: 8192000 op, 699895051.00 ns, 85.4364 ns/op
WorkloadActual  15: 8192000 op, 698665481.00 ns, 85.2863 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 690827290.00 ns, 84.3295 ns/op
WorkloadResult   2: 8192000 op, 704663024.00 ns, 86.0184 ns/op
WorkloadResult   3: 8192000 op, 699598366.00 ns, 85.4002 ns/op
WorkloadResult   4: 8192000 op, 698457220.00 ns, 85.2609 ns/op
WorkloadResult   5: 8192000 op, 703337653.00 ns, 85.8566 ns/op
WorkloadResult   6: 8192000 op, 703839463.00 ns, 85.9179 ns/op
WorkloadResult   7: 8192000 op, 706954623.00 ns, 86.2982 ns/op
WorkloadResult   8: 8192000 op, 703867065.00 ns, 85.9213 ns/op
WorkloadResult   9: 8192000 op, 701914266.00 ns, 85.6829 ns/op
WorkloadResult  10: 8192000 op, 700186988.00 ns, 85.4720 ns/op
WorkloadResult  11: 8192000 op, 703154629.00 ns, 85.8343 ns/op
WorkloadResult  12: 8192000 op, 700984048.00 ns, 85.5693 ns/op
WorkloadResult  13: 8192000 op, 700796297.00 ns, 85.5464 ns/op
WorkloadResult  14: 8192000 op, 699876286.00 ns, 85.4341 ns/op
WorkloadResult  15: 8192000 op, 698646716.00 ns, 85.2840 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4466 has exited with code 0.

Mean = 85.588 ns, StdErr = 0.118 ns (0.14%), N = 15, StdDev = 0.456 ns
Min = 84.330 ns, Q1 = 85.417 ns, Median = 85.569 ns, Q3 = 85.887 ns, Max = 86.298 ns
IQR = 0.470 ns, LowerFence = 84.712 ns, UpperFence = 86.592 ns
ConfidenceInterval = [85.101 ns; 86.076 ns] (CI 99.9%), Margin = 0.487 ns (0.57% of Mean)
Skewness = -1.07, Kurtosis = 4.41, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-04 17:08 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 199824.00 ns, 199.8240 ns/op
WorkloadJitting  1: 1000 op, 1875558.00 ns, 1.8756 us/op

OverheadJitting  2: 16000 op, 183103.00 ns, 11.4439 ns/op
WorkloadJitting  2: 16000 op, 19792284.00 ns, 1.2370 us/op

WorkloadPilot    1: 16000 op, 17850793.00 ns, 1.1157 us/op
WorkloadPilot    2: 32000 op, 33658693.00 ns, 1.0518 us/op
WorkloadPilot    3: 64000 op, 66113934.00 ns, 1.0330 us/op
WorkloadPilot    4: 128000 op, 116379863.00 ns, 909.2177 ns/op
WorkloadPilot    5: 256000 op, 50810357.00 ns, 198.4780 ns/op
WorkloadPilot    6: 512000 op, 84650640.00 ns, 165.3333 ns/op
WorkloadPilot    7: 1024000 op, 167005064.00 ns, 163.0909 ns/op
WorkloadPilot    8: 2048000 op, 335677246.00 ns, 163.9049 ns/op
WorkloadPilot    9: 4096000 op, 669567480.00 ns, 163.4686 ns/op

OverheadWarmup   1: 4096000 op, 11872.00 ns, 0.0029 ns/op
OverheadWarmup   2: 4096000 op, 9948.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadWarmup   4: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadWarmup   5: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadWarmup   6: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadWarmup   7: 4096000 op, 9678.00 ns, 0.0024 ns/op
OverheadWarmup   8: 4096000 op, 9627.00 ns, 0.0024 ns/op

OverheadActual   1: 4096000 op, 9668.00 ns, 0.0024 ns/op
OverheadActual   2: 4096000 op, 9668.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9729.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9668.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 9659.00 ns, 0.0024 ns/op
OverheadActual   6: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual   7: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   8: 4096000 op, 9647.00 ns, 0.0024 ns/op
OverheadActual   9: 4096000 op, 11231.00 ns, 0.0027 ns/op
OverheadActual  10: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual  11: 4096000 op, 9708.00 ns, 0.0024 ns/op
OverheadActual  12: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual  13: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual  14: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual  15: 4096000 op, 9639.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 4096000 op, 686621011.00 ns, 167.6321 ns/op
WorkloadWarmup   2: 4096000 op, 684629285.00 ns, 167.1458 ns/op
WorkloadWarmup   3: 4096000 op, 672165692.00 ns, 164.1030 ns/op
WorkloadWarmup   4: 4096000 op, 673331172.00 ns, 164.3875 ns/op
WorkloadWarmup   5: 4096000 op, 671872463.00 ns, 164.0314 ns/op
WorkloadWarmup   6: 4096000 op, 671355125.00 ns, 163.9051 ns/op
WorkloadWarmup   7: 4096000 op, 670747308.00 ns, 163.7567 ns/op
WorkloadWarmup   8: 4096000 op, 674698570.00 ns, 164.7213 ns/op
WorkloadWarmup   9: 4096000 op, 677511664.00 ns, 165.4081 ns/op
WorkloadWarmup  10: 4096000 op, 669931112.00 ns, 163.5574 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 675012518.00 ns, 164.7980 ns/op
WorkloadActual   2: 4096000 op, 671490158.00 ns, 163.9380 ns/op
WorkloadActual   3: 4096000 op, 673995625.00 ns, 164.5497 ns/op
WorkloadActual   4: 4096000 op, 670570507.00 ns, 163.7135 ns/op
WorkloadActual   5: 4096000 op, 661520186.00 ns, 161.5040 ns/op
WorkloadActual   6: 4096000 op, 652756009.00 ns, 159.3643 ns/op
WorkloadActual   7: 4096000 op, 661065225.00 ns, 161.3929 ns/op
WorkloadActual   8: 4096000 op, 667046876.00 ns, 162.8532 ns/op
WorkloadActual   9: 4096000 op, 665046764.00 ns, 162.3649 ns/op
WorkloadActual  10: 4096000 op, 663529876.00 ns, 161.9946 ns/op
WorkloadActual  11: 4096000 op, 660477225.00 ns, 161.2493 ns/op
WorkloadActual  12: 4096000 op, 669145733.00 ns, 163.3657 ns/op
WorkloadActual  13: 4096000 op, 675431102.00 ns, 164.9002 ns/op
WorkloadActual  14: 4096000 op, 667724544.00 ns, 163.0187 ns/op
WorkloadActual  15: 4096000 op, 666648710.00 ns, 162.7560 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 675002860.00 ns, 164.7956 ns/op
WorkloadResult   2: 4096000 op, 671480500.00 ns, 163.9357 ns/op
WorkloadResult   3: 4096000 op, 673985967.00 ns, 164.5474 ns/op
WorkloadResult   4: 4096000 op, 670560849.00 ns, 163.7111 ns/op
WorkloadResult   5: 4096000 op, 661510528.00 ns, 161.5016 ns/op
WorkloadResult   6: 4096000 op, 652746351.00 ns, 159.3619 ns/op
WorkloadResult   7: 4096000 op, 661055567.00 ns, 161.3905 ns/op
WorkloadResult   8: 4096000 op, 667037218.00 ns, 162.8509 ns/op
WorkloadResult   9: 4096000 op, 665037106.00 ns, 162.3626 ns/op
WorkloadResult  10: 4096000 op, 663520218.00 ns, 161.9922 ns/op
WorkloadResult  11: 4096000 op, 660467567.00 ns, 161.2470 ns/op
WorkloadResult  12: 4096000 op, 669136075.00 ns, 163.3633 ns/op
WorkloadResult  13: 4096000 op, 675421444.00 ns, 164.8978 ns/op
WorkloadResult  14: 4096000 op, 667714886.00 ns, 163.0163 ns/op
WorkloadResult  15: 4096000 op, 666639052.00 ns, 162.7537 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4486 has exited with code 0.

Mean = 162.782 ns, StdErr = 0.394 ns (0.24%), N = 15, StdDev = 1.528 ns
Min = 159.362 ns, Q1 = 161.747 ns, Median = 162.851 ns, Q3 = 163.823 ns, Max = 164.898 ns
IQR = 2.076 ns, LowerFence = 158.632 ns, UpperFence = 166.938 ns
ConfidenceInterval = [161.149 ns; 164.415 ns] (CI 99.9%), Margin = 1.633 ns (1.00% of Mean)
Skewness = -0.44, Kurtosis = 2.42, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-04 17:08 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 207980.00 ns, 207.9800 ns/op
WorkloadJitting  1: 1000 op, 1126498.00 ns, 1.1265 us/op

OverheadJitting  2: 16000 op, 211185.00 ns, 13.1991 ns/op
WorkloadJitting  2: 16000 op, 7967978.00 ns, 497.9986 ns/op

WorkloadPilot    1: 16000 op, 6614104.00 ns, 413.3815 ns/op
WorkloadPilot    2: 32000 op, 12571781.00 ns, 392.8682 ns/op
WorkloadPilot    3: 64000 op, 25265890.00 ns, 394.7795 ns/op
WorkloadPilot    4: 128000 op, 50534436.00 ns, 394.8003 ns/op
WorkloadPilot    5: 256000 op, 59842060.00 ns, 233.7580 ns/op
WorkloadPilot    6: 512000 op, 35761838.00 ns, 69.8473 ns/op
WorkloadPilot    7: 1024000 op, 72865245.00 ns, 71.1575 ns/op
WorkloadPilot    8: 2048000 op, 141134341.00 ns, 68.9133 ns/op
WorkloadPilot    9: 4096000 op, 283048051.00 ns, 69.1035 ns/op
WorkloadPilot   10: 8192000 op, 565347630.00 ns, 69.0122 ns/op

OverheadWarmup   1: 8192000 op, 43982.00 ns, 0.0054 ns/op
OverheadWarmup   2: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 19206.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18946.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18795.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 19016.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 19016.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 23695.00 ns, 0.0029 ns/op
OverheadActual   4: 8192000 op, 18905.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18956.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18975.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18956.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 37400.00 ns, 0.0046 ns/op
OverheadActual  12: 8192000 op, 49853.00 ns, 0.0061 ns/op
OverheadActual  13: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 31839.00 ns, 0.0039 ns/op
OverheadActual  15: 8192000 op, 31158.00 ns, 0.0038 ns/op
OverheadActual  16: 8192000 op, 31629.00 ns, 0.0039 ns/op
OverheadActual  17: 8192000 op, 31389.00 ns, 0.0038 ns/op
OverheadActual  18: 8192000 op, 31669.00 ns, 0.0039 ns/op
OverheadActual  19: 8192000 op, 36067.00 ns, 0.0044 ns/op
OverheadActual  20: 8192000 op, 32681.00 ns, 0.0040 ns/op

WorkloadWarmup   1: 8192000 op, 575395629.00 ns, 70.2387 ns/op
WorkloadWarmup   2: 8192000 op, 571411706.00 ns, 69.7524 ns/op
WorkloadWarmup   3: 8192000 op, 567049845.00 ns, 69.2200 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 566021781.00 ns, 69.0945 ns/op
WorkloadActual   2: 8192000 op, 564306623.00 ns, 68.8851 ns/op
WorkloadActual   3: 8192000 op, 563410346.00 ns, 68.7757 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 566000425.50 ns, 69.0918 ns/op
WorkloadResult   2: 8192000 op, 564285267.50 ns, 68.8825 ns/op
WorkloadResult   3: 8192000 op, 563388990.50 ns, 68.7731 ns/op
// GC:  11 0 0 196608000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4500 has exited with code 0.

Mean = 68.916 ns, StdErr = 0.094 ns (0.14%), N = 3, StdDev = 0.162 ns
Min = 68.773 ns, Q1 = 68.828 ns, Median = 68.882 ns, Q3 = 68.987 ns, Max = 69.092 ns
IQR = 0.159 ns, LowerFence = 68.589 ns, UpperFence = 69.226 ns
ConfidenceInterval = [65.961 ns; 71.871 ns] (CI 99.9%), Margin = 2.955 ns (4.29% of Mean)
Skewness = 0.2, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-04 17:07 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 237004.00 ns, 237.0040 ns/op
WorkloadJitting  1: 1000 op, 1399528.00 ns, 1.3995 us/op

OverheadJitting  2: 16000 op, 274294.00 ns, 17.1434 ns/op
WorkloadJitting  2: 16000 op, 14334359.00 ns, 895.8974 ns/op

WorkloadPilot    1: 16000 op, 12630210.00 ns, 789.3881 ns/op
WorkloadPilot    2: 32000 op, 23662932.00 ns, 739.4666 ns/op
WorkloadPilot    3: 64000 op, 43979803.00 ns, 687.1844 ns/op
WorkloadPilot    4: 128000 op, 81023559.00 ns, 632.9966 ns/op
WorkloadPilot    5: 256000 op, 97753239.00 ns, 381.8486 ns/op
WorkloadPilot    6: 512000 op, 63095387.00 ns, 123.2332 ns/op
WorkloadPilot    7: 1024000 op, 124862177.00 ns, 121.9357 ns/op
WorkloadPilot    8: 2048000 op, 248033080.00 ns, 121.1099 ns/op
WorkloadPilot    9: 4096000 op, 497546049.00 ns, 121.4712 ns/op
WorkloadPilot   10: 8192000 op, 999877046.00 ns, 122.0553 ns/op

OverheadWarmup   1: 8192000 op, 44343.00 ns, 0.0054 ns/op
OverheadWarmup   2: 8192000 op, 19206.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18855.00 ns, 0.0023 ns/op
OverheadWarmup   7: 8192000 op, 18785.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 22642.00 ns, 0.0028 ns/op
OverheadActual   3: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18896.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18976.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 22673.00 ns, 0.0028 ns/op
OverheadActual  11: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 44814.00 ns, 0.0055 ns/op
OverheadActual  13: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 18765.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 1006009476.00 ns, 122.8039 ns/op
WorkloadWarmup   2: 8192000 op, 996905122.00 ns, 121.6925 ns/op
WorkloadWarmup   3: 8192000 op, 1009568836.00 ns, 123.2384 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 1016626099.00 ns, 124.0999 ns/op
WorkloadActual   2: 8192000 op, 1021222689.00 ns, 124.6610 ns/op
WorkloadActual   3: 8192000 op, 1021448412.00 ns, 124.6885 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 1016607304.00 ns, 124.0976 ns/op
WorkloadResult   2: 8192000 op, 1021203894.00 ns, 124.6587 ns/op
WorkloadResult   3: 8192000 op, 1021429617.00 ns, 124.6862 ns/op
// GC:  129 0 0 2162688000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4508 has exited with code 0.

Mean = 124.481 ns, StdErr = 0.192 ns (0.15%), N = 3, StdDev = 0.332 ns
Min = 124.098 ns, Q1 = 124.378 ns, Median = 124.659 ns, Q3 = 124.672 ns, Max = 124.686 ns
IQR = 0.294 ns, LowerFence = 123.937 ns, UpperFence = 125.114 ns
ConfidenceInterval = [118.420 ns; 130.541 ns] (CI 99.9%), Margin = 6.060 ns (4.87% of Mean)
Skewness = -0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-04 17:07 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 243595.00 ns, 243.5950 ns/op
WorkloadJitting  1: 1000 op, 1328265.00 ns, 1.3283 us/op

OverheadJitting  2: 16000 op, 247163.00 ns, 15.4477 ns/op
WorkloadJitting  2: 16000 op, 10514793.00 ns, 657.1746 ns/op

WorkloadPilot    1: 16000 op, 9025175.00 ns, 564.0734 ns/op
WorkloadPilot    2: 32000 op, 16860175.00 ns, 526.8805 ns/op
WorkloadPilot    3: 64000 op, 33988761.00 ns, 531.0744 ns/op
WorkloadPilot    4: 128000 op, 75828621.00 ns, 592.4111 ns/op
WorkloadPilot    5: 256000 op, 31470770.00 ns, 122.9327 ns/op
WorkloadPilot    6: 512000 op, 46441429.00 ns, 90.7059 ns/op
WorkloadPilot    7: 1024000 op, 93098722.00 ns, 90.9167 ns/op
WorkloadPilot    8: 2048000 op, 185508212.00 ns, 90.5802 ns/op
WorkloadPilot    9: 4096000 op, 373833663.00 ns, 91.2680 ns/op
WorkloadPilot   10: 8192000 op, 744583283.00 ns, 90.8915 ns/op

OverheadWarmup   1: 8192000 op, 23654.00 ns, 0.0029 ns/op
OverheadWarmup   2: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadWarmup   7: 8192000 op, 18796.00 ns, 0.0023 ns/op
OverheadWarmup   8: 8192000 op, 18855.00 ns, 0.0023 ns/op
OverheadWarmup   9: 8192000 op, 25808.00 ns, 0.0032 ns/op
OverheadWarmup  10: 8192000 op, 18855.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18866.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 18816.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 22171.00 ns, 0.0027 ns/op
OverheadActual   8: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 18895.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 18906.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 22031.00 ns, 0.0027 ns/op

WorkloadWarmup   1: 8192000 op, 753739333.00 ns, 92.0092 ns/op
WorkloadWarmup   2: 8192000 op, 748635877.00 ns, 91.3862 ns/op
WorkloadWarmup   3: 8192000 op, 739543935.00 ns, 90.2764 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 742829723.00 ns, 90.6775 ns/op
WorkloadActual   2: 8192000 op, 740743199.00 ns, 90.4228 ns/op
WorkloadActual   3: 8192000 op, 741049712.00 ns, 90.4602 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 742810907.00 ns, 90.6752 ns/op
WorkloadResult   2: 8192000 op, 740724383.00 ns, 90.4205 ns/op
WorkloadResult   3: 8192000 op, 741030896.00 ns, 90.4579 ns/op
// GC:  47 0 0 786432000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4517 has exited with code 0.

Mean = 90.518 ns, StdErr = 0.079 ns (0.09%), N = 3, StdDev = 0.138 ns
Min = 90.420 ns, Q1 = 90.439 ns, Median = 90.458 ns, Q3 = 90.567 ns, Max = 90.675 ns
IQR = 0.127 ns, LowerFence = 90.248 ns, UpperFence = 90.758 ns
ConfidenceInterval = [88.009 ns; 93.027 ns] (CI 99.9%), Margin = 2.509 ns (2.77% of Mean)
Skewness = 0.35, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-04 17:07 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 209543.00 ns, 209.5430 ns/op
WorkloadJitting  1: 1000 op, 1800369.00 ns, 1.8004 us/op

OverheadJitting  2: 16000 op, 265898.00 ns, 16.6186 ns/op
WorkloadJitting  2: 16000 op, 19867204.00 ns, 1.2417 us/op

WorkloadPilot    1: 16000 op, 17454198.00 ns, 1.0909 us/op
WorkloadPilot    2: 32000 op, 32984347.00 ns, 1.0308 us/op
WorkloadPilot    3: 64000 op, 65659426.00 ns, 1.0259 us/op
WorkloadPilot    4: 128000 op, 120430961.00 ns, 940.8669 ns/op
WorkloadPilot    5: 256000 op, 47094739.00 ns, 183.9638 ns/op
WorkloadPilot    6: 512000 op, 87757711.00 ns, 171.4018 ns/op
WorkloadPilot    7: 1024000 op, 164668452.00 ns, 160.8090 ns/op
WorkloadPilot    8: 2048000 op, 328506721.00 ns, 160.4037 ns/op
WorkloadPilot    9: 4096000 op, 657992684.00 ns, 160.6427 ns/op

OverheadWarmup   1: 4096000 op, 19726.00 ns, 0.0048 ns/op
OverheadWarmup   2: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 9608.00 ns, 0.0023 ns/op
OverheadWarmup   4: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadWarmup   5: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadWarmup   6: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadWarmup   7: 4096000 op, 9618.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9608.00 ns, 0.0023 ns/op
OverheadActual   2: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 9647.00 ns, 0.0024 ns/op
OverheadActual   6: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   7: 4096000 op, 9647.00 ns, 0.0024 ns/op
OverheadActual   8: 4096000 op, 9698.00 ns, 0.0024 ns/op
OverheadActual   9: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual  10: 4096000 op, 12033.00 ns, 0.0029 ns/op
OverheadActual  11: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  12: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual  13: 4096000 op, 9607.00 ns, 0.0023 ns/op
OverheadActual  14: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual  15: 4096000 op, 9648.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 4096000 op, 670176181.00 ns, 163.6172 ns/op
WorkloadWarmup   2: 4096000 op, 669319919.00 ns, 163.4082 ns/op
WorkloadWarmup   3: 4096000 op, 654065684.00 ns, 159.6840 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 652267355.00 ns, 159.2450 ns/op
WorkloadActual   2: 4096000 op, 648566072.00 ns, 158.3413 ns/op
WorkloadActual   3: 4096000 op, 645534459.00 ns, 157.6012 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 652257717.00 ns, 159.2426 ns/op
WorkloadResult   2: 4096000 op, 648556434.00 ns, 158.3390 ns/op
WorkloadResult   3: 4096000 op, 645524821.00 ns, 157.5988 ns/op
// GC:  47 0 0 786432000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4528 has exited with code 0.

Mean = 158.393 ns, StdErr = 0.475 ns (0.30%), N = 3, StdDev = 0.823 ns
Min = 157.599 ns, Q1 = 157.969 ns, Median = 158.339 ns, Q3 = 158.791 ns, Max = 159.243 ns
IQR = 0.822 ns, LowerFence = 156.736 ns, UpperFence = 160.024 ns
ConfidenceInterval = [143.374 ns; 173.412 ns] (CI 99.9%), Margin = 15.019 ns (9.48% of Mean)
Skewness = 0.07, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-04 17:07 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 68.310 ns, StdErr = 0.073 ns (0.11%), N = 14, StdDev = 0.273 ns
Min = 67.962 ns, Q1 = 68.164 ns, Median = 68.241 ns, Q3 = 68.445 ns, Max = 68.861 ns
IQR = 0.281 ns, LowerFence = 67.743 ns, UpperFence = 68.867 ns
ConfidenceInterval = [68.003 ns; 68.618 ns] (CI 99.9%), Margin = 0.308 ns (0.45% of Mean)
Skewness = 0.72, Kurtosis = 2.43, MValue = 2
-------------------- Histogram --------------------
[67.814 ns ; 69.009 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 124.569 ns, StdErr = 0.258 ns (0.21%), N = 15, StdDev = 0.998 ns
Min = 123.269 ns, Q1 = 123.796 ns, Median = 124.691 ns, Q3 = 125.263 ns, Max = 126.606 ns
IQR = 1.467 ns, LowerFence = 121.596 ns, UpperFence = 127.463 ns
ConfidenceInterval = [123.502 ns; 125.636 ns] (CI 99.9%), Margin = 1.067 ns (0.86% of Mean)
Skewness = 0.3, Kurtosis = 1.99, MValue = 2
-------------------- Histogram --------------------
[122.738 ns ; 127.137 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 85.588 ns, StdErr = 0.118 ns (0.14%), N = 15, StdDev = 0.456 ns
Min = 84.330 ns, Q1 = 85.417 ns, Median = 85.569 ns, Q3 = 85.887 ns, Max = 86.298 ns
IQR = 0.470 ns, LowerFence = 84.712 ns, UpperFence = 86.592 ns
ConfidenceInterval = [85.101 ns; 86.076 ns] (CI 99.9%), Margin = 0.487 ns (0.57% of Mean)
Skewness = -1.07, Kurtosis = 4.41, MValue = 2
-------------------- Histogram --------------------
[84.087 ns ; 86.327 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 162.782 ns, StdErr = 0.394 ns (0.24%), N = 15, StdDev = 1.528 ns
Min = 159.362 ns, Q1 = 161.747 ns, Median = 162.851 ns, Q3 = 163.823 ns, Max = 164.898 ns
IQR = 2.076 ns, LowerFence = 158.632 ns, UpperFence = 166.938 ns
ConfidenceInterval = [161.149 ns; 164.415 ns] (CI 99.9%), Margin = 1.633 ns (1.00% of Mean)
Skewness = -0.44, Kurtosis = 2.42, MValue = 2
-------------------- Histogram --------------------
[158.549 ns ; 165.711 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 68.916 ns, StdErr = 0.094 ns (0.14%), N = 3, StdDev = 0.162 ns
Min = 68.773 ns, Q1 = 68.828 ns, Median = 68.882 ns, Q3 = 68.987 ns, Max = 69.092 ns
IQR = 0.159 ns, LowerFence = 68.589 ns, UpperFence = 69.226 ns
ConfidenceInterval = [65.961 ns; 71.871 ns] (CI 99.9%), Margin = 2.955 ns (4.29% of Mean)
Skewness = 0.2, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[68.626 ns ; 69.239 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 124.481 ns, StdErr = 0.192 ns (0.15%), N = 3, StdDev = 0.332 ns
Min = 124.098 ns, Q1 = 124.378 ns, Median = 124.659 ns, Q3 = 124.672 ns, Max = 124.686 ns
IQR = 0.294 ns, LowerFence = 123.937 ns, UpperFence = 125.114 ns
ConfidenceInterval = [118.420 ns; 130.541 ns] (CI 99.9%), Margin = 6.060 ns (4.87% of Mean)
Skewness = -0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[124.090 ns ; 124.694 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 90.518 ns, StdErr = 0.079 ns (0.09%), N = 3, StdDev = 0.138 ns
Min = 90.420 ns, Q1 = 90.439 ns, Median = 90.458 ns, Q3 = 90.567 ns, Max = 90.675 ns
IQR = 0.127 ns, LowerFence = 90.248 ns, UpperFence = 90.758 ns
ConfidenceInterval = [88.009 ns; 93.027 ns] (CI 99.9%), Margin = 2.509 ns (2.77% of Mean)
Skewness = 0.35, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[90.295 ns ; 90.800 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 158.393 ns, StdErr = 0.475 ns (0.30%), N = 3, StdDev = 0.823 ns
Min = 157.599 ns, Q1 = 157.969 ns, Median = 158.339 ns, Q3 = 158.791 ns, Max = 159.243 ns
IQR = 0.822 ns, LowerFence = 156.736 ns, UpperFence = 160.024 ns
ConfidenceInterval = [143.374 ns; 173.412 ns] (CI 99.9%), Margin = 15.019 ns (9.48% of Mean)
Skewness = 0.07, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[156.850 ns ; 159.992 ns) | @@@
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
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  68.31 ns |  0.308 ns | 0.273 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 124.57 ns |  1.067 ns | 0.998 ns | 0.0157 |     264 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  85.59 ns |  0.487 ns | 0.456 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 162.78 ns |  1.633 ns | 1.528 ns | 0.0115 |     192 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  68.92 ns |  2.955 ns | 0.162 ns | 0.0013 |      24 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 124.48 ns |  6.060 ns | 0.332 ns | 0.0157 |     264 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  90.52 ns |  2.509 ns | 0.138 ns | 0.0057 |      96 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 158.39 ns | 15.019 ns | 0.823 ns | 0.0115 |     192 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput    -> 1 outlier  was  removed (69.96 ns)
  CoreDispatchBenchmarks.'Request  Request': RunStrategy=Throughput -> 1 outlier  was  detected (84.33 ns)
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
Run time: 00:01:50 (110.4 sec), executed benchmarks: 8

Global total time: 00:02:05 (125.08 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
