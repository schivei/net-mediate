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

Run: 2026-05-05 11:51 UTC | Branch: copilot/implementar-long-term | Commit: 21f35b0

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Platinum 8370C CPU 2.80GHz (Max: 3.39GHz), 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4


```
| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error    | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|---------:|---------:|-------:|----------:|
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  67.38 ns | 0.148 ns | 0.132 ns | 0.0012 |      32 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 131.80 ns | 0.532 ns | 0.472 ns | 0.0107 |     272 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  78.48 ns | 0.244 ns | 0.217 ns | 0.0040 |     104 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 172.59 ns | 0.198 ns | 0.165 ns | 0.0078 |     200 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           |  65.08 ns | 1.579 ns | 0.087 ns | 0.0012 |      32 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 138.08 ns | 4.573 ns | 0.251 ns | 0.0107 |     272 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           |  76.46 ns | 3.326 ns | 0.182 ns | 0.0040 |     104 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 177.31 ns | 3.162 ns | 0.173 ns | 0.0078 |     200 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.61 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 12.13 sec and exited with 0
// ***** Done, took 00:00:13 (13.8 sec)   *****
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

OverheadJitting  1: 1000 op, 175853.00 ns, 175.8530 ns/op
WorkloadJitting  1: 1000 op, 1083259.00 ns, 1.0833 us/op

OverheadJitting  2: 16000 op, 228757.00 ns, 14.2973 ns/op
WorkloadJitting  2: 16000 op, 5896477.00 ns, 368.5298 ns/op

WorkloadPilot    1: 16000 op, 4403126.00 ns, 275.1954 ns/op
WorkloadPilot    2: 32000 op, 8314824.00 ns, 259.8383 ns/op
WorkloadPilot    3: 64000 op, 16227893.00 ns, 253.5608 ns/op
WorkloadPilot    4: 128000 op, 34169740.00 ns, 266.9511 ns/op
WorkloadPilot    5: 256000 op, 67191111.00 ns, 262.4653 ns/op
WorkloadPilot    6: 512000 op, 52890662.00 ns, 103.3021 ns/op
WorkloadPilot    7: 1024000 op, 69222204.00 ns, 67.5998 ns/op
WorkloadPilot    8: 2048000 op, 138359207.00 ns, 67.5582 ns/op
WorkloadPilot    9: 4096000 op, 276360588.00 ns, 67.4708 ns/op
WorkloadPilot   10: 8192000 op, 551779998.00 ns, 67.3560 ns/op

OverheadWarmup   1: 8192000 op, 25008.00 ns, 0.0031 ns/op
OverheadWarmup   2: 8192000 op, 25545.00 ns, 0.0031 ns/op
OverheadWarmup   3: 8192000 op, 27204.00 ns, 0.0033 ns/op
OverheadWarmup   4: 8192000 op, 26950.00 ns, 0.0033 ns/op
OverheadWarmup   5: 8192000 op, 24737.00 ns, 0.0030 ns/op
OverheadWarmup   6: 8192000 op, 27666.00 ns, 0.0034 ns/op
OverheadWarmup   7: 8192000 op, 26160.00 ns, 0.0032 ns/op

OverheadActual   1: 8192000 op, 28056.00 ns, 0.0034 ns/op
OverheadActual   2: 8192000 op, 24407.00 ns, 0.0030 ns/op
OverheadActual   3: 8192000 op, 25818.00 ns, 0.0032 ns/op
OverheadActual   4: 8192000 op, 28117.00 ns, 0.0034 ns/op
OverheadActual   5: 8192000 op, 23220.00 ns, 0.0028 ns/op
OverheadActual   6: 8192000 op, 38640.00 ns, 0.0047 ns/op
OverheadActual   7: 8192000 op, 25851.00 ns, 0.0032 ns/op
OverheadActual   8: 8192000 op, 24643.00 ns, 0.0030 ns/op
OverheadActual   9: 8192000 op, 23147.00 ns, 0.0028 ns/op
OverheadActual  10: 8192000 op, 24397.00 ns, 0.0030 ns/op
OverheadActual  11: 8192000 op, 23165.00 ns, 0.0028 ns/op
OverheadActual  12: 8192000 op, 23183.00 ns, 0.0028 ns/op
OverheadActual  13: 8192000 op, 23208.00 ns, 0.0028 ns/op
OverheadActual  14: 8192000 op, 24079.00 ns, 0.0029 ns/op
OverheadActual  15: 8192000 op, 29479.00 ns, 0.0036 ns/op
OverheadActual  16: 8192000 op, 24404.00 ns, 0.0030 ns/op
OverheadActual  17: 8192000 op, 24377.00 ns, 0.0030 ns/op
OverheadActual  18: 8192000 op, 28560.00 ns, 0.0035 ns/op
OverheadActual  19: 8192000 op, 27331.00 ns, 0.0033 ns/op
OverheadActual  20: 8192000 op, 27005.00 ns, 0.0033 ns/op

WorkloadWarmup   1: 8192000 op, 567073877.00 ns, 69.2229 ns/op
WorkloadWarmup   2: 8192000 op, 574779420.00 ns, 70.1635 ns/op
WorkloadWarmup   3: 8192000 op, 552534586.00 ns, 67.4481 ns/op
WorkloadWarmup   4: 8192000 op, 551284358.00 ns, 67.2955 ns/op
WorkloadWarmup   5: 8192000 op, 552989678.00 ns, 67.5036 ns/op
WorkloadWarmup   6: 8192000 op, 551433715.00 ns, 67.3137 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 554390354.00 ns, 67.6746 ns/op
WorkloadActual   2: 8192000 op, 551621349.00 ns, 67.3366 ns/op
WorkloadActual   3: 8192000 op, 552883530.00 ns, 67.4907 ns/op
WorkloadActual   4: 8192000 op, 553557396.00 ns, 67.5729 ns/op
WorkloadActual   5: 8192000 op, 551325541.00 ns, 67.3005 ns/op
WorkloadActual   6: 8192000 op, 551002536.00 ns, 67.2611 ns/op
WorkloadActual   7: 8192000 op, 552748119.00 ns, 67.4741 ns/op
WorkloadActual   8: 8192000 op, 551512160.00 ns, 67.3233 ns/op
WorkloadActual   9: 8192000 op, 551703867.00 ns, 67.3467 ns/op
WorkloadActual  10: 8192000 op, 559777420.00 ns, 68.3322 ns/op
WorkloadActual  11: 8192000 op, 551063578.00 ns, 67.2685 ns/op
WorkloadActual  12: 8192000 op, 551827136.00 ns, 67.3617 ns/op
WorkloadActual  13: 8192000 op, 550625360.00 ns, 67.2150 ns/op
WorkloadActual  14: 8192000 op, 552325375.00 ns, 67.4225 ns/op
WorkloadActual  15: 8192000 op, 551098271.00 ns, 67.2727 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 554365829.00 ns, 67.6716 ns/op
WorkloadResult   2: 8192000 op, 551596824.00 ns, 67.3336 ns/op
WorkloadResult   3: 8192000 op, 552859005.00 ns, 67.4877 ns/op
WorkloadResult   4: 8192000 op, 553532871.00 ns, 67.5699 ns/op
WorkloadResult   5: 8192000 op, 551301016.00 ns, 67.2975 ns/op
WorkloadResult   6: 8192000 op, 550978011.00 ns, 67.2581 ns/op
WorkloadResult   7: 8192000 op, 552723594.00 ns, 67.4711 ns/op
WorkloadResult   8: 8192000 op, 551487635.00 ns, 67.3203 ns/op
WorkloadResult   9: 8192000 op, 551679342.00 ns, 67.3437 ns/op
WorkloadResult  10: 8192000 op, 551039053.00 ns, 67.2655 ns/op
WorkloadResult  11: 8192000 op, 551802611.00 ns, 67.3587 ns/op
WorkloadResult  12: 8192000 op, 550600835.00 ns, 67.2120 ns/op
WorkloadResult  13: 8192000 op, 552300850.00 ns, 67.4195 ns/op
WorkloadResult  14: 8192000 op, 551073746.00 ns, 67.2697 ns/op
// GC:  10 0 0 262144000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4683 has exited with code 0.

Mean = 67.377 ns, StdErr = 0.035 ns (0.05%), N = 14, StdDev = 0.132 ns
Min = 67.212 ns, Q1 = 67.277 ns, Median = 67.339 ns, Q3 = 67.458 ns, Max = 67.672 ns
IQR = 0.182 ns, LowerFence = 67.004 ns, UpperFence = 67.731 ns
ConfidenceInterval = [67.229 ns; 67.526 ns] (CI 99.9%), Margin = 0.148 ns (0.22% of Mean)
Skewness = 0.8, Kurtosis = 2.48, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-05 11:51 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 196149.00 ns, 196.1490 ns/op
WorkloadJitting  1: 1000 op, 1196374.00 ns, 1.1964 us/op

OverheadJitting  2: 16000 op, 232084.00 ns, 14.5053 ns/op
WorkloadJitting  2: 16000 op, 9909973.00 ns, 619.3733 ns/op

WorkloadPilot    1: 16000 op, 8259114.00 ns, 516.1946 ns/op
WorkloadPilot    2: 32000 op, 15293003.00 ns, 477.9063 ns/op
WorkloadPilot    3: 64000 op, 28897457.00 ns, 451.5228 ns/op
WorkloadPilot    4: 128000 op, 53505480.00 ns, 418.0116 ns/op
WorkloadPilot    5: 256000 op, 67910341.00 ns, 265.2748 ns/op
WorkloadPilot    6: 512000 op, 67205010.00 ns, 131.2598 ns/op
WorkloadPilot    7: 1024000 op, 134428318.00 ns, 131.2777 ns/op
WorkloadPilot    8: 2048000 op, 268494456.00 ns, 131.1008 ns/op
WorkloadPilot    9: 4096000 op, 536621470.00 ns, 131.0111 ns/op

OverheadWarmup   1: 4096000 op, 13798.00 ns, 0.0034 ns/op
OverheadWarmup   2: 4096000 op, 11905.00 ns, 0.0029 ns/op
OverheadWarmup   3: 4096000 op, 11847.00 ns, 0.0029 ns/op
OverheadWarmup   4: 4096000 op, 11774.00 ns, 0.0029 ns/op
OverheadWarmup   5: 4096000 op, 11784.00 ns, 0.0029 ns/op
OverheadWarmup   6: 4096000 op, 11817.00 ns, 0.0029 ns/op
OverheadWarmup   7: 4096000 op, 11807.00 ns, 0.0029 ns/op
OverheadWarmup   8: 4096000 op, 11818.00 ns, 0.0029 ns/op
OverheadWarmup   9: 4096000 op, 11840.00 ns, 0.0029 ns/op
OverheadWarmup  10: 4096000 op, 11855.00 ns, 0.0029 ns/op

OverheadActual   1: 4096000 op, 11856.00 ns, 0.0029 ns/op
OverheadActual   2: 4096000 op, 11944.00 ns, 0.0029 ns/op
OverheadActual   3: 4096000 op, 11958.00 ns, 0.0029 ns/op
OverheadActual   4: 4096000 op, 11852.00 ns, 0.0029 ns/op
OverheadActual   5: 4096000 op, 11898.00 ns, 0.0029 ns/op
OverheadActual   6: 4096000 op, 11754.00 ns, 0.0029 ns/op
OverheadActual   7: 4096000 op, 13238.00 ns, 0.0032 ns/op
OverheadActual   8: 4096000 op, 11684.00 ns, 0.0029 ns/op
OverheadActual   9: 4096000 op, 11800.00 ns, 0.0029 ns/op
OverheadActual  10: 4096000 op, 11829.00 ns, 0.0029 ns/op
OverheadActual  11: 4096000 op, 11796.00 ns, 0.0029 ns/op
OverheadActual  12: 4096000 op, 11823.00 ns, 0.0029 ns/op
OverheadActual  13: 4096000 op, 11460.00 ns, 0.0028 ns/op
OverheadActual  14: 4096000 op, 14070.00 ns, 0.0034 ns/op
OverheadActual  15: 4096000 op, 14277.00 ns, 0.0035 ns/op

WorkloadWarmup   1: 4096000 op, 563854490.00 ns, 137.6598 ns/op
WorkloadWarmup   2: 4096000 op, 550146196.00 ns, 134.3130 ns/op
WorkloadWarmup   3: 4096000 op, 539914236.00 ns, 131.8150 ns/op
WorkloadWarmup   4: 4096000 op, 537941000.00 ns, 131.3333 ns/op
WorkloadWarmup   5: 4096000 op, 538703069.00 ns, 131.5193 ns/op
WorkloadWarmup   6: 4096000 op, 539192143.00 ns, 131.6387 ns/op
WorkloadWarmup   7: 4096000 op, 539251846.00 ns, 131.6533 ns/op
WorkloadWarmup   8: 4096000 op, 536596214.00 ns, 131.0049 ns/op
WorkloadWarmup   9: 4096000 op, 541447367.00 ns, 132.1893 ns/op
WorkloadWarmup  10: 4096000 op, 540475936.00 ns, 131.9521 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 542610733.00 ns, 132.4733 ns/op
WorkloadActual   2: 4096000 op, 541849187.00 ns, 132.2874 ns/op
WorkloadActual   3: 4096000 op, 542905150.00 ns, 132.5452 ns/op
WorkloadActual   4: 4096000 op, 541250115.00 ns, 132.1411 ns/op
WorkloadActual   5: 4096000 op, 538597575.00 ns, 131.4935 ns/op
WorkloadActual   6: 4096000 op, 546873664.00 ns, 133.5141 ns/op
WorkloadActual   7: 4096000 op, 538039481.00 ns, 131.3573 ns/op
WorkloadActual   8: 4096000 op, 540300618.00 ns, 131.9093 ns/op
WorkloadActual   9: 4096000 op, 539123884.00 ns, 131.6220 ns/op
WorkloadActual  10: 4096000 op, 538136508.00 ns, 131.3810 ns/op
WorkloadActual  11: 4096000 op, 537002941.00 ns, 131.1042 ns/op
WorkloadActual  12: 4096000 op, 537186746.00 ns, 131.1491 ns/op
WorkloadActual  13: 4096000 op, 540830016.00 ns, 132.0386 ns/op
WorkloadActual  14: 4096000 op, 539332615.00 ns, 131.6730 ns/op
WorkloadActual  15: 4096000 op, 540713794.00 ns, 132.0102 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 542598881.00 ns, 132.4704 ns/op
WorkloadResult   2: 4096000 op, 541837335.00 ns, 132.2845 ns/op
WorkloadResult   3: 4096000 op, 542893298.00 ns, 132.5423 ns/op
WorkloadResult   4: 4096000 op, 541238263.00 ns, 132.1382 ns/op
WorkloadResult   5: 4096000 op, 538585723.00 ns, 131.4907 ns/op
WorkloadResult   6: 4096000 op, 538027629.00 ns, 131.3544 ns/op
WorkloadResult   7: 4096000 op, 540288766.00 ns, 131.9064 ns/op
WorkloadResult   8: 4096000 op, 539112032.00 ns, 131.6191 ns/op
WorkloadResult   9: 4096000 op, 538124656.00 ns, 131.3781 ns/op
WorkloadResult  10: 4096000 op, 536991089.00 ns, 131.1013 ns/op
WorkloadResult  11: 4096000 op, 537174894.00 ns, 131.1462 ns/op
WorkloadResult  12: 4096000 op, 540818164.00 ns, 132.0357 ns/op
WorkloadResult  13: 4096000 op, 539320763.00 ns, 131.6701 ns/op
WorkloadResult  14: 4096000 op, 540701942.00 ns, 132.0073 ns/op
// GC:  44 0 0 1114112000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4694 has exited with code 0.

Mean = 131.796 ns, StdErr = 0.126 ns (0.10%), N = 14, StdDev = 0.472 ns
Min = 131.101 ns, Q1 = 131.406 ns, Median = 131.788 ns, Q3 = 132.113 ns, Max = 132.542 ns
IQR = 0.706 ns, LowerFence = 130.347 ns, UpperFence = 133.172 ns
ConfidenceInterval = [131.264 ns; 132.329 ns] (CI 99.9%), Margin = 0.532 ns (0.40% of Mean)
Skewness = 0.07, Kurtosis = 1.56, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-05 11:51 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 179319.00 ns, 179.3190 ns/op
WorkloadJitting  1: 1000 op, 1173221.00 ns, 1.1732 us/op

OverheadJitting  2: 16000 op, 239575.00 ns, 14.9734 ns/op
WorkloadJitting  2: 16000 op, 7924514.00 ns, 495.2821 ns/op

WorkloadPilot    1: 16000 op, 6449905.00 ns, 403.1191 ns/op
WorkloadPilot    2: 32000 op, 11659701.00 ns, 364.3657 ns/op
WorkloadPilot    3: 64000 op, 23372675.00 ns, 365.1980 ns/op
WorkloadPilot    4: 128000 op, 48302299.00 ns, 377.3617 ns/op
WorkloadPilot    5: 256000 op, 74448099.00 ns, 290.8129 ns/op
WorkloadPilot    6: 512000 op, 43059678.00 ns, 84.1009 ns/op
WorkloadPilot    7: 1024000 op, 80663640.00 ns, 78.7731 ns/op
WorkloadPilot    8: 2048000 op, 171574916.00 ns, 83.7768 ns/op
WorkloadPilot    9: 4096000 op, 323888963.00 ns, 79.0745 ns/op
WorkloadPilot   10: 8192000 op, 643672080.00 ns, 78.5733 ns/op

OverheadWarmup   1: 8192000 op, 25314.00 ns, 0.0031 ns/op
OverheadWarmup   2: 8192000 op, 23264.00 ns, 0.0028 ns/op
OverheadWarmup   3: 8192000 op, 27161.00 ns, 0.0033 ns/op
OverheadWarmup   4: 8192000 op, 26369.00 ns, 0.0032 ns/op
OverheadWarmup   5: 8192000 op, 27838.00 ns, 0.0034 ns/op
OverheadWarmup   6: 8192000 op, 26656.00 ns, 0.0033 ns/op

OverheadActual   1: 8192000 op, 26946.00 ns, 0.0033 ns/op
OverheadActual   2: 8192000 op, 27414.00 ns, 0.0033 ns/op
OverheadActual   3: 8192000 op, 29199.00 ns, 0.0036 ns/op
OverheadActual   4: 8192000 op, 27224.00 ns, 0.0033 ns/op
OverheadActual   5: 8192000 op, 26672.00 ns, 0.0033 ns/op
OverheadActual   6: 8192000 op, 26991.00 ns, 0.0033 ns/op
OverheadActual   7: 8192000 op, 26774.00 ns, 0.0033 ns/op
OverheadActual   8: 8192000 op, 26854.00 ns, 0.0033 ns/op
OverheadActual   9: 8192000 op, 27155.00 ns, 0.0033 ns/op
OverheadActual  10: 8192000 op, 23238.00 ns, 0.0028 ns/op
OverheadActual  11: 8192000 op, 24355.00 ns, 0.0030 ns/op
OverheadActual  12: 8192000 op, 23146.00 ns, 0.0028 ns/op
OverheadActual  13: 8192000 op, 23191.00 ns, 0.0028 ns/op
OverheadActual  14: 8192000 op, 23173.00 ns, 0.0028 ns/op
OverheadActual  15: 8192000 op, 23190.00 ns, 0.0028 ns/op
OverheadActual  16: 8192000 op, 22568.00 ns, 0.0028 ns/op
OverheadActual  17: 8192000 op, 23132.00 ns, 0.0028 ns/op
OverheadActual  18: 8192000 op, 23218.00 ns, 0.0028 ns/op
OverheadActual  19: 8192000 op, 23841.00 ns, 0.0029 ns/op
OverheadActual  20: 8192000 op, 23124.00 ns, 0.0028 ns/op

WorkloadWarmup   1: 8192000 op, 650824390.00 ns, 79.4463 ns/op
WorkloadWarmup   2: 8192000 op, 654163655.00 ns, 79.8540 ns/op
WorkloadWarmup   3: 8192000 op, 641002415.00 ns, 78.2474 ns/op
WorkloadWarmup   4: 8192000 op, 638274862.00 ns, 77.9144 ns/op
WorkloadWarmup   5: 8192000 op, 637894798.00 ns, 77.8680 ns/op
WorkloadWarmup   6: 8192000 op, 637752430.00 ns, 77.8506 ns/op
WorkloadWarmup   7: 8192000 op, 637691153.00 ns, 77.8432 ns/op
WorkloadWarmup   8: 8192000 op, 638360795.00 ns, 77.9249 ns/op
WorkloadWarmup   9: 8192000 op, 638087350.00 ns, 77.8915 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 641126152.00 ns, 78.2625 ns/op
WorkloadActual   2: 8192000 op, 640747533.00 ns, 78.2163 ns/op
WorkloadActual   3: 8192000 op, 641153979.00 ns, 78.2659 ns/op
WorkloadActual   4: 8192000 op, 643785756.00 ns, 78.5871 ns/op
WorkloadActual   5: 8192000 op, 641589283.00 ns, 78.3190 ns/op
WorkloadActual   6: 8192000 op, 645403289.00 ns, 78.7846 ns/op
WorkloadActual   7: 8192000 op, 643149713.00 ns, 78.5095 ns/op
WorkloadActual   8: 8192000 op, 642112811.00 ns, 78.3829 ns/op
WorkloadActual   9: 8192000 op, 644630038.00 ns, 78.6902 ns/op
WorkloadActual  10: 8192000 op, 643416338.00 ns, 78.5420 ns/op
WorkloadActual  11: 8192000 op, 644465591.00 ns, 78.6701 ns/op
WorkloadActual  12: 8192000 op, 641877642.00 ns, 78.3542 ns/op
WorkloadActual  13: 8192000 op, 640957299.00 ns, 78.2419 ns/op
WorkloadActual  14: 8192000 op, 646076949.00 ns, 78.8668 ns/op
WorkloadActual  15: 8192000 op, 654966076.00 ns, 79.9519 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 641102054.00 ns, 78.2595 ns/op
WorkloadResult   2: 8192000 op, 640723435.00 ns, 78.2133 ns/op
WorkloadResult   3: 8192000 op, 641129881.00 ns, 78.2629 ns/op
WorkloadResult   4: 8192000 op, 643761658.00 ns, 78.5842 ns/op
WorkloadResult   5: 8192000 op, 641565185.00 ns, 78.3161 ns/op
WorkloadResult   6: 8192000 op, 645379191.00 ns, 78.7816 ns/op
WorkloadResult   7: 8192000 op, 643125615.00 ns, 78.5065 ns/op
WorkloadResult   8: 8192000 op, 642088713.00 ns, 78.3800 ns/op
WorkloadResult   9: 8192000 op, 644605940.00 ns, 78.6872 ns/op
WorkloadResult  10: 8192000 op, 643392240.00 ns, 78.5391 ns/op
WorkloadResult  11: 8192000 op, 644441493.00 ns, 78.6672 ns/op
WorkloadResult  12: 8192000 op, 641853544.00 ns, 78.3513 ns/op
WorkloadResult  13: 8192000 op, 640933201.00 ns, 78.2389 ns/op
WorkloadResult  14: 8192000 op, 646052851.00 ns, 78.8639 ns/op
// GC:  33 0 0 851968000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4704 has exited with code 0.

Mean = 78.475 ns, StdErr = 0.058 ns (0.07%), N = 14, StdDev = 0.217 ns
Min = 78.213 ns, Q1 = 78.276 ns, Median = 78.443 ns, Q3 = 78.646 ns, Max = 78.864 ns
IQR = 0.370 ns, LowerFence = 77.721 ns, UpperFence = 79.202 ns
ConfidenceInterval = [78.231 ns; 78.719 ns] (CI 99.9%), Margin = 0.244 ns (0.31% of Mean)
Skewness = 0.34, Kurtosis = 1.57, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-05 11:51 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 198559.00 ns, 198.5590 ns/op
WorkloadJitting  1: 1000 op, 1536538.00 ns, 1.5365 us/op

OverheadJitting  2: 16000 op, 212210.00 ns, 13.2631 ns/op
WorkloadJitting  2: 16000 op, 15480330.00 ns, 967.5206 ns/op

WorkloadPilot    1: 16000 op, 12932414.00 ns, 808.2759 ns/op
WorkloadPilot    2: 32000 op, 24137850.00 ns, 754.3078 ns/op
WorkloadPilot    3: 64000 op, 47199686.00 ns, 737.4951 ns/op
WorkloadPilot    4: 128000 op, 78229807.00 ns, 611.1704 ns/op
WorkloadPilot    5: 256000 op, 50041875.00 ns, 195.4761 ns/op
WorkloadPilot    6: 512000 op, 93306146.00 ns, 182.2386 ns/op
WorkloadPilot    7: 1024000 op, 176963330.00 ns, 172.8158 ns/op
WorkloadPilot    8: 2048000 op, 353945139.00 ns, 172.8248 ns/op
WorkloadPilot    9: 4096000 op, 707338819.00 ns, 172.6901 ns/op

OverheadWarmup   1: 4096000 op, 14022.00 ns, 0.0034 ns/op
OverheadWarmup   2: 4096000 op, 11914.00 ns, 0.0029 ns/op
OverheadWarmup   3: 4096000 op, 11863.00 ns, 0.0029 ns/op
OverheadWarmup   4: 4096000 op, 11788.00 ns, 0.0029 ns/op
OverheadWarmup   5: 4096000 op, 11903.00 ns, 0.0029 ns/op
OverheadWarmup   6: 4096000 op, 11872.00 ns, 0.0029 ns/op
OverheadWarmup   7: 4096000 op, 11817.00 ns, 0.0029 ns/op
OverheadWarmup   8: 4096000 op, 11862.00 ns, 0.0029 ns/op
OverheadWarmup   9: 4096000 op, 12111.00 ns, 0.0030 ns/op
OverheadWarmup  10: 4096000 op, 11855.00 ns, 0.0029 ns/op

OverheadActual   1: 4096000 op, 11918.00 ns, 0.0029 ns/op
OverheadActual   2: 4096000 op, 11981.00 ns, 0.0029 ns/op
OverheadActual   3: 4096000 op, 12003.00 ns, 0.0029 ns/op
OverheadActual   4: 4096000 op, 11890.00 ns, 0.0029 ns/op
OverheadActual   5: 4096000 op, 11915.00 ns, 0.0029 ns/op
OverheadActual   6: 4096000 op, 11852.00 ns, 0.0029 ns/op
OverheadActual   7: 4096000 op, 13273.00 ns, 0.0032 ns/op
OverheadActual   8: 4096000 op, 11916.00 ns, 0.0029 ns/op
OverheadActual   9: 4096000 op, 11869.00 ns, 0.0029 ns/op
OverheadActual  10: 4096000 op, 11746.00 ns, 0.0029 ns/op
OverheadActual  11: 4096000 op, 11794.00 ns, 0.0029 ns/op
OverheadActual  12: 4096000 op, 11789.00 ns, 0.0029 ns/op
OverheadActual  13: 4096000 op, 11840.00 ns, 0.0029 ns/op
OverheadActual  14: 4096000 op, 11885.00 ns, 0.0029 ns/op
OverheadActual  15: 4096000 op, 11939.00 ns, 0.0029 ns/op

WorkloadWarmup   1: 4096000 op, 716026834.00 ns, 174.8112 ns/op
WorkloadWarmup   2: 4096000 op, 716703015.00 ns, 174.9763 ns/op
WorkloadWarmup   3: 4096000 op, 706373087.00 ns, 172.4544 ns/op
WorkloadWarmup   4: 4096000 op, 704749886.00 ns, 172.0581 ns/op
WorkloadWarmup   5: 4096000 op, 705809592.00 ns, 172.3168 ns/op
WorkloadWarmup   6: 4096000 op, 707125255.00 ns, 172.6380 ns/op
WorkloadWarmup   7: 4096000 op, 706902772.00 ns, 172.5837 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 710156230.00 ns, 173.3780 ns/op
WorkloadActual   2: 4096000 op, 706414771.00 ns, 172.4645 ns/op
WorkloadActual   3: 4096000 op, 706448585.00 ns, 172.4728 ns/op
WorkloadActual   4: 4096000 op, 706237471.00 ns, 172.4213 ns/op
WorkloadActual   5: 4096000 op, 706230229.00 ns, 172.4195 ns/op
WorkloadActual   6: 4096000 op, 706897533.00 ns, 172.5824 ns/op
WorkloadActual   7: 4096000 op, 706619517.00 ns, 172.5145 ns/op
WorkloadActual   8: 4096000 op, 707183498.00 ns, 172.6522 ns/op
WorkloadActual   9: 4096000 op, 710508796.00 ns, 173.4641 ns/op
WorkloadActual  10: 4096000 op, 708474634.00 ns, 172.9674 ns/op
WorkloadActual  11: 4096000 op, 707200962.00 ns, 172.6565 ns/op
WorkloadActual  12: 4096000 op, 706473292.00 ns, 172.4788 ns/op
WorkloadActual  13: 4096000 op, 706826992.00 ns, 172.5652 ns/op
WorkloadActual  14: 4096000 op, 707432401.00 ns, 172.7130 ns/op
WorkloadActual  15: 4096000 op, 707901520.00 ns, 172.8275 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 706402881.00 ns, 172.4616 ns/op
WorkloadResult   2: 4096000 op, 706436695.00 ns, 172.4699 ns/op
WorkloadResult   3: 4096000 op, 706225581.00 ns, 172.4184 ns/op
WorkloadResult   4: 4096000 op, 706218339.00 ns, 172.4166 ns/op
WorkloadResult   5: 4096000 op, 706885643.00 ns, 172.5795 ns/op
WorkloadResult   6: 4096000 op, 706607627.00 ns, 172.5116 ns/op
WorkloadResult   7: 4096000 op, 707171608.00 ns, 172.6493 ns/op
WorkloadResult   8: 4096000 op, 708462744.00 ns, 172.9645 ns/op
WorkloadResult   9: 4096000 op, 707189072.00 ns, 172.6536 ns/op
WorkloadResult  10: 4096000 op, 706461402.00 ns, 172.4759 ns/op
WorkloadResult  11: 4096000 op, 706815102.00 ns, 172.5623 ns/op
WorkloadResult  12: 4096000 op, 707420511.00 ns, 172.7101 ns/op
WorkloadResult  13: 4096000 op, 707889630.00 ns, 172.8246 ns/op
// GC:  32 0 0 819200000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4718 has exited with code 0.

Mean = 172.592 ns, StdErr = 0.046 ns (0.03%), N = 13, StdDev = 0.165 ns
Min = 172.417 ns, Q1 = 172.470 ns, Median = 172.562 ns, Q3 = 172.654 ns, Max = 172.965 ns
IQR = 0.184 ns, LowerFence = 172.194 ns, UpperFence = 172.929 ns
ConfidenceInterval = [172.394 ns; 172.790 ns] (CI 99.9%), Margin = 0.198 ns (0.11% of Mean)
Skewness = 0.84, Kurtosis = 2.58, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-05 11:51 (0h 1m from now) **
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

OverheadJitting  1: 1000 op, 190097.00 ns, 190.0970 ns/op
WorkloadJitting  1: 1000 op, 1055767.00 ns, 1.0558 us/op

OverheadJitting  2: 16000 op, 255992.00 ns, 15.9995 ns/op
WorkloadJitting  2: 16000 op, 5737779.00 ns, 358.6112 ns/op

WorkloadPilot    1: 16000 op, 4370999.00 ns, 273.1874 ns/op
WorkloadPilot    2: 32000 op, 8364822.00 ns, 261.4007 ns/op
WorkloadPilot    3: 64000 op, 16630217.00 ns, 259.8471 ns/op
WorkloadPilot    4: 128000 op, 33555210.00 ns, 262.1501 ns/op
WorkloadPilot    5: 256000 op, 79804535.00 ns, 311.7365 ns/op
WorkloadPilot    6: 512000 op, 39793166.00 ns, 77.7210 ns/op
WorkloadPilot    7: 1024000 op, 67134470.00 ns, 65.5610 ns/op
WorkloadPilot    8: 2048000 op, 133137513.00 ns, 65.0086 ns/op
WorkloadPilot    9: 4096000 op, 266367714.00 ns, 65.0312 ns/op
WorkloadPilot   10: 8192000 op, 532777590.00 ns, 65.0363 ns/op

OverheadWarmup   1: 8192000 op, 27704.00 ns, 0.0034 ns/op
OverheadWarmup   2: 8192000 op, 24975.00 ns, 0.0030 ns/op
OverheadWarmup   3: 8192000 op, 27878.00 ns, 0.0034 ns/op
OverheadWarmup   4: 8192000 op, 28239.00 ns, 0.0034 ns/op
OverheadWarmup   5: 8192000 op, 27620.00 ns, 0.0034 ns/op
OverheadWarmup   6: 8192000 op, 28795.00 ns, 0.0035 ns/op
OverheadWarmup   7: 8192000 op, 25469.00 ns, 0.0031 ns/op

OverheadActual   1: 8192000 op, 25653.00 ns, 0.0031 ns/op
OverheadActual   2: 8192000 op, 47175.00 ns, 0.0058 ns/op
OverheadActual   3: 8192000 op, 25677.00 ns, 0.0031 ns/op
OverheadActual   4: 8192000 op, 24809.00 ns, 0.0030 ns/op
OverheadActual   5: 8192000 op, 25573.00 ns, 0.0031 ns/op
OverheadActual   6: 8192000 op, 25497.00 ns, 0.0031 ns/op
OverheadActual   7: 8192000 op, 25519.00 ns, 0.0031 ns/op
OverheadActual   8: 8192000 op, 25562.00 ns, 0.0031 ns/op
OverheadActual   9: 8192000 op, 25510.00 ns, 0.0031 ns/op
OverheadActual  10: 8192000 op, 29373.00 ns, 0.0036 ns/op
OverheadActual  11: 8192000 op, 27674.00 ns, 0.0034 ns/op
OverheadActual  12: 8192000 op, 28184.00 ns, 0.0034 ns/op
OverheadActual  13: 8192000 op, 27087.00 ns, 0.0033 ns/op
OverheadActual  14: 8192000 op, 28222.00 ns, 0.0034 ns/op
OverheadActual  15: 8192000 op, 28136.00 ns, 0.0034 ns/op
OverheadActual  16: 8192000 op, 27822.00 ns, 0.0034 ns/op
OverheadActual  17: 8192000 op, 31352.00 ns, 0.0038 ns/op
OverheadActual  18: 8192000 op, 28893.00 ns, 0.0035 ns/op
OverheadActual  19: 8192000 op, 29449.00 ns, 0.0036 ns/op
OverheadActual  20: 8192000 op, 27516.00 ns, 0.0034 ns/op

WorkloadWarmup   1: 8192000 op, 541918150.00 ns, 66.1521 ns/op
WorkloadWarmup   2: 8192000 op, 540820517.00 ns, 66.0181 ns/op
WorkloadWarmup   3: 8192000 op, 533465807.00 ns, 65.1203 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 533967969.00 ns, 65.1816 ns/op
WorkloadActual   2: 8192000 op, 532623985.00 ns, 65.0176 ns/op
WorkloadActual   3: 8192000 op, 532903928.00 ns, 65.0517 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 533940374.00 ns, 65.1783 ns/op
WorkloadResult   2: 8192000 op, 532596390.00 ns, 65.0142 ns/op
WorkloadResult   3: 8192000 op, 532876333.00 ns, 65.0484 ns/op
// GC:  10 0 0 262144000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4732 has exited with code 0.

Mean = 65.080 ns, StdErr = 0.050 ns (0.08%), N = 3, StdDev = 0.087 ns
Min = 65.014 ns, Q1 = 65.031 ns, Median = 65.048 ns, Q3 = 65.113 ns, Max = 65.178 ns
IQR = 0.082 ns, LowerFence = 64.908 ns, UpperFence = 65.236 ns
ConfidenceInterval = [63.501 ns; 66.659 ns] (CI 99.9%), Margin = 1.579 ns (2.43% of Mean)
Skewness = 0.32, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-05 11:51 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 174593.00 ns, 174.5930 ns/op
WorkloadJitting  1: 1000 op, 1231650.00 ns, 1.2317 us/op

OverheadJitting  2: 16000 op, 227672.00 ns, 14.2295 ns/op
WorkloadJitting  2: 16000 op, 9885992.00 ns, 617.8745 ns/op

WorkloadPilot    1: 16000 op, 8051265.00 ns, 503.2041 ns/op
WorkloadPilot    2: 32000 op, 16403113.00 ns, 512.5973 ns/op
WorkloadPilot    3: 64000 op, 34829524.00 ns, 544.2113 ns/op
WorkloadPilot    4: 128000 op, 50598561.00 ns, 395.3013 ns/op
WorkloadPilot    5: 256000 op, 69111096.00 ns, 269.9652 ns/op
WorkloadPilot    6: 512000 op, 70747198.00 ns, 138.1781 ns/op
WorkloadPilot    7: 1024000 op, 141067533.00 ns, 137.7613 ns/op
WorkloadPilot    8: 2048000 op, 281325553.00 ns, 137.3660 ns/op
WorkloadPilot    9: 4096000 op, 563438172.00 ns, 137.5581 ns/op

OverheadWarmup   1: 4096000 op, 14953.00 ns, 0.0037 ns/op
OverheadWarmup   2: 4096000 op, 13079.00 ns, 0.0032 ns/op
OverheadWarmup   3: 4096000 op, 12988.00 ns, 0.0032 ns/op
OverheadWarmup   4: 4096000 op, 13089.00 ns, 0.0032 ns/op
OverheadWarmup   5: 4096000 op, 13043.00 ns, 0.0032 ns/op
OverheadWarmup   6: 4096000 op, 13014.00 ns, 0.0032 ns/op
OverheadWarmup   7: 4096000 op, 13015.00 ns, 0.0032 ns/op
OverheadWarmup   8: 4096000 op, 12992.00 ns, 0.0032 ns/op

OverheadActual   1: 4096000 op, 13081.00 ns, 0.0032 ns/op
OverheadActual   2: 4096000 op, 13093.00 ns, 0.0032 ns/op
OverheadActual   3: 4096000 op, 13105.00 ns, 0.0032 ns/op
OverheadActual   4: 4096000 op, 13001.00 ns, 0.0032 ns/op
OverheadActual   5: 4096000 op, 13060.00 ns, 0.0032 ns/op
OverheadActual   6: 4096000 op, 14654.00 ns, 0.0036 ns/op
OverheadActual   7: 4096000 op, 14579.00 ns, 0.0036 ns/op
OverheadActual   8: 4096000 op, 14558.00 ns, 0.0036 ns/op
OverheadActual   9: 4096000 op, 16298.00 ns, 0.0040 ns/op
OverheadActual  10: 4096000 op, 14491.00 ns, 0.0035 ns/op
OverheadActual  11: 4096000 op, 14559.00 ns, 0.0036 ns/op
OverheadActual  12: 4096000 op, 14345.00 ns, 0.0035 ns/op
OverheadActual  13: 4096000 op, 14422.00 ns, 0.0035 ns/op
OverheadActual  14: 4096000 op, 13964.00 ns, 0.0034 ns/op
OverheadActual  15: 4096000 op, 14419.00 ns, 0.0035 ns/op
OverheadActual  16: 4096000 op, 14130.00 ns, 0.0034 ns/op
OverheadActual  17: 4096000 op, 27811.00 ns, 0.0068 ns/op
OverheadActual  18: 4096000 op, 14430.00 ns, 0.0035 ns/op
OverheadActual  19: 4096000 op, 14685.00 ns, 0.0036 ns/op
OverheadActual  20: 4096000 op, 14471.00 ns, 0.0035 ns/op

WorkloadWarmup   1: 4096000 op, 571407522.00 ns, 139.5038 ns/op
WorkloadWarmup   2: 4096000 op, 573812651.00 ns, 140.0910 ns/op
WorkloadWarmup   3: 4096000 op, 565483139.00 ns, 138.0574 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 564433038.00 ns, 137.8010 ns/op
WorkloadActual   2: 4096000 op, 565870555.00 ns, 138.1520 ns/op
WorkloadActual   3: 4096000 op, 566421828.00 ns, 138.2866 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 564418612.00 ns, 137.7975 ns/op
WorkloadResult   2: 4096000 op, 565856129.00 ns, 138.1485 ns/op
WorkloadResult   3: 4096000 op, 566407402.00 ns, 138.2831 ns/op
// GC:  44 0 0 1114112000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4741 has exited with code 0.

Mean = 138.076 ns, StdErr = 0.145 ns (0.10%), N = 3, StdDev = 0.251 ns
Min = 137.798 ns, Q1 = 137.973 ns, Median = 138.148 ns, Q3 = 138.216 ns, Max = 138.283 ns
IQR = 0.243 ns, LowerFence = 137.609 ns, UpperFence = 138.580 ns
ConfidenceInterval = [133.503 ns; 142.650 ns] (CI 99.9%), Margin = 4.573 ns (3.31% of Mean)
Skewness = -0.26, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-05 11:51 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 182055.00 ns, 182.0550 ns/op
WorkloadJitting  1: 1000 op, 1199024.00 ns, 1.1990 us/op

OverheadJitting  2: 16000 op, 229475.00 ns, 14.3422 ns/op
WorkloadJitting  2: 16000 op, 7966159.00 ns, 497.8849 ns/op

WorkloadPilot    1: 16000 op, 6308353.00 ns, 394.2721 ns/op
WorkloadPilot    2: 32000 op, 11761438.00 ns, 367.5449 ns/op
WorkloadPilot    3: 64000 op, 23438691.00 ns, 366.2295 ns/op
WorkloadPilot    4: 128000 op, 47596736.00 ns, 371.8495 ns/op
WorkloadPilot    5: 256000 op, 65685897.00 ns, 256.5855 ns/op
WorkloadPilot    6: 512000 op, 41257088.00 ns, 80.5803 ns/op
WorkloadPilot    7: 1024000 op, 78412821.00 ns, 76.5750 ns/op
WorkloadPilot    8: 2048000 op, 156641019.00 ns, 76.4849 ns/op
WorkloadPilot    9: 4096000 op, 311404890.00 ns, 76.0266 ns/op
WorkloadPilot   10: 8192000 op, 623387682.00 ns, 76.0971 ns/op

OverheadWarmup   1: 8192000 op, 28257.00 ns, 0.0034 ns/op
OverheadWarmup   2: 8192000 op, 25746.00 ns, 0.0031 ns/op
OverheadWarmup   3: 8192000 op, 25483.00 ns, 0.0031 ns/op
OverheadWarmup   4: 8192000 op, 25494.00 ns, 0.0031 ns/op
OverheadWarmup   5: 8192000 op, 25530.00 ns, 0.0031 ns/op
OverheadWarmup   6: 8192000 op, 25472.00 ns, 0.0031 ns/op
OverheadWarmup   7: 8192000 op, 25520.00 ns, 0.0031 ns/op
OverheadWarmup   8: 8192000 op, 25497.00 ns, 0.0031 ns/op

OverheadActual   1: 8192000 op, 26718.00 ns, 0.0033 ns/op
OverheadActual   2: 8192000 op, 25602.00 ns, 0.0031 ns/op
OverheadActual   3: 8192000 op, 25618.00 ns, 0.0031 ns/op
OverheadActual   4: 8192000 op, 25596.00 ns, 0.0031 ns/op
OverheadActual   5: 8192000 op, 24856.00 ns, 0.0030 ns/op
OverheadActual   6: 8192000 op, 25516.00 ns, 0.0031 ns/op
OverheadActual   7: 8192000 op, 25466.00 ns, 0.0031 ns/op
OverheadActual   8: 8192000 op, 25489.00 ns, 0.0031 ns/op
OverheadActual   9: 8192000 op, 27143.00 ns, 0.0033 ns/op
OverheadActual  10: 8192000 op, 25502.00 ns, 0.0031 ns/op
OverheadActual  11: 8192000 op, 25506.00 ns, 0.0031 ns/op
OverheadActual  12: 8192000 op, 25481.00 ns, 0.0031 ns/op
OverheadActual  13: 8192000 op, 35261.00 ns, 0.0043 ns/op
OverheadActual  14: 8192000 op, 25529.00 ns, 0.0031 ns/op
OverheadActual  15: 8192000 op, 35989.00 ns, 0.0044 ns/op

WorkloadWarmup   1: 8192000 op, 632158835.00 ns, 77.1678 ns/op
WorkloadWarmup   2: 8192000 op, 660665213.00 ns, 80.6476 ns/op
WorkloadWarmup   3: 8192000 op, 623830772.00 ns, 76.1512 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 628107504.00 ns, 76.6733 ns/op
WorkloadActual   2: 8192000 op, 625779075.00 ns, 76.3890 ns/op
WorkloadActual   3: 8192000 op, 625323420.00 ns, 76.3334 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 628081975.00 ns, 76.6702 ns/op
WorkloadResult   2: 8192000 op, 625753546.00 ns, 76.3859 ns/op
WorkloadResult   3: 8192000 op, 625297891.00 ns, 76.3303 ns/op
// GC:  33 0 0 851968000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 4752 has exited with code 0.

Mean = 76.462 ns, StdErr = 0.105 ns (0.14%), N = 3, StdDev = 0.182 ns
Min = 76.330 ns, Q1 = 76.358 ns, Median = 76.386 ns, Q3 = 76.528 ns, Max = 76.670 ns
IQR = 0.170 ns, LowerFence = 76.103 ns, UpperFence = 76.783 ns
ConfidenceInterval = [73.136 ns; 79.788 ns] (CI 99.9%), Margin = 3.326 ns (4.35% of Mean)
Skewness = 0.34, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-05 11:51 (0h 0m from now) **
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

OverheadJitting  1: 1000 op, 186917.00 ns, 186.9170 ns/op
WorkloadJitting  1: 1000 op, 1623201.00 ns, 1.6232 us/op

OverheadJitting  2: 16000 op, 235874.00 ns, 14.7421 ns/op
WorkloadJitting  2: 16000 op, 14784332.00 ns, 924.0208 ns/op

WorkloadPilot    1: 16000 op, 12843375.00 ns, 802.7109 ns/op
WorkloadPilot    2: 32000 op, 23850296.00 ns, 745.3218 ns/op
WorkloadPilot    3: 64000 op, 48202686.00 ns, 753.1670 ns/op
WorkloadPilot    4: 128000 op, 84009779.00 ns, 656.3264 ns/op
WorkloadPilot    5: 256000 op, 48475619.00 ns, 189.3579 ns/op
WorkloadPilot    6: 512000 op, 91046939.00 ns, 177.8261 ns/op
WorkloadPilot    7: 1024000 op, 181143229.00 ns, 176.8977 ns/op
WorkloadPilot    8: 2048000 op, 363666086.00 ns, 177.5713 ns/op
WorkloadPilot    9: 4096000 op, 723838073.00 ns, 176.7183 ns/op

OverheadWarmup   1: 4096000 op, 15237.00 ns, 0.0037 ns/op
OverheadWarmup   2: 4096000 op, 12680.00 ns, 0.0031 ns/op
OverheadWarmup   3: 4096000 op, 12918.00 ns, 0.0032 ns/op
OverheadWarmup   4: 4096000 op, 12994.00 ns, 0.0032 ns/op
OverheadWarmup   5: 4096000 op, 12971.00 ns, 0.0032 ns/op
OverheadWarmup   6: 4096000 op, 12964.00 ns, 0.0032 ns/op
OverheadWarmup   7: 4096000 op, 12996.00 ns, 0.0032 ns/op
OverheadWarmup   8: 4096000 op, 13086.00 ns, 0.0032 ns/op
OverheadWarmup   9: 4096000 op, 13016.00 ns, 0.0032 ns/op

OverheadActual   1: 4096000 op, 13091.00 ns, 0.0032 ns/op
OverheadActual   2: 4096000 op, 12734.00 ns, 0.0031 ns/op
OverheadActual   3: 4096000 op, 13092.00 ns, 0.0032 ns/op
OverheadActual   4: 4096000 op, 13080.00 ns, 0.0032 ns/op
OverheadActual   5: 4096000 op, 13102.00 ns, 0.0032 ns/op
OverheadActual   6: 4096000 op, 12984.00 ns, 0.0032 ns/op
OverheadActual   7: 4096000 op, 12962.00 ns, 0.0032 ns/op
OverheadActual   8: 4096000 op, 14684.00 ns, 0.0036 ns/op
OverheadActual   9: 4096000 op, 12939.00 ns, 0.0032 ns/op
OverheadActual  10: 4096000 op, 12918.00 ns, 0.0032 ns/op
OverheadActual  11: 4096000 op, 12934.00 ns, 0.0032 ns/op
OverheadActual  12: 4096000 op, 12971.00 ns, 0.0032 ns/op
OverheadActual  13: 4096000 op, 12997.00 ns, 0.0032 ns/op
OverheadActual  14: 4096000 op, 13000.00 ns, 0.0032 ns/op
OverheadActual  15: 4096000 op, 13017.00 ns, 0.0032 ns/op

WorkloadWarmup   1: 4096000 op, 730594981.00 ns, 178.3679 ns/op
WorkloadWarmup   2: 4096000 op, 733980619.00 ns, 179.1945 ns/op
WorkloadWarmup   3: 4096000 op, 726908381.00 ns, 177.4679 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 727072431.00 ns, 177.5079 ns/op
WorkloadActual   2: 4096000 op, 725726787.00 ns, 177.1794 ns/op
WorkloadActual   3: 4096000 op, 726007154.00 ns, 177.2478 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 727059434.00 ns, 177.5047 ns/op
WorkloadResult   2: 4096000 op, 725713790.00 ns, 177.1762 ns/op
WorkloadResult   3: 4096000 op, 725994157.00 ns, 177.2447 ns/op
// GC:  32 0 0 819200000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 4760 has exited with code 0.

Mean = 177.309 ns, StdErr = 0.100 ns (0.06%), N = 3, StdDev = 0.173 ns
Min = 177.176 ns, Q1 = 177.210 ns, Median = 177.245 ns, Q3 = 177.375 ns, Max = 177.505 ns
IQR = 0.164 ns, LowerFence = 176.964 ns, UpperFence = 177.621 ns
ConfidenceInterval = [174.146 ns; 180.471 ns] (CI 99.9%), Margin = 3.162 ns (1.78% of Mean)
Skewness = 0.32, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-05 11:51 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 67.377 ns, StdErr = 0.035 ns (0.05%), N = 14, StdDev = 0.132 ns
Min = 67.212 ns, Q1 = 67.277 ns, Median = 67.339 ns, Q3 = 67.458 ns, Max = 67.672 ns
IQR = 0.182 ns, LowerFence = 67.004 ns, UpperFence = 67.731 ns
ConfidenceInterval = [67.229 ns; 67.526 ns] (CI 99.9%), Margin = 0.148 ns (0.22% of Mean)
Skewness = 0.8, Kurtosis = 2.48, MValue = 2
-------------------- Histogram --------------------
[67.140 ns ; 67.743 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 131.796 ns, StdErr = 0.126 ns (0.10%), N = 14, StdDev = 0.472 ns
Min = 131.101 ns, Q1 = 131.406 ns, Median = 131.788 ns, Q3 = 132.113 ns, Max = 132.542 ns
IQR = 0.706 ns, LowerFence = 130.347 ns, UpperFence = 133.172 ns
ConfidenceInterval = [131.264 ns; 132.329 ns] (CI 99.9%), Margin = 0.532 ns (0.40% of Mean)
Skewness = 0.07, Kurtosis = 1.56, MValue = 2
-------------------- Histogram --------------------
[130.844 ns ; 132.799 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 78.475 ns, StdErr = 0.058 ns (0.07%), N = 14, StdDev = 0.217 ns
Min = 78.213 ns, Q1 = 78.276 ns, Median = 78.443 ns, Q3 = 78.646 ns, Max = 78.864 ns
IQR = 0.370 ns, LowerFence = 77.721 ns, UpperFence = 79.202 ns
ConfidenceInterval = [78.231 ns; 78.719 ns] (CI 99.9%), Margin = 0.244 ns (0.31% of Mean)
Skewness = 0.34, Kurtosis = 1.57, MValue = 2
-------------------- Histogram --------------------
[78.095 ns ; 78.982 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 172.592 ns, StdErr = 0.046 ns (0.03%), N = 13, StdDev = 0.165 ns
Min = 172.417 ns, Q1 = 172.470 ns, Median = 172.562 ns, Q3 = 172.654 ns, Max = 172.965 ns
IQR = 0.184 ns, LowerFence = 172.194 ns, UpperFence = 172.929 ns
ConfidenceInterval = [172.394 ns; 172.790 ns] (CI 99.9%), Margin = 0.198 ns (0.11% of Mean)
Skewness = 0.84, Kurtosis = 2.58, MValue = 2
-------------------- Histogram --------------------
[172.324 ns ; 173.057 ns) | @@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 65.080 ns, StdErr = 0.050 ns (0.08%), N = 3, StdDev = 0.087 ns
Min = 65.014 ns, Q1 = 65.031 ns, Median = 65.048 ns, Q3 = 65.113 ns, Max = 65.178 ns
IQR = 0.082 ns, LowerFence = 64.908 ns, UpperFence = 65.236 ns
ConfidenceInterval = [63.501 ns; 66.659 ns] (CI 99.9%), Margin = 1.579 ns (2.43% of Mean)
Skewness = 0.32, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[64.935 ns ; 65.257 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 138.076 ns, StdErr = 0.145 ns (0.10%), N = 3, StdDev = 0.251 ns
Min = 137.798 ns, Q1 = 137.973 ns, Median = 138.148 ns, Q3 = 138.216 ns, Max = 138.283 ns
IQR = 0.243 ns, LowerFence = 137.609 ns, UpperFence = 138.580 ns
ConfidenceInterval = [133.503 ns; 142.650 ns] (CI 99.9%), Margin = 4.573 ns (3.31% of Mean)
Skewness = -0.26, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[137.569 ns ; 138.511 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 76.462 ns, StdErr = 0.105 ns (0.14%), N = 3, StdDev = 0.182 ns
Min = 76.330 ns, Q1 = 76.358 ns, Median = 76.386 ns, Q3 = 76.528 ns, Max = 76.670 ns
IQR = 0.170 ns, LowerFence = 76.103 ns, UpperFence = 76.783 ns
ConfidenceInterval = [73.136 ns; 79.788 ns] (CI 99.9%), Margin = 3.326 ns (4.35% of Mean)
Skewness = 0.34, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[76.164 ns ; 76.836 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 177.309 ns, StdErr = 0.100 ns (0.06%), N = 3, StdDev = 0.173 ns
Min = 177.176 ns, Q1 = 177.210 ns, Median = 177.245 ns, Q3 = 177.375 ns, Max = 177.505 ns
IQR = 0.164 ns, LowerFence = 176.964 ns, UpperFence = 177.621 ns
ConfidenceInterval = [174.146 ns; 180.471 ns] (CI 99.9%), Margin = 3.162 ns (1.78% of Mean)
Skewness = 0.32, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[177.018 ns ; 177.662 ns) | @@@
---------------------------------------------------

// * Summary *

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Platinum 8370C CPU 2.80GHz (Max: 3.39GHz), 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
  Job-CEIKLR : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
  ShortRun   : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4


| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean      | Error    | StdDev   | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |----------:|---------:|---------:|-------:|----------:|
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  67.38 ns | 0.148 ns | 0.132 ns | 0.0012 |      32 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 131.80 ns | 0.532 ns | 0.472 ns | 0.0107 |     272 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     |  78.48 ns | 0.244 ns | 0.217 ns | 0.0040 |     104 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 172.59 ns | 0.198 ns | 0.165 ns | 0.0078 |     200 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           |  65.08 ns | 1.579 ns | 0.087 ns | 0.0012 |      32 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 138.08 ns | 4.573 ns | 0.251 ns | 0.0107 |     272 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           |  76.46 ns | 3.326 ns | 0.182 ns | 0.0040 |     104 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 177.31 ns | 3.162 ns | 0.173 ns | 0.0078 |     200 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput                        -> 1 outlier  was  removed (68.33 ns)
  CoreDispatchBenchmarks.'Notification  Notify': RunStrategy=Throughput                 -> 1 outlier  was  removed (133.51 ns)
  CoreDispatchBenchmarks.'Request  Request': RunStrategy=Throughput                     -> 1 outlier  was  removed (79.95 ns)
  CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': RunStrategy=Throughput -> 2 outliers were removed (173.38 ns, 173.46 ns)
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
Run time: 00:01:28 (88.91 sec), executed benchmarks: 8

Global total time: 00:01:42 (102.81 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
