# NetMediate Benchmark Results

<!-- netmediate-bench-baseline: {"cmd": 73.15, "notify": 127.97, "request": 90.91, "stream": 168.96} -->

This document describes the performance characteristics of NetMediate under the current implementation, which uses **explicit handler registration only** (no assembly scanning) and **closed-type pipeline executors** registered at startup.

---

## Reference benchmark environment

The table below is updated automatically by CI on every PR benchmark run. System info comes from the BenchmarkDotNet host environment.

<!-- ci-environment-start -->
| Key | Value |
|---|---|
| OS | Linux Ubuntu 24.04.4 LTS (Noble Numbat) |
| CPU | AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores |
| .NET SDK | 10.0.203 |
| Runtime | .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3 |
| Last CI run | 2026-05-05 12:03 UTC |
| Branch | `copilot/implementar-long-term` |
| Commit | `692888b` |
<!-- ci-environment-end -->

---

## Core dispatch throughput

Measured with BenchmarkDotNet (`CoreDispatchBenchmarks`) — no behaviors, no resilience, no adapters registered.
`Mean` is the BenchmarkDotNet Throughput-job mean (ns/op). `Throughput` is the derived ops/s. The `vs baseline` column
compares against the last recorded values from the target branch (±3% = no change, ✅ = improved, ⚠️ = degraded).

<!-- ci-throughput-start -->
| Benchmark | Mean | Error | Gen0 | Allocated | Throughput | vs baseline |
|---|---|---|---|---|---|---|
| Command `Send` | 73.15 ns | ±0.732 ns | 0.0018 | 32 B | ~13.7M msg/s | — |
| Notification `Notify` | 127.97 ns | ±1.502 ns | 0.0161 | 272 B | ~7.8M msg/s | — |
| Request `Request` | 90.91 ns | ±0.294 ns | 0.0061 | 104 B | ~11.0M msg/s | — |
| Stream `RequestStream` | 168.96 ns | ±1.156 ns | 0.0117 | 200 B | ~5.9M msg/s | — |
<!-- ci-throughput-end -->

> ¹ Stream measures complete stream invocations (3 items each). Higher throughput = better.

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

Run: 2026-05-05 12:16 UTC | Branch: copilot/implementar-long-term | Commit: 926d6db

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
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  69.85 ns |  0.110 ns | 0.086 ns | 0.0018 |      32 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 119.37 ns |  1.787 ns | 1.672 ns | 0.0162 |     272 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  88.23 ns |  0.923 ns | 0.864 ns | 0.0061 |     104 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 162.06 ns |  0.735 ns | 0.652 ns | 0.0117 |     200 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  71.65 ns |  1.569 ns | 0.086 ns | 0.0018 |      32 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 119.45 ns | 31.477 ns | 1.725 ns | 0.0162 |     272 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  88.67 ns |  4.307 ns | 0.236 ns | 0.0061 |     104 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 171.07 ns | 32.894 ns | 1.803 ns | 0.0117 |     200 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.7 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 12.49 sec and exited with 0
// ***** Done, took 00:00:14 (14.25 sec)   *****
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

OverheadJitting  1: 1000 op, 201638.00 ns, 201.6380 ns/op
WorkloadJitting  1: 1000 op, 1088274.00 ns, 1.0883 us/op

OverheadJitting  2: 16000 op, 183514.00 ns, 11.4696 ns/op
WorkloadJitting  2: 16000 op, 8084586.00 ns, 505.2866 ns/op

WorkloadPilot    1: 16000 op, 6629304.00 ns, 414.3315 ns/op
WorkloadPilot    2: 32000 op, 12699599.00 ns, 396.8625 ns/op
WorkloadPilot    3: 64000 op, 25232816.00 ns, 394.2628 ns/op
WorkloadPilot    4: 128000 op, 53077987.00 ns, 414.6718 ns/op
WorkloadPilot    5: 256000 op, 53147597.00 ns, 207.6078 ns/op
WorkloadPilot    6: 512000 op, 37657459.00 ns, 73.5497 ns/op
WorkloadPilot    7: 1024000 op, 72591236.00 ns, 70.8899 ns/op
WorkloadPilot    8: 2048000 op, 144589588.00 ns, 70.6004 ns/op
WorkloadPilot    9: 4096000 op, 289054133.00 ns, 70.5699 ns/op
WorkloadPilot   10: 8192000 op, 571274046.00 ns, 69.7356 ns/op

OverheadWarmup   1: 8192000 op, 37411.00 ns, 0.0046 ns/op
OverheadWarmup   2: 8192000 op, 28725.00 ns, 0.0035 ns/op
OverheadWarmup   3: 8192000 op, 36789.00 ns, 0.0045 ns/op
OverheadWarmup   4: 8192000 op, 16431.00 ns, 0.0020 ns/op
OverheadWarmup   5: 8192000 op, 16481.00 ns, 0.0020 ns/op
OverheadWarmup   6: 8192000 op, 16361.00 ns, 0.0020 ns/op

OverheadActual   1: 8192000 op, 16581.00 ns, 0.0020 ns/op
OverheadActual   2: 8192000 op, 16411.00 ns, 0.0020 ns/op
OverheadActual   3: 8192000 op, 19647.00 ns, 0.0024 ns/op
OverheadActual   4: 8192000 op, 29666.00 ns, 0.0036 ns/op
OverheadActual   5: 8192000 op, 29526.00 ns, 0.0036 ns/op
OverheadActual   6: 8192000 op, 29706.00 ns, 0.0036 ns/op
OverheadActual   7: 8192000 op, 29635.00 ns, 0.0036 ns/op
OverheadActual   8: 8192000 op, 28654.00 ns, 0.0035 ns/op
OverheadActual   9: 8192000 op, 29546.00 ns, 0.0036 ns/op
OverheadActual  10: 8192000 op, 29726.00 ns, 0.0036 ns/op
OverheadActual  11: 8192000 op, 36308.00 ns, 0.0044 ns/op
OverheadActual  12: 8192000 op, 29555.00 ns, 0.0036 ns/op
OverheadActual  13: 8192000 op, 29886.00 ns, 0.0036 ns/op
OverheadActual  14: 8192000 op, 30818.00 ns, 0.0038 ns/op
OverheadActual  15: 8192000 op, 28293.00 ns, 0.0035 ns/op
OverheadActual  16: 8192000 op, 27853.00 ns, 0.0034 ns/op
OverheadActual  17: 8192000 op, 28353.00 ns, 0.0035 ns/op
OverheadActual  18: 8192000 op, 29295.00 ns, 0.0036 ns/op
OverheadActual  19: 8192000 op, 34635.00 ns, 0.0042 ns/op
OverheadActual  20: 8192000 op, 28093.00 ns, 0.0034 ns/op

WorkloadWarmup   1: 8192000 op, 589292190.00 ns, 71.9351 ns/op
WorkloadWarmup   2: 8192000 op, 577886660.00 ns, 70.5428 ns/op
WorkloadWarmup   3: 8192000 op, 575741543.00 ns, 70.2810 ns/op
WorkloadWarmup   4: 8192000 op, 569586119.00 ns, 69.5296 ns/op
WorkloadWarmup   5: 8192000 op, 570236089.00 ns, 69.6089 ns/op
WorkloadWarmup   6: 8192000 op, 572284854.00 ns, 69.8590 ns/op
WorkloadWarmup   7: 8192000 op, 573219980.00 ns, 69.9731 ns/op
WorkloadWarmup   8: 8192000 op, 575394722.00 ns, 70.2386 ns/op
WorkloadWarmup   9: 8192000 op, 576160141.00 ns, 70.3320 ns/op
WorkloadWarmup  10: 8192000 op, 580076796.00 ns, 70.8102 ns/op
WorkloadWarmup  11: 8192000 op, 574324417.00 ns, 70.1080 ns/op
WorkloadWarmup  12: 8192000 op, 575969726.00 ns, 70.3088 ns/op
WorkloadWarmup  13: 8192000 op, 572759960.00 ns, 69.9170 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 574631905.00 ns, 70.1455 ns/op
WorkloadActual   2: 8192000 op, 570851186.00 ns, 69.6840 ns/op
WorkloadActual   3: 8192000 op, 575141000.00 ns, 70.2076 ns/op
WorkloadActual   4: 8192000 op, 573182493.00 ns, 69.9686 ns/op
WorkloadActual   5: 8192000 op, 572382922.00 ns, 69.8710 ns/op
WorkloadActual   6: 8192000 op, 572760811.00 ns, 69.9171 ns/op
WorkloadActual   7: 8192000 op, 572325985.00 ns, 69.8640 ns/op
WorkloadActual   8: 8192000 op, 571172160.00 ns, 69.7232 ns/op
WorkloadActual   9: 8192000 op, 572069224.00 ns, 69.8327 ns/op
WorkloadActual  10: 8192000 op, 571787294.00 ns, 69.7983 ns/op
WorkloadActual  11: 8192000 op, 572555292.00 ns, 69.8920 ns/op
WorkloadActual  12: 8192000 op, 574698085.00 ns, 70.1536 ns/op
WorkloadActual  13: 8192000 op, 572341651.00 ns, 69.8659 ns/op
WorkloadActual  14: 8192000 op, 572761219.00 ns, 69.9171 ns/op
WorkloadActual  15: 8192000 op, 572985810.00 ns, 69.9446 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 570821650.00 ns, 69.6804 ns/op
WorkloadResult   2: 8192000 op, 573152957.00 ns, 69.9650 ns/op
WorkloadResult   3: 8192000 op, 572353386.00 ns, 69.8674 ns/op
WorkloadResult   4: 8192000 op, 572731275.00 ns, 69.9135 ns/op
WorkloadResult   5: 8192000 op, 572296449.00 ns, 69.8604 ns/op
WorkloadResult   6: 8192000 op, 571142624.00 ns, 69.7196 ns/op
WorkloadResult   7: 8192000 op, 572039688.00 ns, 69.8291 ns/op
WorkloadResult   8: 8192000 op, 571757758.00 ns, 69.7946 ns/op
WorkloadResult   9: 8192000 op, 572525756.00 ns, 69.8884 ns/op
WorkloadResult  10: 8192000 op, 572312115.00 ns, 69.8623 ns/op
WorkloadResult  11: 8192000 op, 572731683.00 ns, 69.9135 ns/op
WorkloadResult  12: 8192000 op, 572956274.00 ns, 69.9410 ns/op
// GC:  15 0 0 262144000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4620 has exited with code 0.

Mean = 69.853 ns, StdErr = 0.025 ns (0.04%), N = 12, StdDev = 0.086 ns
Min = 69.680 ns, Q1 = 69.820 ns, Median = 69.865 ns, Q3 = 69.913 ns, Max = 69.965 ns
IQR = 0.093 ns, LowerFence = 69.681 ns, UpperFence = 70.053 ns
ConfidenceInterval = [69.743 ns; 69.963 ns] (CI 99.9%), Margin = 0.110 ns (0.16% of Mean)
Skewness = -0.68, Kurtosis = 2.26, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-05 12:17 (0h 2m from now) **
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

OverheadJitting  1: 1000 op, 193583.00 ns, 193.5830 ns/op
WorkloadJitting  1: 1000 op, 1363830.00 ns, 1.3638 us/op

OverheadJitting  2: 16000 op, 179277.00 ns, 11.2048 ns/op
WorkloadJitting  2: 16000 op, 13619498.00 ns, 851.2186 ns/op

WorkloadPilot    1: 16000 op, 11926760.00 ns, 745.4225 ns/op
WorkloadPilot    2: 32000 op, 22134223.00 ns, 691.6945 ns/op
WorkloadPilot    3: 64000 op, 40467399.00 ns, 632.3031 ns/op
WorkloadPilot    4: 128000 op, 74183926.00 ns, 579.5619 ns/op
WorkloadPilot    5: 256000 op, 37052579.00 ns, 144.7366 ns/op
WorkloadPilot    6: 512000 op, 61705770.00 ns, 120.5191 ns/op
WorkloadPilot    7: 1024000 op, 120199491.00 ns, 117.3823 ns/op
WorkloadPilot    8: 2048000 op, 243611684.00 ns, 118.9510 ns/op
WorkloadPilot    9: 4096000 op, 487806713.00 ns, 119.0934 ns/op
WorkloadPilot   10: 8192000 op, 971541902.00 ns, 118.5964 ns/op

OverheadWarmup   1: 8192000 op, 21331.00 ns, 0.0026 ns/op
OverheadWarmup   2: 8192000 op, 18274.00 ns, 0.0022 ns/op
OverheadWarmup   3: 8192000 op, 18084.00 ns, 0.0022 ns/op
OverheadWarmup   4: 8192000 op, 19207.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18044.00 ns, 0.0022 ns/op
OverheadWarmup   6: 8192000 op, 17964.00 ns, 0.0022 ns/op
OverheadWarmup   7: 8192000 op, 18004.00 ns, 0.0022 ns/op
OverheadWarmup   8: 8192000 op, 18164.00 ns, 0.0022 ns/op
OverheadWarmup   9: 8192000 op, 19497.00 ns, 0.0024 ns/op
OverheadWarmup  10: 8192000 op, 18124.00 ns, 0.0022 ns/op

OverheadActual   1: 8192000 op, 17964.00 ns, 0.0022 ns/op
OverheadActual   2: 8192000 op, 18134.00 ns, 0.0022 ns/op
OverheadActual   3: 8192000 op, 18114.00 ns, 0.0022 ns/op
OverheadActual   4: 8192000 op, 18044.00 ns, 0.0022 ns/op
OverheadActual   5: 8192000 op, 18054.00 ns, 0.0022 ns/op
OverheadActual   6: 8192000 op, 18125.00 ns, 0.0022 ns/op
OverheadActual   7: 8192000 op, 19527.00 ns, 0.0024 ns/op
OverheadActual   8: 8192000 op, 18094.00 ns, 0.0022 ns/op
OverheadActual   9: 8192000 op, 18123.00 ns, 0.0022 ns/op
OverheadActual  10: 8192000 op, 17994.00 ns, 0.0022 ns/op
OverheadActual  11: 8192000 op, 17984.00 ns, 0.0022 ns/op
OverheadActual  12: 8192000 op, 18143.00 ns, 0.0022 ns/op
OverheadActual  13: 8192000 op, 18074.00 ns, 0.0022 ns/op
OverheadActual  14: 8192000 op, 18114.00 ns, 0.0022 ns/op
OverheadActual  15: 8192000 op, 20469.00 ns, 0.0025 ns/op

WorkloadWarmup   1: 8192000 op, 982507969.00 ns, 119.9351 ns/op
WorkloadWarmup   2: 8192000 op, 982371162.00 ns, 119.9184 ns/op
WorkloadWarmup   3: 8192000 op, 968331583.00 ns, 118.2045 ns/op
WorkloadWarmup   4: 8192000 op, 963006411.00 ns, 117.5545 ns/op
WorkloadWarmup   5: 8192000 op, 967382214.00 ns, 118.0886 ns/op
WorkloadWarmup   6: 8192000 op, 976377451.00 ns, 119.1867 ns/op
WorkloadWarmup   7: 8192000 op, 965521101.00 ns, 117.8615 ns/op
WorkloadWarmup   8: 8192000 op, 966041838.00 ns, 117.9250 ns/op
WorkloadWarmup   9: 8192000 op, 975257818.00 ns, 119.0500 ns/op
WorkloadWarmup  10: 8192000 op, 969167846.00 ns, 118.3066 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 987308782.00 ns, 120.5211 ns/op
WorkloadActual   2: 8192000 op, 966939214.00 ns, 118.0346 ns/op
WorkloadActual   3: 8192000 op, 964029330.00 ns, 117.6794 ns/op
WorkloadActual   4: 8192000 op, 979092119.00 ns, 119.5181 ns/op
WorkloadActual   5: 8192000 op, 968979905.00 ns, 118.2837 ns/op
WorkloadActual   6: 8192000 op, 986312340.00 ns, 120.3995 ns/op
WorkloadActual   7: 8192000 op, 964059038.00 ns, 117.6830 ns/op
WorkloadActual   8: 8192000 op, 970811474.00 ns, 118.5073 ns/op
WorkloadActual   9: 8192000 op, 975453887.00 ns, 119.0740 ns/op
WorkloadActual  10: 8192000 op, 972012524.00 ns, 118.6539 ns/op
WorkloadActual  11: 8192000 op, 982448095.00 ns, 119.9277 ns/op
WorkloadActual  12: 8192000 op, 964938354.00 ns, 117.7903 ns/op
WorkloadActual  13: 8192000 op, 974189231.00 ns, 118.9196 ns/op
WorkloadActual  14: 8192000 op, 1003880680.00 ns, 122.5440 ns/op
WorkloadActual  15: 8192000 op, 1008103016.00 ns, 123.0595 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 987290668.00 ns, 120.5189 ns/op
WorkloadResult   2: 8192000 op, 966921100.00 ns, 118.0324 ns/op
WorkloadResult   3: 8192000 op, 964011216.00 ns, 117.6772 ns/op
WorkloadResult   4: 8192000 op, 979074005.00 ns, 119.5159 ns/op
WorkloadResult   5: 8192000 op, 968961791.00 ns, 118.2815 ns/op
WorkloadResult   6: 8192000 op, 986294226.00 ns, 120.3972 ns/op
WorkloadResult   7: 8192000 op, 964040924.00 ns, 117.6808 ns/op
WorkloadResult   8: 8192000 op, 970793360.00 ns, 118.5050 ns/op
WorkloadResult   9: 8192000 op, 975435773.00 ns, 119.0717 ns/op
WorkloadResult  10: 8192000 op, 971994410.00 ns, 118.6517 ns/op
WorkloadResult  11: 8192000 op, 982429981.00 ns, 119.9255 ns/op
WorkloadResult  12: 8192000 op, 964920240.00 ns, 117.7881 ns/op
WorkloadResult  13: 8192000 op, 974171117.00 ns, 118.9174 ns/op
WorkloadResult  14: 8192000 op, 1003862566.00 ns, 122.5418 ns/op
WorkloadResult  15: 8192000 op, 1008084902.00 ns, 123.0572 ns/op
// GC:  133 0 0 2228224000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4635 has exited with code 0.

Mean = 119.371 ns, StdErr = 0.432 ns (0.36%), N = 15, StdDev = 1.672 ns
Min = 117.677 ns, Q1 = 118.157 ns, Median = 118.917 ns, Q3 = 120.161 ns, Max = 123.057 ns
IQR = 2.004 ns, LowerFence = 115.150 ns, UpperFence = 123.168 ns
ConfidenceInterval = [117.584 ns; 121.158 ns] (CI 99.9%), Margin = 1.787 ns (1.50% of Mean)
Skewness = 0.95, Kurtosis = 2.71, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-05 12:18 (0h 2m from now) **
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

OverheadJitting  1: 1000 op, 239048.00 ns, 239.0480 ns/op
WorkloadJitting  1: 1000 op, 1365974.00 ns, 1.3660 us/op

OverheadJitting  2: 16000 op, 207089.00 ns, 12.9431 ns/op
WorkloadJitting  2: 16000 op, 11066165.00 ns, 691.6353 ns/op

WorkloadPilot    1: 16000 op, 9415666.00 ns, 588.4791 ns/op
WorkloadPilot    2: 32000 op, 17555657.00 ns, 548.6143 ns/op
WorkloadPilot    3: 64000 op, 34918812.00 ns, 545.6064 ns/op
WorkloadPilot    4: 128000 op, 78216765.00 ns, 611.0685 ns/op
WorkloadPilot    5: 256000 op, 26585779.00 ns, 103.8507 ns/op
WorkloadPilot    6: 512000 op, 46075276.00 ns, 89.9908 ns/op
WorkloadPilot    7: 1024000 op, 91951940.00 ns, 89.7968 ns/op
WorkloadPilot    8: 2048000 op, 182476849.00 ns, 89.1000 ns/op
WorkloadPilot    9: 4096000 op, 364753203.00 ns, 89.0511 ns/op
WorkloadPilot   10: 8192000 op, 712538563.00 ns, 86.9798 ns/op

OverheadWarmup   1: 8192000 op, 20969.00 ns, 0.0026 ns/op
OverheadWarmup   2: 8192000 op, 18174.00 ns, 0.0022 ns/op
OverheadWarmup   3: 8192000 op, 18164.00 ns, 0.0022 ns/op
OverheadWarmup   4: 8192000 op, 18174.00 ns, 0.0022 ns/op
OverheadWarmup   5: 8192000 op, 18164.00 ns, 0.0022 ns/op
OverheadWarmup   6: 8192000 op, 18104.00 ns, 0.0022 ns/op
OverheadWarmup   7: 8192000 op, 18184.00 ns, 0.0022 ns/op
OverheadWarmup   8: 8192000 op, 18144.00 ns, 0.0022 ns/op

OverheadActual   1: 8192000 op, 19456.00 ns, 0.0024 ns/op
OverheadActual   2: 8192000 op, 18054.00 ns, 0.0022 ns/op
OverheadActual   3: 8192000 op, 18234.00 ns, 0.0022 ns/op
OverheadActual   4: 8192000 op, 18034.00 ns, 0.0022 ns/op
OverheadActual   5: 8192000 op, 18114.00 ns, 0.0022 ns/op
OverheadActual   6: 8192000 op, 18144.00 ns, 0.0022 ns/op
OverheadActual   7: 8192000 op, 18114.00 ns, 0.0022 ns/op
OverheadActual   8: 8192000 op, 18134.00 ns, 0.0022 ns/op
OverheadActual   9: 8192000 op, 19437.00 ns, 0.0024 ns/op
OverheadActual  10: 8192000 op, 18184.00 ns, 0.0022 ns/op
OverheadActual  11: 8192000 op, 18184.00 ns, 0.0022 ns/op
OverheadActual  12: 8192000 op, 18104.00 ns, 0.0022 ns/op
OverheadActual  13: 8192000 op, 18124.00 ns, 0.0022 ns/op
OverheadActual  14: 8192000 op, 18154.00 ns, 0.0022 ns/op
OverheadActual  15: 8192000 op, 18185.00 ns, 0.0022 ns/op

WorkloadWarmup   1: 8192000 op, 741339668.00 ns, 90.4956 ns/op
WorkloadWarmup   2: 8192000 op, 721966262.00 ns, 88.1306 ns/op
WorkloadWarmup   3: 8192000 op, 714866134.00 ns, 87.2639 ns/op
WorkloadWarmup   4: 8192000 op, 717030696.00 ns, 87.5282 ns/op
WorkloadWarmup   5: 8192000 op, 716421554.00 ns, 87.4538 ns/op
WorkloadWarmup   6: 8192000 op, 712529748.00 ns, 86.9787 ns/op
WorkloadWarmup   7: 8192000 op, 708002099.00 ns, 86.4260 ns/op
WorkloadWarmup   8: 8192000 op, 710641904.00 ns, 86.7483 ns/op
WorkloadWarmup   9: 8192000 op, 711865481.00 ns, 86.8976 ns/op
WorkloadWarmup  10: 8192000 op, 709418061.00 ns, 86.5989 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 722757258.00 ns, 88.2272 ns/op
WorkloadActual   2: 8192000 op, 712815504.00 ns, 87.0136 ns/op
WorkloadActual   3: 8192000 op, 718700111.00 ns, 87.7319 ns/op
WorkloadActual   4: 8192000 op, 713653668.00 ns, 87.1159 ns/op
WorkloadActual   5: 8192000 op, 716939797.00 ns, 87.5171 ns/op
WorkloadActual   6: 8192000 op, 726038206.00 ns, 88.6277 ns/op
WorkloadActual   7: 8192000 op, 721206506.00 ns, 88.0379 ns/op
WorkloadActual   8: 8192000 op, 725784641.00 ns, 88.5968 ns/op
WorkloadActual   9: 8192000 op, 732525876.00 ns, 89.4197 ns/op
WorkloadActual  10: 8192000 op, 725880150.00 ns, 88.6084 ns/op
WorkloadActual  11: 8192000 op, 733796941.00 ns, 89.5748 ns/op
WorkloadActual  12: 8192000 op, 734375016.00 ns, 89.6454 ns/op
WorkloadActual  13: 8192000 op, 718002902.00 ns, 87.6468 ns/op
WorkloadActual  14: 8192000 op, 715017988.00 ns, 87.2825 ns/op
WorkloadActual  15: 8192000 op, 724220133.00 ns, 88.4058 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 722739114.00 ns, 88.2250 ns/op
WorkloadResult   2: 8192000 op, 712797360.00 ns, 87.0114 ns/op
WorkloadResult   3: 8192000 op, 718681967.00 ns, 87.7297 ns/op
WorkloadResult   4: 8192000 op, 713635524.00 ns, 87.1137 ns/op
WorkloadResult   5: 8192000 op, 716921653.00 ns, 87.5149 ns/op
WorkloadResult   6: 8192000 op, 726020062.00 ns, 88.6255 ns/op
WorkloadResult   7: 8192000 op, 721188362.00 ns, 88.0357 ns/op
WorkloadResult   8: 8192000 op, 725766497.00 ns, 88.5945 ns/op
WorkloadResult   9: 8192000 op, 732507732.00 ns, 89.4174 ns/op
WorkloadResult  10: 8192000 op, 725862006.00 ns, 88.6062 ns/op
WorkloadResult  11: 8192000 op, 733778797.00 ns, 89.5726 ns/op
WorkloadResult  12: 8192000 op, 734356872.00 ns, 89.6432 ns/op
WorkloadResult  13: 8192000 op, 717984758.00 ns, 87.6446 ns/op
WorkloadResult  14: 8192000 op, 714999844.00 ns, 87.2803 ns/op
WorkloadResult  15: 8192000 op, 724201989.00 ns, 88.4036 ns/op
// GC:  50 0 0 851968000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4652 has exited with code 0.

Mean = 88.228 ns, StdErr = 0.223 ns (0.25%), N = 15, StdDev = 0.864 ns
Min = 87.011 ns, Q1 = 87.580 ns, Median = 88.225 ns, Q3 = 88.616 ns, Max = 89.643 ns
IQR = 1.036 ns, LowerFence = 86.026 ns, UpperFence = 90.170 ns
ConfidenceInterval = [87.304 ns; 89.151 ns] (CI 99.9%), Margin = 0.923 ns (1.05% of Mean)
Skewness = 0.25, Kurtosis = 1.72, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-05 12:18 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 203783.00 ns, 203.7830 ns/op
WorkloadJitting  1: 1000 op, 1756116.00 ns, 1.7561 us/op

OverheadJitting  2: 16000 op, 179688.00 ns, 11.2305 ns/op
WorkloadJitting  2: 16000 op, 19862758.00 ns, 1.2414 us/op

WorkloadPilot    1: 16000 op, 17879845.00 ns, 1.1175 us/op
WorkloadPilot    2: 32000 op, 33542107.00 ns, 1.0482 us/op
WorkloadPilot    3: 64000 op, 66581280.00 ns, 1.0403 us/op
WorkloadPilot    4: 128000 op, 122383124.00 ns, 956.1182 ns/op
WorkloadPilot    5: 256000 op, 46002167.00 ns, 179.6960 ns/op
WorkloadPilot    6: 512000 op, 83642898.00 ns, 163.3650 ns/op
WorkloadPilot    7: 1024000 op, 169021299.00 ns, 165.0599 ns/op
WorkloadPilot    8: 2048000 op, 336509513.00 ns, 164.3113 ns/op
WorkloadPilot    9: 4096000 op, 660577480.00 ns, 161.2738 ns/op

OverheadWarmup   1: 4096000 op, 20980.00 ns, 0.0051 ns/op
OverheadWarmup   2: 4096000 op, 8416.00 ns, 0.0021 ns/op
OverheadWarmup   3: 4096000 op, 8416.00 ns, 0.0021 ns/op
OverheadWarmup   4: 4096000 op, 8506.00 ns, 0.0021 ns/op
OverheadWarmup   5: 4096000 op, 8416.00 ns, 0.0021 ns/op
OverheadWarmup   6: 4096000 op, 8476.00 ns, 0.0021 ns/op

OverheadActual   1: 4096000 op, 8556.00 ns, 0.0021 ns/op
OverheadActual   2: 4096000 op, 8456.00 ns, 0.0021 ns/op
OverheadActual   3: 4096000 op, 8456.00 ns, 0.0021 ns/op
OverheadActual   4: 4096000 op, 8596.00 ns, 0.0021 ns/op
OverheadActual   5: 4096000 op, 9047.00 ns, 0.0022 ns/op
OverheadActual   6: 4096000 op, 8566.00 ns, 0.0021 ns/op
OverheadActual   7: 4096000 op, 8586.00 ns, 0.0021 ns/op
OverheadActual   8: 4096000 op, 8556.00 ns, 0.0021 ns/op
OverheadActual   9: 4096000 op, 8447.00 ns, 0.0021 ns/op
OverheadActual  10: 4096000 op, 8436.00 ns, 0.0021 ns/op
OverheadActual  11: 4096000 op, 10670.00 ns, 0.0026 ns/op
OverheadActual  12: 4096000 op, 8426.00 ns, 0.0021 ns/op
OverheadActual  13: 4096000 op, 8475.00 ns, 0.0021 ns/op
OverheadActual  14: 4096000 op, 8446.00 ns, 0.0021 ns/op
OverheadActual  15: 4096000 op, 8627.00 ns, 0.0021 ns/op

WorkloadWarmup   1: 4096000 op, 668576946.00 ns, 163.2268 ns/op
WorkloadWarmup   2: 4096000 op, 664036012.00 ns, 162.1182 ns/op
WorkloadWarmup   3: 4096000 op, 664740645.00 ns, 162.2902 ns/op
WorkloadWarmup   4: 4096000 op, 668707340.00 ns, 163.2586 ns/op
WorkloadWarmup   5: 4096000 op, 657014719.00 ns, 160.4040 ns/op
WorkloadWarmup   6: 4096000 op, 663076509.00 ns, 161.8839 ns/op
WorkloadWarmup   7: 4096000 op, 664767744.00 ns, 162.2968 ns/op
WorkloadWarmup   8: 4096000 op, 658791595.00 ns, 160.8378 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 661360969.00 ns, 161.4651 ns/op
WorkloadActual   2: 4096000 op, 662787683.00 ns, 161.8134 ns/op
WorkloadActual   3: 4096000 op, 670467875.00 ns, 163.6884 ns/op
WorkloadActual   4: 4096000 op, 664315535.00 ns, 162.1864 ns/op
WorkloadActual   5: 4096000 op, 661576434.00 ns, 161.5177 ns/op
WorkloadActual   6: 4096000 op, 661835840.00 ns, 161.5810 ns/op
WorkloadActual   7: 4096000 op, 661263245.00 ns, 161.4412 ns/op
WorkloadActual   8: 4096000 op, 666760554.00 ns, 162.7833 ns/op
WorkloadActual   9: 4096000 op, 662702169.00 ns, 161.7925 ns/op
WorkloadActual  10: 4096000 op, 661538384.00 ns, 161.5084 ns/op
WorkloadActual  11: 4096000 op, 676653487.00 ns, 165.1986 ns/op
WorkloadActual  12: 4096000 op, 664118657.00 ns, 162.1383 ns/op
WorkloadActual  13: 4096000 op, 663565560.00 ns, 162.0033 ns/op
WorkloadActual  14: 4096000 op, 667044359.00 ns, 162.8526 ns/op
WorkloadActual  15: 4096000 op, 663863298.00 ns, 162.0760 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 661352413.00 ns, 161.4630 ns/op
WorkloadResult   2: 4096000 op, 662779127.00 ns, 161.8113 ns/op
WorkloadResult   3: 4096000 op, 670459319.00 ns, 163.6864 ns/op
WorkloadResult   4: 4096000 op, 664306979.00 ns, 162.1843 ns/op
WorkloadResult   5: 4096000 op, 661567878.00 ns, 161.5156 ns/op
WorkloadResult   6: 4096000 op, 661827284.00 ns, 161.5789 ns/op
WorkloadResult   7: 4096000 op, 661254689.00 ns, 161.4391 ns/op
WorkloadResult   8: 4096000 op, 666751998.00 ns, 162.7812 ns/op
WorkloadResult   9: 4096000 op, 662693613.00 ns, 161.7904 ns/op
WorkloadResult  10: 4096000 op, 661529828.00 ns, 161.5063 ns/op
WorkloadResult  11: 4096000 op, 664110101.00 ns, 162.1363 ns/op
WorkloadResult  12: 4096000 op, 663557004.00 ns, 162.0012 ns/op
WorkloadResult  13: 4096000 op, 667035803.00 ns, 162.8505 ns/op
WorkloadResult  14: 4096000 op, 663854742.00 ns, 162.0739 ns/op
// GC:  48 0 0 819200000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4670 has exited with code 0.

Mean = 162.058 ns, StdErr = 0.174 ns (0.11%), N = 14, StdDev = 0.652 ns
Min = 161.439 ns, Q1 = 161.531 ns, Median = 161.906 ns, Q3 = 162.172 ns, Max = 163.686 ns
IQR = 0.641 ns, LowerFence = 160.570 ns, UpperFence = 163.134 ns
ConfidenceInterval = [161.323 ns; 162.794 ns] (CI 99.9%), Margin = 0.735 ns (0.45% of Mean)
Skewness = 1.1, Kurtosis = 3.24, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-05 12:17 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 175971.00 ns, 175.9710 ns/op
WorkloadJitting  1: 1000 op, 1123829.00 ns, 1.1238 us/op

OverheadJitting  2: 16000 op, 180028.00 ns, 11.2518 ns/op
WorkloadJitting  2: 16000 op, 8447356.00 ns, 527.9598 ns/op

WorkloadPilot    1: 16000 op, 7301114.00 ns, 456.3196 ns/op
WorkloadPilot    2: 32000 op, 16875205.00 ns, 527.3502 ns/op
WorkloadPilot    3: 64000 op, 28429384.00 ns, 444.2091 ns/op
WorkloadPilot    4: 128000 op, 56375088.00 ns, 440.4304 ns/op
WorkloadPilot    5: 256000 op, 50450718.00 ns, 197.0731 ns/op
WorkloadPilot    6: 512000 op, 38998752.00 ns, 76.1694 ns/op
WorkloadPilot    7: 1024000 op, 73585393.00 ns, 71.8607 ns/op
WorkloadPilot    8: 2048000 op, 146419416.00 ns, 71.4939 ns/op
WorkloadPilot    9: 4096000 op, 292507428.00 ns, 71.4129 ns/op
WorkloadPilot   10: 8192000 op, 590020803.00 ns, 72.0240 ns/op

OverheadWarmup   1: 8192000 op, 24216.00 ns, 0.0030 ns/op
OverheadWarmup   2: 8192000 op, 20799.00 ns, 0.0025 ns/op
OverheadWarmup   3: 8192000 op, 20849.00 ns, 0.0025 ns/op
OverheadWarmup   4: 8192000 op, 20689.00 ns, 0.0025 ns/op
OverheadWarmup   5: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadWarmup   7: 8192000 op, 18785.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 21901.00 ns, 0.0027 ns/op
OverheadActual   3: 8192000 op, 18805.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18795.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18775.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 44283.00 ns, 0.0054 ns/op
OverheadActual  10: 8192000 op, 21991.00 ns, 0.0027 ns/op
OverheadActual  11: 8192000 op, 19126.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 19096.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 20799.00 ns, 0.0025 ns/op
OverheadActual  14: 8192000 op, 20709.00 ns, 0.0025 ns/op
OverheadActual  15: 8192000 op, 20659.00 ns, 0.0025 ns/op
OverheadActual  16: 8192000 op, 20639.00 ns, 0.0025 ns/op
OverheadActual  17: 8192000 op, 20929.00 ns, 0.0026 ns/op
OverheadActual  18: 8192000 op, 21810.00 ns, 0.0027 ns/op
OverheadActual  19: 8192000 op, 18836.00 ns, 0.0023 ns/op
OverheadActual  20: 8192000 op, 18766.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 598592702.00 ns, 73.0704 ns/op
WorkloadWarmup   2: 8192000 op, 593935903.00 ns, 72.5019 ns/op
WorkloadWarmup   3: 8192000 op, 586507037.00 ns, 71.5951 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 587802869.00 ns, 71.7533 ns/op
WorkloadActual   2: 8192000 op, 586712131.00 ns, 71.6201 ns/op
WorkloadActual   3: 8192000 op, 586485015.00 ns, 71.5924 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 587783758.00 ns, 71.7509 ns/op
WorkloadResult   2: 8192000 op, 586693020.00 ns, 71.6178 ns/op
WorkloadResult   3: 8192000 op, 586465904.00 ns, 71.5901 ns/op
// GC:  15 0 0 262144000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4682 has exited with code 0.

Mean = 71.653 ns, StdErr = 0.050 ns (0.07%), N = 3, StdDev = 0.086 ns
Min = 71.590 ns, Q1 = 71.604 ns, Median = 71.618 ns, Q3 = 71.684 ns, Max = 71.751 ns
IQR = 0.080 ns, LowerFence = 71.483 ns, UpperFence = 71.805 ns
ConfidenceInterval = [70.084 ns; 73.222 ns] (CI 99.9%), Margin = 1.569 ns (2.19% of Mean)
Skewness = 0.34, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-05 12:17 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 197040.00 ns, 197.0400 ns/op
WorkloadJitting  1: 1000 op, 1302345.00 ns, 1.3023 us/op

OverheadJitting  2: 16000 op, 197311.00 ns, 12.3319 ns/op
WorkloadJitting  2: 16000 op, 13539546.00 ns, 846.2216 ns/op

WorkloadPilot    1: 16000 op, 11980680.00 ns, 748.7925 ns/op
WorkloadPilot    2: 32000 op, 22293642.00 ns, 696.6763 ns/op
WorkloadPilot    3: 64000 op, 40538289.00 ns, 633.4108 ns/op
WorkloadPilot    4: 128000 op, 73920384.00 ns, 577.5030 ns/op
WorkloadPilot    5: 256000 op, 40935175.00 ns, 159.9030 ns/op
WorkloadPilot    6: 512000 op, 65120115.00 ns, 127.1877 ns/op
WorkloadPilot    7: 1024000 op, 129970945.00 ns, 126.9248 ns/op
WorkloadPilot    8: 2048000 op, 250657751.00 ns, 122.3915 ns/op
WorkloadPilot    9: 4096000 op, 487622678.00 ns, 119.0485 ns/op
WorkloadPilot   10: 8192000 op, 980227729.00 ns, 119.6567 ns/op

OverheadWarmup   1: 8192000 op, 23494.00 ns, 0.0029 ns/op
OverheadWarmup   2: 8192000 op, 18776.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18705.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18705.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18725.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18685.00 ns, 0.0023 ns/op
OverheadWarmup   7: 8192000 op, 18735.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadActual   2: 8192000 op, 22012.00 ns, 0.0027 ns/op
OverheadActual   3: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 19165.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 19236.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 19136.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 19136.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 19136.00 ns, 0.0023 ns/op
OverheadActual  10: 8192000 op, 21861.00 ns, 0.0027 ns/op
OverheadActual  11: 8192000 op, 19186.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 19226.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 20669.00 ns, 0.0025 ns/op
OverheadActual  14: 8192000 op, 20639.00 ns, 0.0025 ns/op
OverheadActual  15: 8192000 op, 19256.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 8192000 op, 985147935.00 ns, 120.2573 ns/op
WorkloadWarmup   2: 8192000 op, 994584991.00 ns, 121.4093 ns/op
WorkloadWarmup   3: 8192000 op, 974550740.00 ns, 118.9637 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 994895137.00 ns, 121.4472 ns/op
WorkloadActual   2: 8192000 op, 970082597.00 ns, 118.4183 ns/op
WorkloadActual   3: 8192000 op, 970759047.00 ns, 118.5009 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 994875951.00 ns, 121.4448 ns/op
WorkloadResult   2: 8192000 op, 970063411.00 ns, 118.4159 ns/op
WorkloadResult   3: 8192000 op, 970739861.00 ns, 118.4985 ns/op
// GC:  133 0 0 2228224000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4691 has exited with code 0.

Mean = 119.453 ns, StdErr = 0.996 ns (0.83%), N = 3, StdDev = 1.725 ns
Min = 118.416 ns, Q1 = 118.457 ns, Median = 118.499 ns, Q3 = 119.972 ns, Max = 121.445 ns
IQR = 1.514 ns, LowerFence = 116.186 ns, UpperFence = 122.243 ns
ConfidenceInterval = [87.976 ns; 150.930 ns] (CI 99.9%), Margin = 31.477 ns (26.35% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-05 12:17 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 172364.00 ns, 172.3640 ns/op
WorkloadJitting  1: 1000 op, 1198019.00 ns, 1.1980 us/op

OverheadJitting  2: 16000 op, 219492.00 ns, 13.7183 ns/op
WorkloadJitting  2: 16000 op, 10459204.00 ns, 653.7003 ns/op

WorkloadPilot    1: 16000 op, 9181255.00 ns, 573.8284 ns/op
WorkloadPilot    2: 32000 op, 17323229.00 ns, 541.3509 ns/op
WorkloadPilot    3: 64000 op, 33211535.00 ns, 518.9302 ns/op
WorkloadPilot    4: 128000 op, 68840711.00 ns, 537.8181 ns/op
WorkloadPilot    5: 256000 op, 40401393.00 ns, 157.8179 ns/op
WorkloadPilot    6: 512000 op, 45589011.00 ns, 89.0410 ns/op
WorkloadPilot    7: 1024000 op, 89931002.00 ns, 87.8232 ns/op
WorkloadPilot    8: 2048000 op, 179190524.00 ns, 87.4954 ns/op
WorkloadPilot    9: 4096000 op, 356594173.00 ns, 87.0591 ns/op
WorkloadPilot   10: 8192000 op, 728760778.00 ns, 88.9601 ns/op

OverheadWarmup   1: 8192000 op, 23003.00 ns, 0.0028 ns/op
OverheadWarmup   2: 8192000 op, 18916.00 ns, 0.0023 ns/op
OverheadWarmup   3: 8192000 op, 18715.00 ns, 0.0023 ns/op
OverheadWarmup   4: 8192000 op, 18896.00 ns, 0.0023 ns/op
OverheadWarmup   5: 8192000 op, 18776.00 ns, 0.0023 ns/op
OverheadWarmup   6: 8192000 op, 18715.00 ns, 0.0023 ns/op
OverheadWarmup   7: 8192000 op, 18755.00 ns, 0.0023 ns/op
OverheadWarmup   8: 8192000 op, 18745.00 ns, 0.0023 ns/op

OverheadActual   1: 8192000 op, 22012.00 ns, 0.0027 ns/op
OverheadActual   2: 8192000 op, 18815.00 ns, 0.0023 ns/op
OverheadActual   3: 8192000 op, 18806.00 ns, 0.0023 ns/op
OverheadActual   4: 8192000 op, 18785.00 ns, 0.0023 ns/op
OverheadActual   5: 8192000 op, 18746.00 ns, 0.0023 ns/op
OverheadActual   6: 8192000 op, 18725.00 ns, 0.0023 ns/op
OverheadActual   7: 8192000 op, 18735.00 ns, 0.0023 ns/op
OverheadActual   8: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual   9: 8192000 op, 21811.00 ns, 0.0027 ns/op
OverheadActual  10: 8192000 op, 18745.00 ns, 0.0023 ns/op
OverheadActual  11: 8192000 op, 18766.00 ns, 0.0023 ns/op
OverheadActual  12: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  13: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  14: 8192000 op, 18765.00 ns, 0.0023 ns/op
OverheadActual  15: 8192000 op, 18755.00 ns, 0.0023 ns/op

WorkloadWarmup   1: 8192000 op, 722318184.00 ns, 88.1736 ns/op
WorkloadWarmup   2: 8192000 op, 719949448.00 ns, 87.8845 ns/op
WorkloadWarmup   3: 8192000 op, 721050442.00 ns, 88.0189 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 726379847.00 ns, 88.6694 ns/op
WorkloadActual   2: 8192000 op, 724520849.00 ns, 88.4425 ns/op
WorkloadActual   3: 8192000 op, 728387407.00 ns, 88.9145 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 726361082.00 ns, 88.6671 ns/op
WorkloadResult   2: 8192000 op, 724502084.00 ns, 88.4402 ns/op
WorkloadResult   3: 8192000 op, 728368642.00 ns, 88.9122 ns/op
// GC:  50 0 0 851968000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4701 has exited with code 0.

Mean = 88.673 ns, StdErr = 0.136 ns (0.15%), N = 3, StdDev = 0.236 ns
Min = 88.440 ns, Q1 = 88.554 ns, Median = 88.667 ns, Q3 = 88.790 ns, Max = 88.912 ns
IQR = 0.236 ns, LowerFence = 88.200 ns, UpperFence = 89.144 ns
ConfidenceInterval = [84.367 ns; 92.980 ns] (CI 99.9%), Margin = 4.307 ns (4.86% of Mean)
Skewness = 0.03, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-05 12:17 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 182302.00 ns, 182.3020 ns/op
WorkloadJitting  1: 1000 op, 1756758.00 ns, 1.7568 us/op

OverheadJitting  2: 16000 op, 182934.00 ns, 11.4334 ns/op
WorkloadJitting  2: 16000 op, 26345586.00 ns, 1.6466 us/op

WorkloadPilot    1: 16000 op, 20452814.00 ns, 1.2783 us/op
WorkloadPilot    2: 32000 op, 37363068.00 ns, 1.1676 us/op
WorkloadPilot    3: 64000 op, 68240951.00 ns, 1.0663 us/op
WorkloadPilot    4: 128000 op, 106006855.00 ns, 828.1786 ns/op
WorkloadPilot    5: 256000 op, 50213421.00 ns, 196.1462 ns/op
WorkloadPilot    6: 512000 op, 90211645.00 ns, 176.1946 ns/op
WorkloadPilot    7: 1024000 op, 174846742.00 ns, 170.7488 ns/op
WorkloadPilot    8: 2048000 op, 352342406.00 ns, 172.0422 ns/op
WorkloadPilot    9: 4096000 op, 699586612.00 ns, 170.7975 ns/op

OverheadWarmup   1: 4096000 op, 12945.00 ns, 0.0032 ns/op
OverheadWarmup   2: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadWarmup   3: 4096000 op, 9627.00 ns, 0.0024 ns/op
OverheadWarmup   4: 4096000 op, 9629.00 ns, 0.0024 ns/op
OverheadWarmup   5: 4096000 op, 9678.00 ns, 0.0024 ns/op
OverheadWarmup   6: 4096000 op, 9627.00 ns, 0.0024 ns/op
OverheadWarmup   7: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadWarmup   8: 4096000 op, 9638.00 ns, 0.0024 ns/op

OverheadActual   1: 4096000 op, 9698.00 ns, 0.0024 ns/op
OverheadActual   2: 4096000 op, 9649.00 ns, 0.0024 ns/op
OverheadActual   3: 4096000 op, 9678.00 ns, 0.0024 ns/op
OverheadActual   4: 4096000 op, 9658.00 ns, 0.0024 ns/op
OverheadActual   5: 4096000 op, 9638.00 ns, 0.0024 ns/op
OverheadActual   6: 4096000 op, 9678.00 ns, 0.0024 ns/op
OverheadActual   7: 4096000 op, 9648.00 ns, 0.0024 ns/op
OverheadActual   8: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual   9: 4096000 op, 12203.00 ns, 0.0030 ns/op
OverheadActual  10: 4096000 op, 9628.00 ns, 0.0024 ns/op
OverheadActual  11: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  12: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  13: 4096000 op, 9598.00 ns, 0.0023 ns/op
OverheadActual  14: 4096000 op, 9618.00 ns, 0.0023 ns/op
OverheadActual  15: 4096000 op, 9628.00 ns, 0.0024 ns/op

WorkloadWarmup   1: 4096000 op, 711237703.00 ns, 173.6420 ns/op
WorkloadWarmup   2: 4096000 op, 703067607.00 ns, 171.6474 ns/op
WorkloadWarmup   3: 4096000 op, 697043559.00 ns, 170.1767 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 708148691.00 ns, 172.8879 ns/op
WorkloadActual   2: 4096000 op, 700614551.00 ns, 171.0485 ns/op
WorkloadActual   3: 4096000 op, 693379328.00 ns, 169.2821 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 708139053.00 ns, 172.8855 ns/op
WorkloadResult   2: 4096000 op, 700604913.00 ns, 171.0461 ns/op
WorkloadResult   3: 4096000 op, 693369690.00 ns, 169.2797 ns/op
// GC:  48 0 0 819200000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4711 has exited with code 0.

Mean = 171.070 ns, StdErr = 1.041 ns (0.61%), N = 3, StdDev = 1.803 ns
Min = 169.280 ns, Q1 = 170.163 ns, Median = 171.046 ns, Q3 = 171.966 ns, Max = 172.886 ns
IQR = 1.803 ns, LowerFence = 167.459 ns, UpperFence = 174.670 ns
ConfidenceInterval = [138.177 ns; 203.964 ns] (CI 99.9%), Margin = 32.894 ns (19.23% of Mean)
Skewness = 0.01, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-05 12:16 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 69.853 ns, StdErr = 0.025 ns (0.04%), N = 12, StdDev = 0.086 ns
Min = 69.680 ns, Q1 = 69.820 ns, Median = 69.865 ns, Q3 = 69.913 ns, Max = 69.965 ns
IQR = 0.093 ns, LowerFence = 69.681 ns, UpperFence = 70.053 ns
ConfidenceInterval = [69.743 ns; 69.963 ns] (CI 99.9%), Margin = 0.110 ns (0.16% of Mean)
Skewness = -0.68, Kurtosis = 2.26, MValue = 2
-------------------- Histogram --------------------
[69.631 ns ; 70.014 ns) | @@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 119.371 ns, StdErr = 0.432 ns (0.36%), N = 15, StdDev = 1.672 ns
Min = 117.677 ns, Q1 = 118.157 ns, Median = 118.917 ns, Q3 = 120.161 ns, Max = 123.057 ns
IQR = 2.004 ns, LowerFence = 115.150 ns, UpperFence = 123.168 ns
ConfidenceInterval = [117.584 ns; 121.158 ns] (CI 99.9%), Margin = 1.787 ns (1.50% of Mean)
Skewness = 0.95, Kurtosis = 2.71, MValue = 2
-------------------- Histogram --------------------
[117.191 ns ; 121.424 ns) | @@@@@@@@@@@@@
[121.424 ns ; 123.947 ns) | @@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 88.228 ns, StdErr = 0.223 ns (0.25%), N = 15, StdDev = 0.864 ns
Min = 87.011 ns, Q1 = 87.580 ns, Median = 88.225 ns, Q3 = 88.616 ns, Max = 89.643 ns
IQR = 1.036 ns, LowerFence = 86.026 ns, UpperFence = 90.170 ns
ConfidenceInterval = [87.304 ns; 89.151 ns] (CI 99.9%), Margin = 0.923 ns (1.05% of Mean)
Skewness = 0.25, Kurtosis = 1.72, MValue = 2
-------------------- Histogram --------------------
[86.552 ns ; 90.103 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 162.058 ns, StdErr = 0.174 ns (0.11%), N = 14, StdDev = 0.652 ns
Min = 161.439 ns, Q1 = 161.531 ns, Median = 161.906 ns, Q3 = 162.172 ns, Max = 163.686 ns
IQR = 0.641 ns, LowerFence = 160.570 ns, UpperFence = 163.134 ns
ConfidenceInterval = [161.323 ns; 162.794 ns] (CI 99.9%), Margin = 0.735 ns (0.45% of Mean)
Skewness = 1.1, Kurtosis = 3.24, MValue = 2
-------------------- Histogram --------------------
[161.084 ns ; 164.042 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 71.653 ns, StdErr = 0.050 ns (0.07%), N = 3, StdDev = 0.086 ns
Min = 71.590 ns, Q1 = 71.604 ns, Median = 71.618 ns, Q3 = 71.684 ns, Max = 71.751 ns
IQR = 0.080 ns, LowerFence = 71.483 ns, UpperFence = 71.805 ns
ConfidenceInterval = [70.084 ns; 73.222 ns] (CI 99.9%), Margin = 1.569 ns (2.19% of Mean)
Skewness = 0.34, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[71.512 ns ; 71.829 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 119.453 ns, StdErr = 0.996 ns (0.83%), N = 3, StdDev = 1.725 ns
Min = 118.416 ns, Q1 = 118.457 ns, Median = 118.499 ns, Q3 = 119.972 ns, Max = 121.445 ns
IQR = 1.514 ns, LowerFence = 116.186 ns, UpperFence = 122.243 ns
ConfidenceInterval = [87.976 ns; 150.930 ns] (CI 99.9%), Margin = 31.477 ns (26.35% of Mean)
Skewness = 0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[118.360 ns ; 121.501 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 88.673 ns, StdErr = 0.136 ns (0.15%), N = 3, StdDev = 0.236 ns
Min = 88.440 ns, Q1 = 88.554 ns, Median = 88.667 ns, Q3 = 88.790 ns, Max = 88.912 ns
IQR = 0.236 ns, LowerFence = 88.200 ns, UpperFence = 89.144 ns
ConfidenceInterval = [84.367 ns; 92.980 ns] (CI 99.9%), Margin = 4.307 ns (4.86% of Mean)
Skewness = 0.03, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[88.225 ns ; 89.127 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3; GC = Concurrent Workstation
Mean = 171.070 ns, StdErr = 1.041 ns (0.61%), N = 3, StdDev = 1.803 ns
Min = 169.280 ns, Q1 = 170.163 ns, Median = 171.046 ns, Q3 = 171.966 ns, Max = 172.886 ns
IQR = 1.803 ns, LowerFence = 167.459 ns, UpperFence = 174.670 ns
ConfidenceInterval = [138.177 ns; 203.964 ns] (CI 99.9%), Margin = 32.894 ns (19.23% of Mean)
Skewness = 0.01, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[168.461 ns ; 174.526 ns) | @@@
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
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  69.85 ns |  0.110 ns | 0.086 ns | 0.0018 |      32 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 119.37 ns |  1.787 ns | 1.672 ns | 0.0162 |     272 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  88.23 ns |  0.923 ns | 0.864 ns | 0.0061 |     104 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 162.06 ns |  0.735 ns | 0.652 ns | 0.0117 |     200 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  71.65 ns |  1.569 ns | 0.086 ns | 0.0018 |      32 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 119.45 ns | 31.477 ns | 1.725 ns | 0.0162 |     272 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  88.67 ns |  4.307 ns | 0.236 ns | 0.0061 |     104 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 171.07 ns | 32.894 ns | 1.803 ns | 0.0117 |     200 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput                        -> 3 outliers were removed, 4 outliers were detected (69.68 ns, 70.15 ns..70.21 ns)
  CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': RunStrategy=Throughput -> 1 outlier  was  removed (165.20 ns)
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
Run time: 00:01:53 (113.11 sec), executed benchmarks: 8

Global total time: 00:02:07 (127.51 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
