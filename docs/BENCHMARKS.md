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

Run: 2026-05-05 11:11 UTC | Branch: copilot/implementar-long-term | Commit: 05a1ca7

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
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  68.77 ns |  0.435 ns | 0.407 ns | 0.0018 |      32 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 115.55 ns |  0.577 ns | 0.539 ns | 0.0162 |     272 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  95.15 ns |  0.474 ns | 0.420 ns | 0.0061 |     104 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 163.82 ns |  0.504 ns | 0.393 ns | 0.0117 |     200 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  67.43 ns |  4.385 ns | 0.240 ns | 0.0018 |      32 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 117.21 ns | 10.960 ns | 0.601 ns | 0.0162 |     272 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  88.20 ns | 34.066 ns | 1.867 ns | 0.0061 |     104 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 168.27 ns | 55.355 ns | 3.034 ns | 0.0117 |     200 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.65 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 12.27 sec and exited with 0
// ***** Done, took 00:00:13 (13.99 sec)   *****
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

OverheadJitting  1: 1000 op, 191006.00 ns, 191.0060 ns/op
WorkloadJitting  1: 1000 op, 1090272.00 ns, 1.0903 us/op

OverheadJitting  2: 16000 op, 188181.00 ns, 11.7613 ns/op
WorkloadJitting  2: 16000 op, 7995458.00 ns, 499.7161 ns/op

WorkloadPilot    1: 16000 op, 6831108.00 ns, 426.9443 ns/op
WorkloadPilot    2: 32000 op, 13058782.00 ns, 408.0869 ns/op
WorkloadPilot    3: 64000 op, 26126481.00 ns, 408.2263 ns/op
WorkloadPilot    4: 128000 op, 54033689.00 ns, 422.1382 ns/op
WorkloadPilot    5: 256000 op, 58937606.00 ns, 230.2250 ns/op
WorkloadPilot    6: 512000 op, 36410922.00 ns, 71.1151 ns/op
WorkloadPilot    7: 1024000 op, 70631274.00 ns, 68.9759 ns/op
WorkloadPilot    8: 2048000 op, 139606111.00 ns, 68.1670 ns/op
WorkloadPilot    9: 4096000 op, 281636459.00 ns, 68.7589 ns/op
WorkloadPilot   10: 8192000 op, 560530099.00 ns, 68.4241 ns/op

OverheadWarmup   1: 8192000 op, 20859.00 ns, 0.0025 ns/op
OverheadWarmup   2: 8192000 op, 16631.00 ns, 0.0020 ns/op
OverheadWarmup   3: 8192000 op, 16370.00 ns, 0.0020 ns/op
OverheadWarmup   4: 8192000 op, 16490.00 ns, 0.0020 ns/op
OverheadWarmup   5: 8192000 op, 16521.00 ns, 0.0020 ns/op
OverheadWarmup   6: 8192000 op, 29625.00 ns, 0.0036 ns/op
OverheadWarmup   7: 8192000 op, 29826.00 ns, 0.0036 ns/op
OverheadWarmup   8: 8192000 op, 29455.00 ns, 0.0036 ns/op
OverheadWarmup   9: 8192000 op, 36809.00 ns, 0.0045 ns/op
OverheadWarmup  10: 8192000 op, 32190.00 ns, 0.0039 ns/op

OverheadActual   1: 8192000 op, 29906.00 ns, 0.0037 ns/op
OverheadActual   2: 8192000 op, 29124.00 ns, 0.0036 ns/op
OverheadActual   3: 8192000 op, 29595.00 ns, 0.0036 ns/op
OverheadActual   4: 8192000 op, 30036.00 ns, 0.0037 ns/op
OverheadActual   5: 8192000 op, 29936.00 ns, 0.0037 ns/op
OverheadActual   6: 8192000 op, 29836.00 ns, 0.0036 ns/op
OverheadActual   7: 8192000 op, 19617.00 ns, 0.0024 ns/op
OverheadActual   8: 8192000 op, 28523.00 ns, 0.0035 ns/op
OverheadActual   9: 8192000 op, 29946.00 ns, 0.0037 ns/op
OverheadActual  10: 8192000 op, 29525.00 ns, 0.0036 ns/op
OverheadActual  11: 8192000 op, 29956.00 ns, 0.0037 ns/op
OverheadActual  12: 8192000 op, 29805.00 ns, 0.0036 ns/op
OverheadActual  13: 8192000 op, 29826.00 ns, 0.0036 ns/op
OverheadActual  14: 8192000 op, 28743.00 ns, 0.0035 ns/op
OverheadActual  15: 8192000 op, 32300.00 ns, 0.0039 ns/op
OverheadActual  16: 8192000 op, 28243.00 ns, 0.0034 ns/op
OverheadActual  17: 8192000 op, 16501.00 ns, 0.0020 ns/op
OverheadActual  18: 8192000 op, 28373.00 ns, 0.0035 ns/op
OverheadActual  19: 8192000 op, 16410.00 ns, 0.0020 ns/op
OverheadActual  20: 8192000 op, 29135.00 ns, 0.0036 ns/op

WorkloadWarmup   1: 8192000 op, 574945054.00 ns, 70.1837 ns/op
WorkloadWarmup   2: 8192000 op, 566020666.00 ns, 69.0943 ns/op
WorkloadWarmup   3: 8192000 op, 563785812.00 ns, 68.8215 ns/op
WorkloadWarmup   4: 8192000 op, 561314818.00 ns, 68.5199 ns/op
WorkloadWarmup   5: 8192000 op, 563771656.00 ns, 68.8198 ns/op
WorkloadWarmup   6: 8192000 op, 559674702.00 ns, 68.3197 ns/op
WorkloadWarmup   7: 8192000 op, 561782740.00 ns, 68.5770 ns/op
WorkloadWarmup   8: 8192000 op, 565391203.00 ns, 69.0175 ns/op
WorkloadWarmup   9: 8192000 op, 560883564.00 ns, 68.4672 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 561656615.00 ns, 68.5616 ns/op
WorkloadActual   2: 8192000 op, 561074189.00 ns, 68.4905 ns/op
WorkloadActual   3: 8192000 op, 562789566.00 ns, 68.6999 ns/op
WorkloadActual   4: 8192000 op, 563308452.00 ns, 68.7632 ns/op
WorkloadActual   5: 8192000 op, 559608529.00 ns, 68.3116 ns/op
WorkloadActual   6: 8192000 op, 558814324.00 ns, 68.2146 ns/op
WorkloadActual   7: 8192000 op, 568788378.00 ns, 69.4322 ns/op
WorkloadActual   8: 8192000 op, 561514225.00 ns, 68.5442 ns/op
WorkloadActual   9: 8192000 op, 558625121.00 ns, 68.1915 ns/op
WorkloadActual  10: 8192000 op, 561492734.00 ns, 68.5416 ns/op
WorkloadActual  11: 8192000 op, 566631018.00 ns, 69.1688 ns/op
WorkloadActual  12: 8192000 op, 566858492.00 ns, 69.1966 ns/op
WorkloadActual  13: 8192000 op, 567677519.00 ns, 69.2966 ns/op
WorkloadActual  14: 8192000 op, 566302456.00 ns, 69.1287 ns/op
WorkloadActual  15: 8192000 op, 565272317.00 ns, 69.0030 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 561627055.00 ns, 68.5580 ns/op
WorkloadResult   2: 8192000 op, 561044629.00 ns, 68.4869 ns/op
WorkloadResult   3: 8192000 op, 562760006.00 ns, 68.6963 ns/op
WorkloadResult   4: 8192000 op, 563278892.00 ns, 68.7596 ns/op
WorkloadResult   5: 8192000 op, 559578969.00 ns, 68.3080 ns/op
WorkloadResult   6: 8192000 op, 558784764.00 ns, 68.2110 ns/op
WorkloadResult   7: 8192000 op, 568758818.00 ns, 69.4286 ns/op
WorkloadResult   8: 8192000 op, 561484665.00 ns, 68.5406 ns/op
WorkloadResult   9: 8192000 op, 558595561.00 ns, 68.1879 ns/op
WorkloadResult  10: 8192000 op, 561463174.00 ns, 68.5380 ns/op
WorkloadResult  11: 8192000 op, 566601458.00 ns, 69.1652 ns/op
WorkloadResult  12: 8192000 op, 566828932.00 ns, 69.1930 ns/op
WorkloadResult  13: 8192000 op, 567647959.00 ns, 69.2930 ns/op
WorkloadResult  14: 8192000 op, 566272896.00 ns, 69.1251 ns/op
WorkloadResult  15: 8192000 op, 565242757.00 ns, 68.9994 ns/op
// GC:  15 0 0 262144000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4717 has exited with code 0.

Mean = 68.766 ns, StdErr = 0.105 ns (0.15%), N = 15, StdDev = 0.407 ns
Min = 68.188 ns, Q1 = 68.512 ns, Median = 68.696 ns, Q3 = 69.145 ns, Max = 69.429 ns
IQR = 0.633 ns, LowerFence = 67.563 ns, UpperFence = 70.094 ns
ConfidenceInterval = [68.331 ns; 69.201 ns] (CI 99.9%), Margin = 0.435 ns (0.63% of Mean)
Skewness = 0.12, Kurtosis = 1.48, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-05 11:11 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 193220.00 ns, 193.2200 ns/op
WorkloadJitting  1: 1000 op, 1372477.00 ns, 1.3725 us/op

OverheadJitting  2: 16000 op, 217075.00 ns, 13.5672 ns/op
WorkloadJitting  2: 16000 op, 18852704.00 ns, 1.1783 us/op

WorkloadPilot    1: 16000 op, 11444684.00 ns, 715.2928 ns/op
WorkloadPilot    2: 32000 op, 21739566.00 ns, 679.3614 ns/op
WorkloadPilot    3: 64000 op, 39004087.00 ns, 609.4389 ns/op
WorkloadPilot    4: 128000 op, 72469629.00 ns, 566.1690 ns/op
WorkloadPilot    5: 256000 op, 32168077.00 ns, 125.6566 ns/op
WorkloadPilot    6: 512000 op, 59560346.00 ns, 116.3288 ns/op
WorkloadPilot    7: 1024000 op, 120008356.00 ns, 117.1957 ns/op
WorkloadPilot    8: 2048000 op, 250972295.00 ns, 122.5451 ns/op
WorkloadPilot    9: 4096000 op, 488800577.00 ns, 119.3361 ns/op
WorkloadPilot   10: 8192000 op, 955851262.00 ns, 116.6811 ns/op

OverheadWarmup   1: 8192000 op, 20879.00 ns, 0.0025 ns/op
OverheadWarmup   2: 8192000 op, 18133.00 ns, 0.0022 ns/op
OverheadWarmup   3: 8192000 op, 18104.00 ns, 0.0022 ns/op
OverheadWarmup   4: 8192000 op, 18104.00 ns, 0.0022 ns/op
OverheadWarmup   5: 8192000 op, 18094.00 ns, 0.0022 ns/op
OverheadWarmup   6: 8192000 op, 18073.00 ns, 0.0022 ns/op
OverheadWarmup   7: 8192000 op, 18084.00 ns, 0.0022 ns/op
OverheadWarmup   8: 8192000 op, 18094.00 ns, 0.0022 ns/op
OverheadWarmup   9: 8192000 op, 19246.00 ns, 0.0023 ns/op
OverheadWarmup  10: 8192000 op, 35646.00 ns, 0.0044 ns/op

OverheadActual   1: 8192000 op, 18144.00 ns, 0.0022 ns/op
OverheadActual   2: 8192000 op, 18154.00 ns, 0.0022 ns/op
OverheadActual   3: 8192000 op, 18124.00 ns, 0.0022 ns/op
OverheadActual   4: 8192000 op, 35115.00 ns, 0.0043 ns/op
OverheadActual   5: 8192000 op, 18154.00 ns, 0.0022 ns/op
OverheadActual   6: 8192000 op, 17984.00 ns, 0.0022 ns/op
OverheadActual   7: 8192000 op, 19206.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18093.00 ns, 0.0022 ns/op
OverheadActual   9: 8192000 op, 18104.00 ns, 0.0022 ns/op
OverheadActual  10: 8192000 op, 18114.00 ns, 0.0022 ns/op
OverheadActual  11: 8192000 op, 18144.00 ns, 0.0022 ns/op
OverheadActual  12: 8192000 op, 18083.00 ns, 0.0022 ns/op
OverheadActual  13: 8192000 op, 18064.00 ns, 0.0022 ns/op
OverheadActual  14: 8192000 op, 18094.00 ns, 0.0022 ns/op
OverheadActual  15: 8192000 op, 19376.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 8192000 op, 964526135.00 ns, 117.7400 ns/op
WorkloadWarmup   2: 8192000 op, 957564758.00 ns, 116.8902 ns/op
WorkloadWarmup   3: 8192000 op, 951929206.00 ns, 116.2023 ns/op
WorkloadWarmup   4: 8192000 op, 955678091.00 ns, 116.6599 ns/op
WorkloadWarmup   5: 8192000 op, 940916516.00 ns, 114.8580 ns/op
WorkloadWarmup   6: 8192000 op, 949059659.00 ns, 115.8520 ns/op
WorkloadWarmup   7: 8192000 op, 940180054.00 ns, 114.7681 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 949305881.00 ns, 115.8821 ns/op
WorkloadActual   2: 8192000 op, 942794890.00 ns, 115.0873 ns/op
WorkloadActual   3: 8192000 op, 956216843.00 ns, 116.7257 ns/op
WorkloadActual   4: 8192000 op, 946207673.00 ns, 115.5039 ns/op
WorkloadActual   5: 8192000 op, 950001846.00 ns, 115.9670 ns/op
WorkloadActual   6: 8192000 op, 946990448.00 ns, 115.5994 ns/op
WorkloadActual   7: 8192000 op, 942144206.00 ns, 115.0078 ns/op
WorkloadActual   8: 8192000 op, 943781346.00 ns, 115.2077 ns/op
WorkloadActual   9: 8192000 op, 943619061.00 ns, 115.1879 ns/op
WorkloadActual  10: 8192000 op, 947139774.00 ns, 115.6176 ns/op
WorkloadActual  11: 8192000 op, 943209889.00 ns, 115.1379 ns/op
WorkloadActual  12: 8192000 op, 948565177.00 ns, 115.7916 ns/op
WorkloadActual  13: 8192000 op, 954277430.00 ns, 116.4889 ns/op
WorkloadActual  14: 8192000 op, 941260027.00 ns, 114.8999 ns/op
WorkloadActual  15: 8192000 op, 943874337.00 ns, 115.2190 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 949287757.00 ns, 115.8799 ns/op
WorkloadResult   2: 8192000 op, 942776766.00 ns, 115.0851 ns/op
WorkloadResult   3: 8192000 op, 956198719.00 ns, 116.7235 ns/op
WorkloadResult   4: 8192000 op, 946189549.00 ns, 115.5017 ns/op
WorkloadResult   5: 8192000 op, 949983722.00 ns, 115.9648 ns/op
WorkloadResult   6: 8192000 op, 946972324.00 ns, 115.5972 ns/op
WorkloadResult   7: 8192000 op, 942126082.00 ns, 115.0056 ns/op
WorkloadResult   8: 8192000 op, 943763222.00 ns, 115.2055 ns/op
WorkloadResult   9: 8192000 op, 943600937.00 ns, 115.1857 ns/op
WorkloadResult  10: 8192000 op, 947121650.00 ns, 115.6154 ns/op
WorkloadResult  11: 8192000 op, 943191765.00 ns, 115.1357 ns/op
WorkloadResult  12: 8192000 op, 948547053.00 ns, 115.7894 ns/op
WorkloadResult  13: 8192000 op, 954259306.00 ns, 116.4867 ns/op
WorkloadResult  14: 8192000 op, 941241903.00 ns, 114.8977 ns/op
WorkloadResult  15: 8192000 op, 943856213.00 ns, 115.2168 ns/op
// GC:  133 0 0 2228224000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4730 has exited with code 0.

Mean = 115.553 ns, StdErr = 0.139 ns (0.12%), N = 15, StdDev = 0.539 ns
Min = 114.898 ns, Q1 = 115.161 ns, Median = 115.502 ns, Q3 = 115.835 ns, Max = 116.723 ns
IQR = 0.674 ns, LowerFence = 114.150 ns, UpperFence = 116.846 ns
ConfidenceInterval = [114.976 ns; 116.129 ns] (CI 99.9%), Margin = 0.577 ns (0.50% of Mean)
Skewness = 0.76, Kurtosis = 2.43, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-05 11:12 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 189122.00 ns, 189.1220 ns/op
WorkloadJitting  1: 1000 op, 1205947.00 ns, 1.2059 us/op

OverheadJitting  2: 16000 op, 198911.00 ns, 12.4319 ns/op
WorkloadJitting  2: 16000 op, 10303477.00 ns, 643.9673 ns/op

WorkloadPilot    1: 16000 op, 8992154.00 ns, 562.0096 ns/op
WorkloadPilot    2: 32000 op, 16702829.00 ns, 521.9634 ns/op
WorkloadPilot    3: 64000 op, 33474357.00 ns, 523.0368 ns/op
WorkloadPilot    4: 128000 op, 67764234.00 ns, 529.4081 ns/op
WorkloadPilot    5: 256000 op, 41761788.00 ns, 163.1320 ns/op
WorkloadPilot    6: 512000 op, 49399639.00 ns, 96.4837 ns/op
WorkloadPilot    7: 1024000 op, 97066418.00 ns, 94.7914 ns/op
WorkloadPilot    8: 2048000 op, 196340951.00 ns, 95.8696 ns/op
WorkloadPilot    9: 4096000 op, 387180981.00 ns, 94.5266 ns/op
WorkloadPilot   10: 8192000 op, 775758223.00 ns, 94.6970 ns/op

OverheadWarmup   1: 8192000 op, 20668.00 ns, 0.0025 ns/op
OverheadWarmup   2: 8192000 op, 18184.00 ns, 0.0022 ns/op
OverheadWarmup   3: 8192000 op, 18154.00 ns, 0.0022 ns/op
OverheadWarmup   4: 8192000 op, 18004.00 ns, 0.0022 ns/op
OverheadWarmup   5: 8192000 op, 18134.00 ns, 0.0022 ns/op
OverheadWarmup   6: 8192000 op, 18104.00 ns, 0.0022 ns/op
OverheadWarmup   7: 8192000 op, 18064.00 ns, 0.0022 ns/op
OverheadWarmup   8: 8192000 op, 18134.00 ns, 0.0022 ns/op
OverheadWarmup   9: 8192000 op, 19386.00 ns, 0.0024 ns/op
OverheadWarmup  10: 8192000 op, 18013.00 ns, 0.0022 ns/op

OverheadActual   1: 8192000 op, 18214.00 ns, 0.0022 ns/op
OverheadActual   2: 8192000 op, 37931.00 ns, 0.0046 ns/op
OverheadActual   3: 8192000 op, 18164.00 ns, 0.0022 ns/op
OverheadActual   4: 8192000 op, 18124.00 ns, 0.0022 ns/op
OverheadActual   5: 8192000 op, 18094.00 ns, 0.0022 ns/op
OverheadActual   6: 8192000 op, 18133.00 ns, 0.0022 ns/op
OverheadActual   7: 8192000 op, 19346.00 ns, 0.0024 ns/op
OverheadActual   8: 8192000 op, 18103.00 ns, 0.0022 ns/op
OverheadActual   9: 8192000 op, 18143.00 ns, 0.0022 ns/op
OverheadActual  10: 8192000 op, 18113.00 ns, 0.0022 ns/op
OverheadActual  11: 8192000 op, 18244.00 ns, 0.0022 ns/op
OverheadActual  12: 8192000 op, 17993.00 ns, 0.0022 ns/op
OverheadActual  13: 8192000 op, 18123.00 ns, 0.0022 ns/op
OverheadActual  14: 8192000 op, 18164.00 ns, 0.0022 ns/op
OverheadActual  15: 8192000 op, 19336.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 8192000 op, 786579050.00 ns, 96.0180 ns/op
WorkloadWarmup   2: 8192000 op, 781780817.00 ns, 95.4322 ns/op
WorkloadWarmup   3: 8192000 op, 779382957.00 ns, 95.1395 ns/op
WorkloadWarmup   4: 8192000 op, 777726287.00 ns, 94.9373 ns/op
WorkloadWarmup   5: 8192000 op, 776630465.00 ns, 94.8035 ns/op
WorkloadWarmup   6: 8192000 op, 775106135.00 ns, 94.6174 ns/op
WorkloadWarmup   7: 8192000 op, 773761490.00 ns, 94.4533 ns/op
WorkloadWarmup   8: 8192000 op, 774804162.00 ns, 94.5806 ns/op
WorkloadWarmup   9: 8192000 op, 774594111.00 ns, 94.5549 ns/op
WorkloadWarmup  10: 8192000 op, 774145743.00 ns, 94.5002 ns/op
WorkloadWarmup  11: 8192000 op, 775753839.00 ns, 94.6965 ns/op
WorkloadWarmup  12: 8192000 op, 774104682.00 ns, 94.4952 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 785168949.00 ns, 95.8458 ns/op
WorkloadActual   2: 8192000 op, 773485497.00 ns, 94.4196 ns/op
WorkloadActual   3: 8192000 op, 777229833.00 ns, 94.8767 ns/op
WorkloadActual   4: 8192000 op, 783130745.00 ns, 95.5970 ns/op
WorkloadActual   5: 8192000 op, 778882685.00 ns, 95.0785 ns/op
WorkloadActual   6: 8192000 op, 780443462.00 ns, 95.2690 ns/op
WorkloadActual   7: 8192000 op, 780431199.00 ns, 95.2675 ns/op
WorkloadActual   8: 8192000 op, 784277195.00 ns, 95.7370 ns/op
WorkloadActual   9: 8192000 op, 782057751.00 ns, 95.4660 ns/op
WorkloadActual  10: 8192000 op, 793146183.00 ns, 96.8196 ns/op
WorkloadActual  11: 8192000 op, 775205493.00 ns, 94.6296 ns/op
WorkloadActual  12: 8192000 op, 779084682.00 ns, 95.1031 ns/op
WorkloadActual  13: 8192000 op, 775460659.00 ns, 94.6607 ns/op
WorkloadActual  14: 8192000 op, 779442228.00 ns, 95.1468 ns/op
WorkloadActual  15: 8192000 op, 778708280.00 ns, 95.0572 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 785150806.00 ns, 95.8436 ns/op
WorkloadResult   2: 8192000 op, 773467354.00 ns, 94.4174 ns/op
WorkloadResult   3: 8192000 op, 777211690.00 ns, 94.8745 ns/op
WorkloadResult   4: 8192000 op, 783112602.00 ns, 95.5948 ns/op
WorkloadResult   5: 8192000 op, 778864542.00 ns, 95.0762 ns/op
WorkloadResult   6: 8192000 op, 780425319.00 ns, 95.2668 ns/op
WorkloadResult   7: 8192000 op, 780413056.00 ns, 95.2653 ns/op
WorkloadResult   8: 8192000 op, 784259052.00 ns, 95.7347 ns/op
WorkloadResult   9: 8192000 op, 782039608.00 ns, 95.4638 ns/op
WorkloadResult  10: 8192000 op, 775187350.00 ns, 94.6274 ns/op
WorkloadResult  11: 8192000 op, 779066539.00 ns, 95.1009 ns/op
WorkloadResult  12: 8192000 op, 775442516.00 ns, 94.6585 ns/op
WorkloadResult  13: 8192000 op, 779424085.00 ns, 95.1445 ns/op
WorkloadResult  14: 8192000 op, 778690137.00 ns, 95.0549 ns/op
// GC:  50 0 0 851968032 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4746 has exited with code 0.

Mean = 95.152 ns, StdErr = 0.112 ns (0.12%), N = 14, StdDev = 0.420 ns
Min = 94.417 ns, Q1 = 94.920 ns, Median = 95.123 ns, Q3 = 95.415 ns, Max = 95.844 ns
IQR = 0.495 ns, LowerFence = 94.177 ns, UpperFence = 96.157 ns
ConfidenceInterval = [94.678 ns; 95.625 ns] (CI 99.9%), Margin = 0.474 ns (0.50% of Mean)
Skewness = -0.03, Kurtosis = 1.9, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-05 11:12 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 220221.00 ns, 220.2210 ns/op
WorkloadJitting  1: 1000 op, 1697994.00 ns, 1.6980 us/op

OverheadJitting  2: 16000 op, 193010.00 ns, 12.0631 ns/op
WorkloadJitting  2: 16000 op, 19245791.00 ns, 1.2029 us/op

WorkloadPilot    1: 16000 op, 18031198.00 ns, 1.1269 us/op
WorkloadPilot    2: 32000 op, 32232269.00 ns, 1.0073 us/op
WorkloadPilot    3: 64000 op, 64381413.00 ns, 1.0060 us/op
WorkloadPilot    4: 128000 op, 123974130.00 ns, 968.5479 ns/op
WorkloadPilot    5: 256000 op, 56529873.00 ns, 220.8198 ns/op
WorkloadPilot    6: 512000 op, 83959393.00 ns, 163.9832 ns/op
WorkloadPilot    7: 1024000 op, 166785485.00 ns, 162.8765 ns/op
WorkloadPilot    8: 2048000 op, 334027820.00 ns, 163.0995 ns/op
WorkloadPilot    9: 4096000 op, 676317664.00 ns, 165.1166 ns/op

OverheadWarmup   1: 4096000 op, 11862.00 ns, 0.0029 ns/op
OverheadWarmup   2: 4096000 op, 9297.00 ns, 0.0023 ns/op
OverheadWarmup   3: 4096000 op, 9288.00 ns, 0.0023 ns/op
OverheadWarmup   4: 4096000 op, 9267.00 ns, 0.0023 ns/op
OverheadWarmup   5: 4096000 op, 9297.00 ns, 0.0023 ns/op
OverheadWarmup   6: 4096000 op, 9278.00 ns, 0.0023 ns/op
OverheadWarmup   7: 4096000 op, 9308.00 ns, 0.0023 ns/op
OverheadWarmup   8: 4096000 op, 9307.00 ns, 0.0023 ns/op

OverheadActual   1: 4096000 op, 9327.00 ns, 0.0023 ns/op
OverheadActual   2: 4096000 op, 9317.00 ns, 0.0023 ns/op
OverheadActual   3: 4096000 op, 9297.00 ns, 0.0023 ns/op
OverheadActual   4: 4096000 op, 9308.00 ns, 0.0023 ns/op
OverheadActual   5: 4096000 op, 9318.00 ns, 0.0023 ns/op
OverheadActual   6: 4096000 op, 9307.00 ns, 0.0023 ns/op
OverheadActual   7: 4096000 op, 9328.00 ns, 0.0023 ns/op
OverheadActual   8: 4096000 op, 9167.00 ns, 0.0022 ns/op
OverheadActual   9: 4096000 op, 10379.00 ns, 0.0025 ns/op
OverheadActual  10: 4096000 op, 9248.00 ns, 0.0023 ns/op
OverheadActual  11: 4096000 op, 9267.00 ns, 0.0023 ns/op
OverheadActual  12: 4096000 op, 9318.00 ns, 0.0023 ns/op
OverheadActual  13: 4096000 op, 9227.00 ns, 0.0023 ns/op
OverheadActual  14: 4096000 op, 9297.00 ns, 0.0023 ns/op
OverheadActual  15: 4096000 op, 9298.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 4096000 op, 689140346.00 ns, 168.2472 ns/op
WorkloadWarmup   2: 4096000 op, 675735729.00 ns, 164.9745 ns/op
WorkloadWarmup   3: 4096000 op, 670392139.00 ns, 163.6700 ns/op
WorkloadWarmup   4: 4096000 op, 666521324.00 ns, 162.7249 ns/op
WorkloadWarmup   5: 4096000 op, 667163852.00 ns, 162.8818 ns/op
WorkloadWarmup   6: 4096000 op, 667347124.00 ns, 162.9265 ns/op
WorkloadWarmup   7: 4096000 op, 668012514.00 ns, 163.0890 ns/op
WorkloadWarmup   8: 4096000 op, 675054610.00 ns, 164.8083 ns/op
WorkloadWarmup   9: 4096000 op, 666687243.00 ns, 162.7654 ns/op
WorkloadWarmup  10: 4096000 op, 666853142.00 ns, 162.8059 ns/op
WorkloadWarmup  11: 4096000 op, 665128809.00 ns, 162.3850 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 671084170.00 ns, 163.8389 ns/op
WorkloadActual   2: 4096000 op, 668702447.00 ns, 163.2574 ns/op
WorkloadActual   3: 4096000 op, 669825649.00 ns, 163.5317 ns/op
WorkloadActual   4: 4096000 op, 681802069.00 ns, 166.4556 ns/op
WorkloadActual   5: 4096000 op, 687399059.00 ns, 167.8220 ns/op
WorkloadActual   6: 4096000 op, 684845551.00 ns, 167.1986 ns/op
WorkloadActual   7: 4096000 op, 669293027.00 ns, 163.4016 ns/op
WorkloadActual   8: 4096000 op, 671704269.00 ns, 163.9903 ns/op
WorkloadActual   9: 4096000 op, 670250220.00 ns, 163.6353 ns/op
WorkloadActual  10: 4096000 op, 673994927.00 ns, 164.5495 ns/op
WorkloadActual  11: 4096000 op, 672141875.00 ns, 164.0971 ns/op
WorkloadActual  12: 4096000 op, 671760775.00 ns, 164.0041 ns/op
WorkloadActual  13: 4096000 op, 670263342.00 ns, 163.6385 ns/op
WorkloadActual  14: 4096000 op, 673240999.00 ns, 164.3655 ns/op
WorkloadActual  15: 4096000 op, 669766212.00 ns, 163.5171 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 671074863.00 ns, 163.8366 ns/op
WorkloadResult   2: 4096000 op, 668693140.00 ns, 163.2552 ns/op
WorkloadResult   3: 4096000 op, 669816342.00 ns, 163.5294 ns/op
WorkloadResult   4: 4096000 op, 669283720.00 ns, 163.3993 ns/op
WorkloadResult   5: 4096000 op, 671694962.00 ns, 163.9880 ns/op
WorkloadResult   6: 4096000 op, 670240913.00 ns, 163.6330 ns/op
WorkloadResult   7: 4096000 op, 673985620.00 ns, 164.5473 ns/op
WorkloadResult   8: 4096000 op, 672132568.00 ns, 164.0949 ns/op
WorkloadResult   9: 4096000 op, 671751468.00 ns, 164.0018 ns/op
WorkloadResult  10: 4096000 op, 670254035.00 ns, 163.6362 ns/op
WorkloadResult  11: 4096000 op, 673231692.00 ns, 164.3632 ns/op
WorkloadResult  12: 4096000 op, 669756905.00 ns, 163.5149 ns/op
// GC:  48 0 0 819200032 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4767 has exited with code 0.

Mean = 163.817 ns, StdErr = 0.114 ns (0.07%), N = 12, StdDev = 0.393 ns
Min = 163.255 ns, Q1 = 163.526 ns, Median = 163.736 ns, Q3 = 164.025 ns, Max = 164.547 ns
IQR = 0.499 ns, LowerFence = 162.777 ns, UpperFence = 164.774 ns
ConfidenceInterval = [163.313 ns; 164.321 ns] (CI 99.9%), Margin = 0.504 ns (0.31% of Mean)
Skewness = 0.37, Kurtosis = 1.84, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-05 11:12 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 180646.00 ns, 180.6460 ns/op
WorkloadJitting  1: 1000 op, 1100371.00 ns, 1.1004 us/op

OverheadJitting  2: 16000 op, 177851.00 ns, 11.1157 ns/op
WorkloadJitting  2: 16000 op, 8257239.00 ns, 516.0774 ns/op

WorkloadPilot    1: 16000 op, 7089493.00 ns, 443.0933 ns/op
WorkloadPilot    2: 32000 op, 15074295.00 ns, 471.0717 ns/op
WorkloadPilot    3: 64000 op, 36530714.00 ns, 570.7924 ns/op
WorkloadPilot    4: 128000 op, 53823603.00 ns, 420.4969 ns/op
WorkloadPilot    5: 256000 op, 49322995.00 ns, 192.6679 ns/op
WorkloadPilot    6: 512000 op, 36891817.00 ns, 72.0543 ns/op
WorkloadPilot    7: 1024000 op, 69214659.00 ns, 67.5924 ns/op
WorkloadPilot    8: 2048000 op, 138481033.00 ns, 67.6177 ns/op
WorkloadPilot    9: 4096000 op, 274399099.00 ns, 66.9920 ns/op
WorkloadPilot   10: 8192000 op, 550398381.00 ns, 67.1873 ns/op

OverheadWarmup   1: 8192000 op, 22943.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 31920.00 ns, 0.0039 ns/op
OverheadWarmup   3: 8192000 op, 33112.00 ns, 0.0040 ns/op
OverheadWarmup   4: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18785.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18804.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 22171.00 ns, 0.0027 ns/op
OverheadActual   4: 8192000 op, 18845.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 19596.00 ns, 0.0024 ns/op
OverheadActual  11: 8192000 op, 21911.00 ns, 0.0027 ns/op
OverheadActual  12: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 18775.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 556465780.00 ns, 67.9280 ns/op
WorkloadWarmup   2: 8192000 op, 559259677.00 ns, 68.2690 ns/op
WorkloadWarmup   3: 8192000 op, 550622740.00 ns, 67.2147 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 554657449.00 ns, 67.7072 ns/op
WorkloadActual   2: 8192000 op, 551798120.00 ns, 67.3582 ns/op
WorkloadActual   3: 8192000 op, 550883215.00 ns, 67.2465 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 554638654.00 ns, 67.7049 ns/op
WorkloadResult   2: 8192000 op, 551779325.00 ns, 67.3559 ns/op
WorkloadResult   3: 8192000 op, 550864420.00 ns, 67.2442 ns/op
// GC:  15 0 0 262144000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4783 has exited with code 0.

Mean = 67.435 ns, StdErr = 0.139 ns (0.21%), N = 3, StdDev = 0.240 ns
Min = 67.244 ns, Q1 = 67.300 ns, Median = 67.356 ns, Q3 = 67.530 ns, Max = 67.705 ns
IQR = 0.230 ns, LowerFence = 66.954 ns, UpperFence = 67.876 ns
ConfidenceInterval = [63.050 ns; 71.820 ns] (CI 99.9%), Margin = 4.385 ns (6.50% of Mean)
Skewness = 0.29, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-05 11:12 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 180637.00 ns, 180.6370 ns/op
WorkloadJitting  1: 1000 op, 1817668.00 ns, 1.8177 us/op

OverheadJitting  2: 16000 op, 179886.00 ns, 11.2429 ns/op
WorkloadJitting  2: 16000 op, 13982660.00 ns, 873.9163 ns/op

WorkloadPilot    1: 16000 op, 12280097.00 ns, 767.5061 ns/op
WorkloadPilot    2: 32000 op, 23327146.00 ns, 728.9733 ns/op
WorkloadPilot    3: 64000 op, 42413578.00 ns, 662.7122 ns/op
WorkloadPilot    4: 128000 op, 72237903.00 ns, 564.3586 ns/op
WorkloadPilot    5: 256000 op, 34259671.00 ns, 133.8268 ns/op
WorkloadPilot    6: 512000 op, 60222074.00 ns, 117.6212 ns/op
WorkloadPilot    7: 1024000 op, 119137498.00 ns, 116.3452 ns/op
WorkloadPilot    8: 2048000 op, 238016284.00 ns, 116.2189 ns/op
WorkloadPilot    9: 4096000 op, 477431056.00 ns, 116.5603 ns/op
WorkloadPilot   10: 8192000 op, 960667922.00 ns, 117.2690 ns/op

OverheadWarmup   1: 8192000 op, 23213.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 39043.00 ns, 0.0048 ns/op
OverheadWarmup   3: 8192000 op, 36538.00 ns, 0.0045 ns/op
OverheadWarmup   4: 8192000 op, 31639.00 ns, 0.0039 ns/op
OverheadWarmup   5: 8192000 op, 37400.00 ns, 0.0046 ns/op
OverheadWarmup   6: 8192000 op, 36458.00 ns, 0.0045 ns/op

OverheadActual   1: 8192000 op, 36588.00 ns, 0.0045 ns/op
OverheadActual   2: 8192000 op, 36768.00 ns, 0.0045 ns/op
OverheadActual   3: 8192000 op, 45585.00 ns, 0.0056 ns/op
OverheadActual   4: 8192000 op, 31549.00 ns, 0.0039 ns/op
OverheadActual   5: 8192000 op, 36638.00 ns, 0.0045 ns/op
OverheadActual   6: 8192000 op, 36638.00 ns, 0.0045 ns/op
OverheadActual   7: 8192000 op, 36548.00 ns, 0.0045 ns/op
OverheadActual   8: 8192000 op, 36438.00 ns, 0.0044 ns/op
OverheadActual   9: 8192000 op, 31429.00 ns, 0.0038 ns/op
OverheadActual  10: 8192000 op, 60543.00 ns, 0.0074 ns/op
OverheadActual  11: 8192000 op, 41748.00 ns, 0.0051 ns/op
OverheadActual  12: 8192000 op, 36468.00 ns, 0.0045 ns/op
OverheadActual  13: 8192000 op, 36468.00 ns, 0.0045 ns/op
OverheadActual  14: 8192000 op, 31709.00 ns, 0.0039 ns/op
OverheadActual  15: 8192000 op, 30748.00 ns, 0.0038 ns/op
OverheadActual  16: 8192000 op, 31950.00 ns, 0.0039 ns/op
OverheadActual  17: 8192000 op, 31298.00 ns, 0.0038 ns/op
OverheadActual  18: 8192000 op, 31128.00 ns, 0.0038 ns/op
OverheadActual  19: 8192000 op, 33723.00 ns, 0.0041 ns/op
OverheadActual  20: 8192000 op, 31499.00 ns, 0.0038 ns/op

WorkloadWarmup   1: 8192000 op, 979048940.00 ns, 119.5128 ns/op
WorkloadWarmup   2: 8192000 op, 965824207.00 ns, 117.8985 ns/op
WorkloadWarmup   3: 8192000 op, 955714400.00 ns, 116.6644 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 965067628.00 ns, 117.8061 ns/op
WorkloadActual   2: 8192000 op, 955228002.00 ns, 116.6050 ns/op
WorkloadActual   3: 8192000 op, 960353905.00 ns, 117.2307 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 965031175.00 ns, 117.8017 ns/op
WorkloadResult   2: 8192000 op, 955191549.00 ns, 116.6005 ns/op
WorkloadResult   3: 8192000 op, 960317452.00 ns, 117.2263 ns/op
// GC:  133 0 0 2228224000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4791 has exited with code 0.

Mean = 117.209 ns, StdErr = 0.347 ns (0.30%), N = 3, StdDev = 0.601 ns
Min = 116.601 ns, Q1 = 116.913 ns, Median = 117.226 ns, Q3 = 117.514 ns, Max = 117.802 ns
IQR = 0.601 ns, LowerFence = 116.013 ns, UpperFence = 118.415 ns
ConfidenceInterval = [106.250 ns; 128.169 ns] (CI 99.9%), Margin = 10.960 ns (9.35% of Mean)
Skewness = -0.03, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-05 11:11 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 186227.00 ns, 186.2270 ns/op
WorkloadJitting  1: 1000 op, 1213061.00 ns, 1.2131 us/op

OverheadJitting  2: 16000 op, 199482.00 ns, 12.4676 ns/op
WorkloadJitting  2: 16000 op, 10716997.00 ns, 669.8123 ns/op

WorkloadPilot    1: 16000 op, 9368935.00 ns, 585.5584 ns/op
WorkloadPilot    2: 32000 op, 17026287.00 ns, 532.0715 ns/op
WorkloadPilot    3: 64000 op, 33870145.00 ns, 529.2210 ns/op
WorkloadPilot    4: 128000 op, 70283722.00 ns, 549.0916 ns/op
WorkloadPilot    5: 256000 op, 44236273.00 ns, 172.7979 ns/op
WorkloadPilot    6: 512000 op, 45735212.00 ns, 89.3266 ns/op
WorkloadPilot    7: 1024000 op, 89621729.00 ns, 87.5212 ns/op
WorkloadPilot    8: 2048000 op, 179240752.00 ns, 87.5199 ns/op
WorkloadPilot    9: 4096000 op, 361592975.00 ns, 88.2795 ns/op
WorkloadPilot   10: 8192000 op, 717726105.00 ns, 87.6130 ns/op

OverheadWarmup   1: 8192000 op, 22983.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18835.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18826.00 ns, 0.0023 ns/op
OverheadWarmup   7: 8192000 op, 18785.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 22041.00 ns, 0.0027 ns/op
OverheadActual   3: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18825.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18845.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18786.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18846.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 36478.00 ns, 0.0045 ns/op
OverheadActual   9: 8192000 op, 35958.00 ns, 0.0044 ns/op
OverheadActual  10: 8192000 op, 35145.00 ns, 0.0043 ns/op
OverheadActual  11: 8192000 op, 31608.00 ns, 0.0039 ns/op
OverheadActual  12: 8192000 op, 38592.00 ns, 0.0047 ns/op
OverheadActual  13: 8192000 op, 36107.00 ns, 0.0044 ns/op
OverheadActual  14: 8192000 op, 46277.00 ns, 0.0056 ns/op
OverheadActual  15: 8192000 op, 31258.00 ns, 0.0038 ns/op
OverheadActual  16: 8192000 op, 31308.00 ns, 0.0038 ns/op
OverheadActual  17: 8192000 op, 31208.00 ns, 0.0038 ns/op
OverheadActual  18: 8192000 op, 36468.00 ns, 0.0045 ns/op
OverheadActual  19: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual  20: 8192000 op, 18785.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 724437472.00 ns, 88.4323 ns/op
WorkloadWarmup   2: 8192000 op, 715232563.00 ns, 87.3087 ns/op
WorkloadWarmup   3: 8192000 op, 712176927.00 ns, 86.9357 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 716036532.00 ns, 87.4068 ns/op
WorkloadActual   2: 8192000 op, 711542585.00 ns, 86.8582 ns/op
WorkloadActual   3: 8192000 op, 739996494.00 ns, 90.3316 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 716005299.00 ns, 87.4030 ns/op
WorkloadResult   2: 8192000 op, 711511352.00 ns, 86.8544 ns/op
WorkloadResult   3: 8192000 op, 739965261.00 ns, 90.3278 ns/op
// GC:  50 0 0 851968000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4804 has exited with code 0.

Mean = 88.195 ns, StdErr = 1.078 ns (1.22%), N = 3, StdDev = 1.867 ns
Min = 86.854 ns, Q1 = 87.129 ns, Median = 87.403 ns, Q3 = 88.865 ns, Max = 90.328 ns
IQR = 1.737 ns, LowerFence = 84.524 ns, UpperFence = 91.470 ns
ConfidenceInterval = [54.129 ns; 122.261 ns] (CI 99.9%), Margin = 34.066 ns (38.63% of Mean)
Skewness = 0.35, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-05 11:11 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 173163.00 ns, 173.1630 ns/op
WorkloadJitting  1: 1000 op, 1723252.00 ns, 1.7233 us/op

OverheadJitting  2: 16000 op, 183712.00 ns, 11.4820 ns/op
WorkloadJitting  2: 16000 op, 20022521.00 ns, 1.2514 us/op

WorkloadPilot    1: 16000 op, 18785799.00 ns, 1.1741 us/op
WorkloadPilot    2: 32000 op, 34888884.00 ns, 1.0903 us/op
WorkloadPilot    3: 64000 op, 69288706.00 ns, 1.0826 us/op
WorkloadPilot    4: 128000 op, 121541978.00 ns, 949.5467 ns/op
WorkloadPilot    5: 256000 op, 47163467.00 ns, 184.2323 ns/op
WorkloadPilot    6: 512000 op, 84386907.00 ns, 164.8182 ns/op
WorkloadPilot    7: 1024000 op, 167714902.00 ns, 163.7841 ns/op
WorkloadPilot    8: 2048000 op, 331569156.00 ns, 161.8990 ns/op
WorkloadPilot    9: 4096000 op, 680569948.00 ns, 166.1548 ns/op

OverheadWarmup   1: 4096000 op, 12454.00 ns, 0.0030 ns/op
OverheadWarmup   2: 4096000 op, 9608.00 ns, 0.0023 ns/op
OverheadWarmup   3: 4096000 op, 42169.00 ns, 0.0103 ns/op
OverheadWarmup   4: 4096000 op, 18695.00 ns, 0.0046 ns/op
OverheadWarmup   5: 4096000 op, 18675.00 ns, 0.0046 ns/op
OverheadWarmup   6: 4096000 op, 18694.00 ns, 0.0046 ns/op
OverheadWarmup   7: 4096000 op, 18565.00 ns, 0.0045 ns/op

OverheadActual   1: 4096000 op, 18775.00 ns, 0.0046 ns/op
OverheadActual   2: 4096000 op, 18515.00 ns, 0.0045 ns/op
OverheadActual   3: 4096000 op, 18214.00 ns, 0.0044 ns/op
OverheadActual   4: 4096000 op, 18755.00 ns, 0.0046 ns/op
OverheadActual   5: 4096000 op, 18615.00 ns, 0.0045 ns/op
OverheadActual   6: 4096000 op, 18604.00 ns, 0.0045 ns/op
OverheadActual   7: 4096000 op, 18474.00 ns, 0.0045 ns/op
OverheadActual   8: 4096000 op, 18675.00 ns, 0.0046 ns/op
OverheadActual   9: 4096000 op, 18635.00 ns, 0.0045 ns/op
OverheadActual  10: 4096000 op, 22902.00 ns, 0.0056 ns/op
OverheadActual  11: 4096000 op, 21440.00 ns, 0.0052 ns/op
OverheadActual  12: 4096000 op, 18634.00 ns, 0.0045 ns/op
OverheadActual  13: 4096000 op, 18665.00 ns, 0.0046 ns/op
OverheadActual  14: 4096000 op, 18354.00 ns, 0.0045 ns/op
OverheadActual  15: 4096000 op, 18584.00 ns, 0.0045 ns/op

WorkloadWarmup   1: 4096000 op, 682049846.00 ns, 166.5161 ns/op
WorkloadWarmup   2: 4096000 op, 675092678.00 ns, 164.8175 ns/op
WorkloadWarmup   3: 4096000 op, 667773444.00 ns, 163.0306 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 695489197.00 ns, 169.7972 ns/op
WorkloadActual   2: 4096000 op, 697306958.00 ns, 170.2410 ns/op
WorkloadActual   3: 4096000 op, 674929671.00 ns, 164.7778 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 695470563.00 ns, 169.7926 ns/op
WorkloadResult   2: 4096000 op, 697288324.00 ns, 170.2364 ns/op
WorkloadResult   3: 4096000 op, 674911037.00 ns, 164.7732 ns/op
// GC:  48 0 0 819200000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4813 has exited with code 0.

Mean = 168.267 ns, StdErr = 1.752 ns (1.04%), N = 3, StdDev = 3.034 ns
Min = 164.773 ns, Q1 = 167.283 ns, Median = 169.793 ns, Q3 = 170.015 ns, Max = 170.236 ns
IQR = 2.732 ns, LowerFence = 163.186 ns, UpperFence = 174.112 ns
ConfidenceInterval = [112.912 ns; 223.622 ns] (CI 99.9%), Margin = 55.355 ns (32.90% of Mean)
Skewness = -0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-05 11:11 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 68.766 ns, StdErr = 0.105 ns (0.15%), N = 15, StdDev = 0.407 ns
Min = 68.188 ns, Q1 = 68.512 ns, Median = 68.696 ns, Q3 = 69.145 ns, Max = 69.429 ns
IQR = 0.633 ns, LowerFence = 67.563 ns, UpperFence = 70.094 ns
ConfidenceInterval = [68.331 ns; 69.201 ns] (CI 99.9%), Margin = 0.435 ns (0.63% of Mean)
Skewness = 0.12, Kurtosis = 1.48, MValue = 2
-------------------- Histogram --------------------
[67.971 ns ; 69.645 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 115.553 ns, StdErr = 0.139 ns (0.12%), N = 15, StdDev = 0.539 ns
Min = 114.898 ns, Q1 = 115.161 ns, Median = 115.502 ns, Q3 = 115.835 ns, Max = 116.723 ns
IQR = 0.674 ns, LowerFence = 114.150 ns, UpperFence = 116.846 ns
ConfidenceInterval = [114.976 ns; 116.129 ns] (CI 99.9%), Margin = 0.577 ns (0.50% of Mean)
Skewness = 0.76, Kurtosis = 2.43, MValue = 2
-------------------- Histogram --------------------
[114.611 ns ; 117.011 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 95.152 ns, StdErr = 0.112 ns (0.12%), N = 14, StdDev = 0.420 ns
Min = 94.417 ns, Q1 = 94.920 ns, Median = 95.123 ns, Q3 = 95.415 ns, Max = 95.844 ns
IQR = 0.495 ns, LowerFence = 94.177 ns, UpperFence = 96.157 ns
ConfidenceInterval = [94.678 ns; 95.625 ns] (CI 99.9%), Margin = 0.474 ns (0.50% of Mean)
Skewness = -0.03, Kurtosis = 1.9, MValue = 2
-------------------- Histogram --------------------
[94.189 ns ; 96.072 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 163.817 ns, StdErr = 0.114 ns (0.07%), N = 12, StdDev = 0.393 ns
Min = 163.255 ns, Q1 = 163.526 ns, Median = 163.736 ns, Q3 = 164.025 ns, Max = 164.547 ns
IQR = 0.499 ns, LowerFence = 162.777 ns, UpperFence = 164.774 ns
ConfidenceInterval = [163.313 ns; 164.321 ns] (CI 99.9%), Margin = 0.504 ns (0.31% of Mean)
Skewness = 0.37, Kurtosis = 1.84, MValue = 2
-------------------- Histogram --------------------
[163.030 ns ; 164.773 ns) | @@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 67.435 ns, StdErr = 0.139 ns (0.21%), N = 3, StdDev = 0.240 ns
Min = 67.244 ns, Q1 = 67.300 ns, Median = 67.356 ns, Q3 = 67.530 ns, Max = 67.705 ns
IQR = 0.230 ns, LowerFence = 66.954 ns, UpperFence = 67.876 ns
ConfidenceInterval = [63.050 ns; 71.820 ns] (CI 99.9%), Margin = 4.385 ns (6.50% of Mean)
Skewness = 0.29, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[67.025 ns ; 67.924 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 117.209 ns, StdErr = 0.347 ns (0.30%), N = 3, StdDev = 0.601 ns
Min = 116.601 ns, Q1 = 116.913 ns, Median = 117.226 ns, Q3 = 117.514 ns, Max = 117.802 ns
IQR = 0.601 ns, LowerFence = 116.013 ns, UpperFence = 118.415 ns
ConfidenceInterval = [106.250 ns; 128.169 ns] (CI 99.9%), Margin = 10.960 ns (9.35% of Mean)
Skewness = -0.03, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[116.054 ns ; 118.348 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 88.195 ns, StdErr = 1.078 ns (1.22%), N = 3, StdDev = 1.867 ns
Min = 86.854 ns, Q1 = 87.129 ns, Median = 87.403 ns, Q3 = 88.865 ns, Max = 90.328 ns
IQR = 1.737 ns, LowerFence = 84.524 ns, UpperFence = 91.470 ns
ConfidenceInterval = [54.129 ns; 122.261 ns] (CI 99.9%), Margin = 34.066 ns (38.63% of Mean)
Skewness = 0.35, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[85.155 ns ; 88.828 ns) | @@
[88.828 ns ; 92.027 ns) | @
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 168.267 ns, StdErr = 1.752 ns (1.04%), N = 3, StdDev = 3.034 ns
Min = 164.773 ns, Q1 = 167.283 ns, Median = 169.793 ns, Q3 = 170.015 ns, Max = 170.236 ns
IQR = 2.732 ns, LowerFence = 163.186 ns, UpperFence = 174.112 ns
ConfidenceInterval = [112.912 ns; 223.622 ns] (CI 99.9%), Margin = 55.355 ns (32.90% of Mean)
Skewness = -0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[164.744 ns ; 170.266 ns) | @@@
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
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  68.77 ns |  0.435 ns | 0.407 ns | 0.0018 |      32 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 115.55 ns |  0.577 ns | 0.539 ns | 0.0162 |     272 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  95.15 ns |  0.474 ns | 0.420 ns | 0.0061 |     104 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 163.82 ns |  0.504 ns | 0.393 ns | 0.0117 |     200 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  67.43 ns |  4.385 ns | 0.240 ns | 0.0018 |      32 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 117.21 ns | 10.960 ns | 0.601 ns | 0.0162 |     272 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  88.20 ns | 34.066 ns | 1.867 ns | 0.0061 |     104 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 168.27 ns | 55.355 ns | 3.034 ns | 0.0117 |     200 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Request  Request': RunStrategy=Throughput                     -> 1 outlier  was  removed (96.82 ns)
  CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': RunStrategy=Throughput -> 3 outliers were removed (166.46 ns..167.82 ns)
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
Run time: 00:01:51 (111.62 sec), executed benchmarks: 8

Global total time: 00:02:05 (125.72 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
