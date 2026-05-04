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

## Latest CI Benchmark Run

Run: 2026-05-04 02:21 UTC | Branch: feature/aot | Environment: Ubuntu 24.04.4 LTS, AMD EPYC 9V74 3.69GHz, .NET 10.0.5

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
  Job-CEIKLR : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
  ShortRun   : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4


```
| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean     | Error    | StdDev  | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |---------:|---------:|--------:|-------:|----------:|
| &#39;Command  Send&#39;                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 108.0 ns |  0.36 ns | 0.32 ns | 0.0115 |     192 B |
| &#39;Notification  Notify&#39;                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 164.5 ns |  0.56 ns | 0.47 ns | 0.0256 |     432 B |
| &#39;Request  Request&#39;                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 144.4 ns |  0.77 ns | 0.68 ns | 0.0156 |     264 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 201.6 ns |  0.96 ns | 0.90 ns | 0.0215 |     360 B |
| &#39;Command  Send&#39;                        | ShortRun   | 3              | 1           | Default     | 3           | 110.8 ns |  7.38 ns | 0.40 ns | 0.0115 |     192 B |
| &#39;Notification  Notify&#39;                 | ShortRun   | 3              | 1           | Default     | 3           | 168.0 ns | 13.34 ns | 0.73 ns | 0.0256 |     432 B |
| &#39;Request  Request&#39;                     | ShortRun   | 3              | 1           | Default     | 3           | 140.8 ns | 17.63 ns | 0.97 ns | 0.0156 |     264 B |
| &#39;Stream  RequestStream (3 items/call)&#39; | ShortRun   | 3              | 1           | Default     | 3           | 205.6 ns | 15.44 ns | 0.85 ns | 0.0215 |     360 B |

### Full Console Output

```
// Validating benchmarks:
// ***** BenchmarkRunner: Start   *****
// ***** Found 8 benchmark(s) in total *****
// ***** Building 1 exe(s) in Parallel: Start   *****
// start dotnet  restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 1.3 sec and exited with 0
// start dotnet  build -c Release --no-restore --nodeReuse:false /p:UseSharedCompilation=false /p:Deterministic=true /p:Optimize=true /p:ArtifactsPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/" /p:OutDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:OutputPath="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" /p:PublishDir="/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/publish/" --output "/home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0/" in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1
// command took 9.84 sec and exited with 0
// ***** Done, took 00:00:11 (11.19 sec)   *****
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
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 141 142 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Command --job RunStrategy=Throughput --benchmarkId 0 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 154173.00 ns, 154.1730 ns/op
WorkloadJitting  1: 1000 op, 1148678.00 ns, 1.1487 us/op

OverheadJitting  2: 16000 op, 160382.00 ns, 10.0239 ns/op
WorkloadJitting  2: 16000 op, 12218443.00 ns, 763.6527 ns/op

WorkloadPilot    1: 16000 op, 10832475.00 ns, 677.0297 ns/op
WorkloadPilot    2: 32000 op, 19729060.00 ns, 616.5331 ns/op
WorkloadPilot    3: 64000 op, 39039055.00 ns, 609.9852 ns/op
WorkloadPilot    4: 128000 op, 68320724.00 ns, 533.7557 ns/op
WorkloadPilot    5: 256000 op, 166512698.00 ns, 650.4402 ns/op
WorkloadPilot    6: 512000 op, 120514610.00 ns, 235.3801 ns/op
WorkloadPilot    7: 1024000 op, 110727126.00 ns, 108.1320 ns/op
WorkloadPilot    8: 2048000 op, 220528591.00 ns, 107.6800 ns/op
WorkloadPilot    9: 4096000 op, 443003808.00 ns, 108.1552 ns/op
WorkloadPilot   10: 8192000 op, 886359976.00 ns, 108.1982 ns/op

OverheadWarmup   1: 8192000 op, 26960.00 ns, 0.0033 ns/op
OverheadWarmup   2: 8192000 op, 20732.00 ns, 0.0025 ns/op
OverheadWarmup   3: 8192000 op, 20561.00 ns, 0.0025 ns/op
OverheadWarmup   4: 8192000 op, 20831.00 ns, 0.0025 ns/op
OverheadWarmup   5: 8192000 op, 32299.00 ns, 0.0039 ns/op
OverheadWarmup   6: 8192000 op, 20911.00 ns, 0.0026 ns/op
OverheadWarmup   7: 8192000 op, 33902.00 ns, 0.0041 ns/op
OverheadWarmup   8: 8192000 op, 20541.00 ns, 0.0025 ns/op

OverheadActual   1: 8192000 op, 27502.00 ns, 0.0034 ns/op
OverheadActual   2: 8192000 op, 20662.00 ns, 0.0025 ns/op
OverheadActual   3: 8192000 op, 20461.00 ns, 0.0025 ns/op
OverheadActual   4: 8192000 op, 20361.00 ns, 0.0025 ns/op
OverheadActual   5: 8192000 op, 19890.00 ns, 0.0024 ns/op
OverheadActual   6: 8192000 op, 20741.00 ns, 0.0025 ns/op
OverheadActual   7: 8192000 op, 20902.00 ns, 0.0026 ns/op
OverheadActual   8: 8192000 op, 20792.00 ns, 0.0025 ns/op
OverheadActual   9: 8192000 op, 25378.00 ns, 0.0031 ns/op
OverheadActual  10: 8192000 op, 20651.00 ns, 0.0025 ns/op
OverheadActual  11: 8192000 op, 19910.00 ns, 0.0024 ns/op
OverheadActual  12: 8192000 op, 34372.00 ns, 0.0042 ns/op
OverheadActual  13: 8192000 op, 20391.00 ns, 0.0025 ns/op
OverheadActual  14: 8192000 op, 20551.00 ns, 0.0025 ns/op
OverheadActual  15: 8192000 op, 20150.00 ns, 0.0025 ns/op

WorkloadWarmup   1: 8192000 op, 894896564.00 ns, 109.2403 ns/op
WorkloadWarmup   2: 8192000 op, 896350777.00 ns, 109.4178 ns/op
WorkloadWarmup   3: 8192000 op, 882890252.00 ns, 107.7747 ns/op
WorkloadWarmup   4: 8192000 op, 883417901.00 ns, 107.8391 ns/op
WorkloadWarmup   5: 8192000 op, 886389110.00 ns, 108.2018 ns/op
WorkloadWarmup   6: 8192000 op, 882285775.00 ns, 107.7009 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 891590478.00 ns, 108.8367 ns/op
WorkloadActual   2: 8192000 op, 884315317.00 ns, 107.9486 ns/op
WorkloadActual   3: 8192000 op, 890527813.00 ns, 108.7070 ns/op
WorkloadActual   4: 8192000 op, 881509889.00 ns, 107.6062 ns/op
WorkloadActual   5: 8192000 op, 885263313.00 ns, 108.0644 ns/op
WorkloadActual   6: 8192000 op, 884917746.00 ns, 108.0222 ns/op
WorkloadActual   7: 8192000 op, 882986414.00 ns, 107.7864 ns/op
WorkloadActual   8: 8192000 op, 885566697.00 ns, 108.1014 ns/op
WorkloadActual   9: 8192000 op, 882947535.00 ns, 107.7817 ns/op
WorkloadActual  10: 8192000 op, 886848361.00 ns, 108.2579 ns/op
WorkloadActual  11: 8192000 op, 881621405.00 ns, 107.6198 ns/op
WorkloadActual  12: 8192000 op, 889303859.00 ns, 108.5576 ns/op
WorkloadActual  13: 8192000 op, 883090864.00 ns, 107.7992 ns/op
WorkloadActual  14: 8192000 op, 884409929.00 ns, 107.9602 ns/op
WorkloadActual  15: 8192000 op, 884251659.00 ns, 107.9409 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 884294666.00 ns, 107.9461 ns/op
WorkloadResult   2: 8192000 op, 890507162.00 ns, 108.7045 ns/op
WorkloadResult   3: 8192000 op, 881489238.00 ns, 107.6037 ns/op
WorkloadResult   4: 8192000 op, 885242662.00 ns, 108.0618 ns/op
WorkloadResult   5: 8192000 op, 884897095.00 ns, 108.0197 ns/op
WorkloadResult   6: 8192000 op, 882965763.00 ns, 107.7839 ns/op
WorkloadResult   7: 8192000 op, 885546046.00 ns, 108.0989 ns/op
WorkloadResult   8: 8192000 op, 882926884.00 ns, 107.7792 ns/op
WorkloadResult   9: 8192000 op, 886827710.00 ns, 108.2553 ns/op
WorkloadResult  10: 8192000 op, 881600754.00 ns, 107.6173 ns/op
WorkloadResult  11: 8192000 op, 889283208.00 ns, 108.5551 ns/op
WorkloadResult  12: 8192000 op, 883070213.00 ns, 107.7967 ns/op
WorkloadResult  13: 8192000 op, 884389278.00 ns, 107.9577 ns/op
WorkloadResult  14: 8192000 op, 884231008.00 ns, 107.9384 ns/op
// GC:  94 0 0 1572864000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 5156 has exited with code 0.

Mean = 108.008 ns, StdErr = 0.086 ns (0.08%), N = 14, StdDev = 0.320 ns
Min = 107.604 ns, Q1 = 107.787 ns, Median = 107.952 ns, Q3 = 108.090 ns, Max = 108.704 ns
IQR = 0.303 ns, LowerFence = 107.333 ns, UpperFence = 108.543 ns
ConfidenceInterval = [107.647 ns; 108.369 ns] (CI 99.9%), Margin = 0.361 ns (0.33% of Mean)
Skewness = 0.78, Kurtosis = 2.62, MValue = 2

// ** Remained 7 (87.5 %) benchmark(s) to run. Estimated finish 2026-05-04 2:22 (0h 2m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 141 142 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Notification --job RunStrategy=Throughput --benchmarkId 1 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 149516.00 ns, 149.5160 ns/op
WorkloadJitting  1: 1000 op, 1350813.00 ns, 1.3508 us/op

OverheadJitting  2: 16000 op, 178610.00 ns, 11.1631 ns/op
WorkloadJitting  2: 16000 op, 15355398.00 ns, 959.7124 ns/op

WorkloadPilot    1: 16000 op, 17521132.00 ns, 1.0951 us/op
WorkloadPilot    2: 32000 op, 23814868.00 ns, 744.2146 ns/op
WorkloadPilot    3: 64000 op, 36918642.00 ns, 576.8538 ns/op
WorkloadPilot    4: 128000 op, 71953305.00 ns, 562.1352 ns/op
WorkloadPilot    5: 256000 op, 158160203.00 ns, 617.8133 ns/op
WorkloadPilot    6: 512000 op, 84184232.00 ns, 164.4223 ns/op
WorkloadPilot    7: 1024000 op, 167274569.00 ns, 163.3541 ns/op
WorkloadPilot    8: 2048000 op, 336748398.00 ns, 164.4279 ns/op
WorkloadPilot    9: 4096000 op, 668037311.00 ns, 163.0950 ns/op

OverheadWarmup   1: 4096000 op, 10535.00 ns, 0.0026 ns/op
OverheadWarmup   2: 4096000 op, 7331.00 ns, 0.0018 ns/op
OverheadWarmup   3: 4096000 op, 7251.00 ns, 0.0018 ns/op
OverheadWarmup   4: 4096000 op, 7251.00 ns, 0.0018 ns/op
OverheadWarmup   5: 4096000 op, 7291.00 ns, 0.0018 ns/op
OverheadWarmup   6: 4096000 op, 7231.00 ns, 0.0018 ns/op
OverheadWarmup   7: 4096000 op, 7351.00 ns, 0.0018 ns/op

OverheadActual   1: 4096000 op, 7381.00 ns, 0.0018 ns/op
OverheadActual   2: 4096000 op, 7371.00 ns, 0.0018 ns/op
OverheadActual   3: 4096000 op, 7431.00 ns, 0.0018 ns/op
OverheadActual   4: 4096000 op, 7321.00 ns, 0.0018 ns/op
OverheadActual   5: 4096000 op, 7331.00 ns, 0.0018 ns/op
OverheadActual   6: 4096000 op, 7361.00 ns, 0.0018 ns/op
OverheadActual   7: 4096000 op, 7251.00 ns, 0.0018 ns/op
OverheadActual   8: 4096000 op, 7241.00 ns, 0.0018 ns/op
OverheadActual   9: 4096000 op, 7251.00 ns, 0.0018 ns/op
OverheadActual  10: 4096000 op, 8813.00 ns, 0.0022 ns/op
OverheadActual  11: 4096000 op, 7291.00 ns, 0.0018 ns/op
OverheadActual  12: 4096000 op, 7391.00 ns, 0.0018 ns/op
OverheadActual  13: 4096000 op, 10436.00 ns, 0.0025 ns/op
OverheadActual  14: 4096000 op, 10205.00 ns, 0.0025 ns/op
OverheadActual  15: 4096000 op, 10096.00 ns, 0.0025 ns/op
OverheadActual  16: 4096000 op, 10365.00 ns, 0.0025 ns/op
OverheadActual  17: 4096000 op, 10035.00 ns, 0.0024 ns/op
OverheadActual  18: 4096000 op, 10185.00 ns, 0.0025 ns/op
OverheadActual  19: 4096000 op, 10095.00 ns, 0.0025 ns/op
OverheadActual  20: 4096000 op, 10125.00 ns, 0.0025 ns/op

WorkloadWarmup   1: 4096000 op, 688785597.00 ns, 168.1605 ns/op
WorkloadWarmup   2: 4096000 op, 674374146.00 ns, 164.6421 ns/op
WorkloadWarmup   3: 4096000 op, 670063943.00 ns, 163.5898 ns/op
WorkloadWarmup   4: 4096000 op, 672245045.00 ns, 164.1223 ns/op
WorkloadWarmup   5: 4096000 op, 666550468.00 ns, 162.7320 ns/op
WorkloadWarmup   6: 4096000 op, 667735952.00 ns, 163.0215 ns/op
WorkloadWarmup   7: 4096000 op, 671413677.00 ns, 163.9194 ns/op
WorkloadWarmup   8: 4096000 op, 667055012.00 ns, 162.8552 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 671780492.00 ns, 164.0089 ns/op
WorkloadActual   2: 4096000 op, 675769779.00 ns, 164.9829 ns/op
WorkloadActual   3: 4096000 op, 672240558.00 ns, 164.1212 ns/op
WorkloadActual   4: 4096000 op, 672414922.00 ns, 164.1638 ns/op
WorkloadActual   5: 4096000 op, 677653518.00 ns, 165.4428 ns/op
WorkloadActual   6: 4096000 op, 672521193.00 ns, 164.1897 ns/op
WorkloadActual   7: 4096000 op, 671833732.00 ns, 164.0219 ns/op
WorkloadActual   8: 4096000 op, 675200670.00 ns, 164.8439 ns/op
WorkloadActual   9: 4096000 op, 674253401.00 ns, 164.6126 ns/op
WorkloadActual  10: 4096000 op, 673773326.00 ns, 164.4954 ns/op
WorkloadActual  11: 4096000 op, 687260924.00 ns, 167.7883 ns/op
WorkloadActual  12: 4096000 op, 698360777.00 ns, 170.4982 ns/op
WorkloadActual  13: 4096000 op, 673093705.00 ns, 164.3295 ns/op
WorkloadActual  14: 4096000 op, 676353188.00 ns, 165.1253 ns/op
WorkloadActual  15: 4096000 op, 672259180.00 ns, 164.1258 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 671773081.00 ns, 164.0071 ns/op
WorkloadResult   2: 4096000 op, 675762368.00 ns, 164.9810 ns/op
WorkloadResult   3: 4096000 op, 672233147.00 ns, 164.1194 ns/op
WorkloadResult   4: 4096000 op, 672407511.00 ns, 164.1620 ns/op
WorkloadResult   5: 4096000 op, 677646107.00 ns, 165.4409 ns/op
WorkloadResult   6: 4096000 op, 672513782.00 ns, 164.1879 ns/op
WorkloadResult   7: 4096000 op, 671826321.00 ns, 164.0201 ns/op
WorkloadResult   8: 4096000 op, 675193259.00 ns, 164.8421 ns/op
WorkloadResult   9: 4096000 op, 674245990.00 ns, 164.6108 ns/op
WorkloadResult  10: 4096000 op, 673765915.00 ns, 164.4936 ns/op
WorkloadResult  11: 4096000 op, 673086294.00 ns, 164.3277 ns/op
WorkloadResult  12: 4096000 op, 676345777.00 ns, 165.1235 ns/op
WorkloadResult  13: 4096000 op, 672251769.00 ns, 164.1240 ns/op
// GC:  105 0 0 1769472000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 5179 has exited with code 0.

Mean = 164.495 ns, StdErr = 0.130 ns (0.08%), N = 13, StdDev = 0.469 ns
Min = 164.007 ns, Q1 = 164.124 ns, Median = 164.328 ns, Q3 = 164.842 ns, Max = 165.441 ns
IQR = 0.718 ns, LowerFence = 163.047 ns, UpperFence = 165.919 ns
ConfidenceInterval = [163.934 ns; 165.057 ns] (CI 99.9%), Margin = 0.561 ns (0.34% of Mean)
Skewness = 0.64, Kurtosis = 1.9, MValue = 2

// ** Remained 6 (75.0 %) benchmark(s) to run. Estimated finish 2026-05-04 2:22 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 141 142 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Request --job RunStrategy=Throughput --benchmarkId 2 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 156878.00 ns, 156.8780 ns/op
WorkloadJitting  1: 1000 op, 1214648.00 ns, 1.2146 us/op

OverheadJitting  2: 16000 op, 155034.00 ns, 9.6896 ns/op
WorkloadJitting  2: 16000 op, 13007410.00 ns, 812.9631 ns/op

WorkloadPilot    1: 16000 op, 10451721.00 ns, 653.2326 ns/op
WorkloadPilot    2: 32000 op, 21572115.00 ns, 674.1286 ns/op
WorkloadPilot    3: 64000 op, 33095900.00 ns, 517.1234 ns/op
WorkloadPilot    4: 128000 op, 66085828.00 ns, 516.2955 ns/op
WorkloadPilot    5: 256000 op, 161308864.00 ns, 630.1128 ns/op
WorkloadPilot    6: 512000 op, 77110648.00 ns, 150.6067 ns/op
WorkloadPilot    7: 1024000 op, 149172943.00 ns, 145.6767 ns/op
WorkloadPilot    8: 2048000 op, 297029351.00 ns, 145.0339 ns/op
WorkloadPilot    9: 4096000 op, 591726889.00 ns, 144.4646 ns/op

OverheadWarmup   1: 4096000 op, 14652.00 ns, 0.0036 ns/op
OverheadWarmup   2: 4096000 op, 10656.00 ns, 0.0026 ns/op
OverheadWarmup   3: 4096000 op, 10196.00 ns, 0.0025 ns/op
OverheadWarmup   4: 4096000 op, 10446.00 ns, 0.0026 ns/op
OverheadWarmup   5: 4096000 op, 10576.00 ns, 0.0026 ns/op
OverheadWarmup   6: 4096000 op, 10495.00 ns, 0.0026 ns/op
OverheadWarmup   7: 4096000 op, 10446.00 ns, 0.0026 ns/op
OverheadWarmup   8: 4096000 op, 10636.00 ns, 0.0026 ns/op
OverheadWarmup   9: 4096000 op, 10466.00 ns, 0.0026 ns/op

OverheadActual   1: 4096000 op, 10906.00 ns, 0.0027 ns/op
OverheadActual   2: 4096000 op, 11087.00 ns, 0.0027 ns/op
OverheadActual   3: 4096000 op, 10926.00 ns, 0.0027 ns/op
OverheadActual   4: 4096000 op, 10687.00 ns, 0.0026 ns/op
OverheadActual   5: 4096000 op, 10646.00 ns, 0.0026 ns/op
OverheadActual   6: 4096000 op, 10296.00 ns, 0.0025 ns/op
OverheadActual   7: 4096000 op, 10035.00 ns, 0.0024 ns/op
OverheadActual   8: 4096000 op, 13871.00 ns, 0.0034 ns/op
OverheadActual   9: 4096000 op, 12288.00 ns, 0.0030 ns/op
OverheadActual  10: 4096000 op, 10686.00 ns, 0.0026 ns/op
OverheadActual  11: 4096000 op, 10777.00 ns, 0.0026 ns/op
OverheadActual  12: 4096000 op, 10886.00 ns, 0.0027 ns/op
OverheadActual  13: 4096000 op, 10465.00 ns, 0.0026 ns/op
OverheadActual  14: 4096000 op, 10205.00 ns, 0.0025 ns/op
OverheadActual  15: 4096000 op, 12058.00 ns, 0.0029 ns/op

WorkloadWarmup   1: 4096000 op, 606702495.00 ns, 148.1207 ns/op
WorkloadWarmup   2: 4096000 op, 607517158.00 ns, 148.3196 ns/op
WorkloadWarmup   3: 4096000 op, 596801569.00 ns, 145.7035 ns/op
WorkloadWarmup   4: 4096000 op, 588599223.00 ns, 143.7010 ns/op
WorkloadWarmup   5: 4096000 op, 587472778.00 ns, 143.4260 ns/op
WorkloadWarmup   6: 4096000 op, 584591556.00 ns, 142.7225 ns/op
WorkloadWarmup   7: 4096000 op, 586031626.00 ns, 143.0741 ns/op
WorkloadWarmup   8: 4096000 op, 587356071.00 ns, 143.3975 ns/op
WorkloadWarmup   9: 4096000 op, 584879642.00 ns, 142.7929 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 589411652.00 ns, 143.8993 ns/op
WorkloadActual   2: 4096000 op, 593578560.00 ns, 144.9166 ns/op
WorkloadActual   3: 4096000 op, 594102834.00 ns, 145.0446 ns/op
WorkloadActual   4: 4096000 op, 595881424.00 ns, 145.4789 ns/op
WorkloadActual   5: 4096000 op, 592046170.00 ns, 144.5425 ns/op
WorkloadActual   6: 4096000 op, 596952808.00 ns, 145.7404 ns/op
WorkloadActual   7: 4096000 op, 602187267.00 ns, 147.0184 ns/op
WorkloadActual   8: 4096000 op, 588732457.00 ns, 143.7335 ns/op
WorkloadActual   9: 4096000 op, 592801613.00 ns, 144.7270 ns/op
WorkloadActual  10: 4096000 op, 589885661.00 ns, 144.0151 ns/op
WorkloadActual  11: 4096000 op, 591831526.00 ns, 144.4901 ns/op
WorkloadActual  12: 4096000 op, 589422751.00 ns, 143.9020 ns/op
WorkloadActual  13: 4096000 op, 588428537.00 ns, 143.6593 ns/op
WorkloadActual  14: 4096000 op, 588886619.00 ns, 143.7711 ns/op
WorkloadActual  15: 4096000 op, 589449612.00 ns, 143.9086 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 589400875.00 ns, 143.8967 ns/op
WorkloadResult   2: 4096000 op, 593567783.00 ns, 144.9140 ns/op
WorkloadResult   3: 4096000 op, 594092057.00 ns, 145.0420 ns/op
WorkloadResult   4: 4096000 op, 595870647.00 ns, 145.4762 ns/op
WorkloadResult   5: 4096000 op, 592035393.00 ns, 144.5399 ns/op
WorkloadResult   6: 4096000 op, 596942031.00 ns, 145.7378 ns/op
WorkloadResult   7: 4096000 op, 588721680.00 ns, 143.7309 ns/op
WorkloadResult   8: 4096000 op, 592790836.00 ns, 144.7243 ns/op
WorkloadResult   9: 4096000 op, 589874884.00 ns, 144.0124 ns/op
WorkloadResult  10: 4096000 op, 591820749.00 ns, 144.4875 ns/op
WorkloadResult  11: 4096000 op, 589411974.00 ns, 143.8994 ns/op
WorkloadResult  12: 4096000 op, 588417760.00 ns, 143.6567 ns/op
WorkloadResult  13: 4096000 op, 588875842.00 ns, 143.7685 ns/op
WorkloadResult  14: 4096000 op, 589438835.00 ns, 143.9060 ns/op
// GC:  64 0 0 1081344000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 5191 has exited with code 0.

Mean = 144.414 ns, StdErr = 0.182 ns (0.13%), N = 14, StdDev = 0.682 ns
Min = 143.657 ns, Q1 = 143.897 ns, Median = 144.250 ns, Q3 = 144.867 ns, Max = 145.738 ns
IQR = 0.969 ns, LowerFence = 142.444 ns, UpperFence = 146.320 ns
ConfidenceInterval = [143.644 ns; 145.183 ns] (CI 99.9%), Margin = 0.770 ns (0.53% of Mean)
Skewness = 0.55, Kurtosis = 1.82, MValue = 2

// ** Remained 5 (62.5 %) benchmark(s) to run. Estimated finish 2026-05-04 2:21 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 141 142 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Stream --job RunStrategy=Throughput --benchmarkId 3 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: Job-CEIKLR(RunStrategy=Throughput)

OverheadJitting  1: 1000 op, 180192.00 ns, 180.1920 ns/op
WorkloadJitting  1: 1000 op, 2519462.00 ns, 2.5195 us/op

OverheadJitting  2: 16000 op, 205311.00 ns, 12.8319 ns/op
WorkloadJitting  2: 16000 op, 24612888.00 ns, 1.5383 us/op

WorkloadPilot    1: 16000 op, 15169300.00 ns, 948.0813 ns/op
WorkloadPilot    2: 32000 op, 37034707.00 ns, 1.1573 us/op
WorkloadPilot    3: 64000 op, 67665604.00 ns, 1.0573 us/op
WorkloadPilot    4: 128000 op, 140552197.00 ns, 1.0981 us/op
WorkloadPilot    5: 256000 op, 70301031.00 ns, 274.6134 ns/op
WorkloadPilot    6: 512000 op, 103997432.00 ns, 203.1200 ns/op
WorkloadPilot    7: 1024000 op, 208753908.00 ns, 203.8612 ns/op
WorkloadPilot    8: 2048000 op, 411111240.00 ns, 200.7379 ns/op
WorkloadPilot    9: 4096000 op, 825798231.00 ns, 201.6109 ns/op

OverheadWarmup   1: 4096000 op, 11107.00 ns, 0.0027 ns/op
OverheadWarmup   2: 4096000 op, 7301.00 ns, 0.0018 ns/op
OverheadWarmup   3: 4096000 op, 10235.00 ns, 0.0025 ns/op
OverheadWarmup   4: 4096000 op, 10245.00 ns, 0.0025 ns/op
OverheadWarmup   5: 4096000 op, 10116.00 ns, 0.0025 ns/op
OverheadWarmup   6: 4096000 op, 10506.00 ns, 0.0026 ns/op
OverheadWarmup   7: 4096000 op, 10405.00 ns, 0.0025 ns/op

OverheadActual   1: 4096000 op, 10696.00 ns, 0.0026 ns/op
OverheadActual   2: 4096000 op, 10315.00 ns, 0.0025 ns/op
OverheadActual   3: 4096000 op, 10606.00 ns, 0.0026 ns/op
OverheadActual   4: 4096000 op, 10266.00 ns, 0.0025 ns/op
OverheadActual   5: 4096000 op, 10376.00 ns, 0.0025 ns/op
OverheadActual   6: 4096000 op, 10216.00 ns, 0.0025 ns/op
OverheadActual   7: 4096000 op, 10336.00 ns, 0.0025 ns/op
OverheadActual   8: 4096000 op, 10396.00 ns, 0.0025 ns/op
OverheadActual   9: 4096000 op, 10366.00 ns, 0.0025 ns/op
OverheadActual  10: 4096000 op, 9274.00 ns, 0.0023 ns/op
OverheadActual  11: 4096000 op, 7231.00 ns, 0.0018 ns/op
OverheadActual  12: 4096000 op, 10516.00 ns, 0.0026 ns/op
OverheadActual  13: 4096000 op, 10486.00 ns, 0.0026 ns/op
OverheadActual  14: 4096000 op, 10596.00 ns, 0.0026 ns/op
OverheadActual  15: 4096000 op, 7391.00 ns, 0.0018 ns/op
OverheadActual  16: 4096000 op, 10215.00 ns, 0.0025 ns/op
OverheadActual  17: 4096000 op, 7302.00 ns, 0.0018 ns/op
OverheadActual  18: 4096000 op, 7291.00 ns, 0.0018 ns/op
OverheadActual  19: 4096000 op, 7311.00 ns, 0.0018 ns/op
OverheadActual  20: 4096000 op, 7321.00 ns, 0.0018 ns/op

WorkloadWarmup   1: 4096000 op, 861889414.00 ns, 210.4222 ns/op
WorkloadWarmup   2: 4096000 op, 834684701.00 ns, 203.7804 ns/op
WorkloadWarmup   3: 4096000 op, 821799539.00 ns, 200.6347 ns/op
WorkloadWarmup   4: 4096000 op, 824656383.00 ns, 201.3321 ns/op
WorkloadWarmup   5: 4096000 op, 827042674.00 ns, 201.9147 ns/op
WorkloadWarmup   6: 4096000 op, 823228617.00 ns, 200.9835 ns/op
WorkloadWarmup   7: 4096000 op, 825380910.00 ns, 201.5090 ns/op
WorkloadWarmup   8: 4096000 op, 822266045.00 ns, 200.7485 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 828034650.00 ns, 202.1569 ns/op
WorkloadActual   2: 4096000 op, 828126499.00 ns, 202.1793 ns/op
WorkloadActual   3: 4096000 op, 821455923.00 ns, 200.5508 ns/op
WorkloadActual   4: 4096000 op, 834700278.00 ns, 203.7842 ns/op
WorkloadActual   5: 4096000 op, 820808042.00 ns, 200.3926 ns/op
WorkloadActual   6: 4096000 op, 829941090.00 ns, 202.6223 ns/op
WorkloadActual   7: 4096000 op, 822958537.00 ns, 200.9176 ns/op
WorkloadActual   8: 4096000 op, 823724437.00 ns, 201.1046 ns/op
WorkloadActual   9: 4096000 op, 828242825.00 ns, 202.2077 ns/op
WorkloadActual  10: 4096000 op, 824953301.00 ns, 201.4046 ns/op
WorkloadActual  11: 4096000 op, 826116827.00 ns, 201.6887 ns/op
WorkloadActual  12: 4096000 op, 824123051.00 ns, 201.2019 ns/op
WorkloadActual  13: 4096000 op, 825253281.00 ns, 201.4779 ns/op
WorkloadActual  14: 4096000 op, 824874538.00 ns, 201.3854 ns/op
WorkloadActual  15: 4096000 op, 821965417.00 ns, 200.6752 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 828024359.50 ns, 202.1544 ns/op
WorkloadResult   2: 4096000 op, 828116208.50 ns, 202.1768 ns/op
WorkloadResult   3: 4096000 op, 821445632.50 ns, 200.5483 ns/op
WorkloadResult   4: 4096000 op, 834689987.50 ns, 203.7817 ns/op
WorkloadResult   5: 4096000 op, 820797751.50 ns, 200.3901 ns/op
WorkloadResult   6: 4096000 op, 829930799.50 ns, 202.6198 ns/op
WorkloadResult   7: 4096000 op, 822948246.50 ns, 200.9151 ns/op
WorkloadResult   8: 4096000 op, 823714146.50 ns, 201.1021 ns/op
WorkloadResult   9: 4096000 op, 828232534.50 ns, 202.2052 ns/op
WorkloadResult  10: 4096000 op, 824943010.50 ns, 201.4021 ns/op
WorkloadResult  11: 4096000 op, 826106536.50 ns, 201.6862 ns/op
WorkloadResult  12: 4096000 op, 824112760.50 ns, 201.1994 ns/op
WorkloadResult  13: 4096000 op, 825242990.50 ns, 201.4753 ns/op
WorkloadResult  14: 4096000 op, 824864247.50 ns, 201.3829 ns/op
WorkloadResult  15: 4096000 op, 821955126.50 ns, 200.6726 ns/op
// GC:  88 0 0 1474560000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 5208 has exited with code 0.

Mean = 201.581 ns, StdErr = 0.231 ns (0.11%), N = 15, StdDev = 0.895 ns
Min = 200.390 ns, Q1 = 201.009 ns, Median = 201.402 ns, Q3 = 202.166 ns, Max = 203.782 ns
IQR = 1.157 ns, LowerFence = 199.273 ns, UpperFence = 203.901 ns
ConfidenceInterval = [200.624 ns; 202.538 ns] (CI 99.9%), Margin = 0.957 ns (0.47% of Mean)
Skewness = 0.78, Kurtosis = 3.02, MValue = 2

// ** Remained 4 (50.0 %) benchmark(s) to run. Estimated finish 2026-05-04 2:22 (0h 1m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 141 142 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Command --job ShortRun --benchmarkId 4 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 151530.00 ns, 151.5300 ns/op
WorkloadJitting  1: 1000 op, 1112373.00 ns, 1.1124 us/op

OverheadJitting  2: 16000 op, 187053.00 ns, 11.6908 ns/op
WorkloadJitting  2: 16000 op, 11878398.00 ns, 742.3999 ns/op

WorkloadPilot    1: 16000 op, 10436998.00 ns, 652.3124 ns/op
WorkloadPilot    2: 32000 op, 18952269.00 ns, 592.2584 ns/op
WorkloadPilot    3: 64000 op, 34853243.00 ns, 544.5819 ns/op
WorkloadPilot    4: 128000 op, 63412374.00 ns, 495.4092 ns/op
WorkloadPilot    5: 256000 op, 176771252.00 ns, 690.5127 ns/op
WorkloadPilot    6: 512000 op, 125135182.00 ns, 244.4047 ns/op
WorkloadPilot    7: 1024000 op, 113157884.00 ns, 110.5057 ns/op
WorkloadPilot    8: 2048000 op, 226410061.00 ns, 110.5518 ns/op
WorkloadPilot    9: 4096000 op, 452261476.00 ns, 110.4154 ns/op
WorkloadPilot   10: 8192000 op, 906397137.00 ns, 110.6442 ns/op

OverheadWarmup   1: 8192000 op, 18639.00 ns, 0.0023 ns/op
OverheadWarmup   2: 8192000 op, 23926.00 ns, 0.0029 ns/op
OverheadWarmup   3: 8192000 op, 24027.00 ns, 0.0029 ns/op
OverheadWarmup   4: 8192000 op, 24247.00 ns, 0.0030 ns/op
OverheadWarmup   5: 8192000 op, 24266.00 ns, 0.0030 ns/op
OverheadWarmup   6: 8192000 op, 14262.00 ns, 0.0017 ns/op
OverheadWarmup   7: 8192000 op, 14602.00 ns, 0.0018 ns/op
OverheadWarmup   8: 8192000 op, 19840.00 ns, 0.0024 ns/op
OverheadWarmup   9: 8192000 op, 17186.00 ns, 0.0021 ns/op

OverheadActual   1: 8192000 op, 14361.00 ns, 0.0018 ns/op
OverheadActual   2: 8192000 op, 14252.00 ns, 0.0017 ns/op
OverheadActual   3: 8192000 op, 14712.00 ns, 0.0018 ns/op
OverheadActual   4: 8192000 op, 14252.00 ns, 0.0017 ns/op
OverheadActual   5: 8192000 op, 14252.00 ns, 0.0017 ns/op
OverheadActual   6: 8192000 op, 14352.00 ns, 0.0018 ns/op
OverheadActual   7: 8192000 op, 14452.00 ns, 0.0018 ns/op
OverheadActual   8: 8192000 op, 16925.00 ns, 0.0021 ns/op
OverheadActual   9: 8192000 op, 16425.00 ns, 0.0020 ns/op
OverheadActual  10: 8192000 op, 16415.00 ns, 0.0020 ns/op
OverheadActual  11: 8192000 op, 21673.00 ns, 0.0026 ns/op
OverheadActual  12: 8192000 op, 14341.00 ns, 0.0018 ns/op
OverheadActual  13: 8192000 op, 14292.00 ns, 0.0017 ns/op
OverheadActual  14: 8192000 op, 14292.00 ns, 0.0017 ns/op
OverheadActual  15: 8192000 op, 14492.00 ns, 0.0018 ns/op
OverheadActual  16: 8192000 op, 16945.00 ns, 0.0021 ns/op
OverheadActual  17: 8192000 op, 24167.00 ns, 0.0030 ns/op
OverheadActual  18: 8192000 op, 19930.00 ns, 0.0024 ns/op
OverheadActual  19: 8192000 op, 14232.00 ns, 0.0017 ns/op
OverheadActual  20: 8192000 op, 14201.00 ns, 0.0017 ns/op

WorkloadWarmup   1: 8192000 op, 917670945.00 ns, 112.0204 ns/op
WorkloadWarmup   2: 8192000 op, 912098997.00 ns, 111.3402 ns/op
WorkloadWarmup   3: 8192000 op, 901962432.00 ns, 110.1028 ns/op

// BeforeActualRun
WorkloadActual   1: 8192000 op, 910491174.00 ns, 111.1439 ns/op
WorkloadActual   2: 8192000 op, 904094137.00 ns, 110.3631 ns/op
WorkloadActual   3: 8192000 op, 908799846.00 ns, 110.9375 ns/op

// AfterActualRun
WorkloadResult   1: 8192000 op, 910476767.50 ns, 111.1422 ns/op
WorkloadResult   2: 8192000 op, 904079730.50 ns, 110.3613 ns/op
WorkloadResult   3: 8192000 op, 908785439.50 ns, 110.9357 ns/op
// GC:  94 0 0 1572864000 8192000
// Threading:  0 0 8192000

// AfterAll
// Benchmark Process 5225 has exited with code 0.

Mean = 110.813 ns, StdErr = 0.234 ns (0.21%), N = 3, StdDev = 0.405 ns
Min = 110.361 ns, Q1 = 110.649 ns, Median = 110.936 ns, Q3 = 111.039 ns, Max = 111.142 ns
IQR = 0.390 ns, LowerFence = 110.063 ns, UpperFence = 111.625 ns
ConfidenceInterval = [103.431 ns; 118.195 ns] (CI 99.9%), Margin = 7.382 ns (6.66% of Mean)
Skewness = -0.28, Kurtosis = 0.67, MValue = 2

// ** Remained 3 (37.5 %) benchmark(s) to run. Estimated finish 2026-05-04 2:21 (0h 0m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 141 142 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Notification --job ShortRun --benchmarkId 5 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 150547.00 ns, 150.5470 ns/op
WorkloadJitting  1: 1000 op, 1331344.00 ns, 1.3313 us/op

OverheadJitting  2: 16000 op, 165030.00 ns, 10.3144 ns/op
WorkloadJitting  2: 16000 op, 15712972.00 ns, 982.0608 ns/op

WorkloadPilot    1: 16000 op, 12110432.00 ns, 756.9020 ns/op
WorkloadPilot    2: 32000 op, 22722126.00 ns, 710.0664 ns/op
WorkloadPilot    3: 64000 op, 35688122.00 ns, 557.6269 ns/op
WorkloadPilot    4: 128000 op, 70358395.00 ns, 549.6750 ns/op
WorkloadPilot    5: 256000 op, 162776824.00 ns, 635.8470 ns/op
WorkloadPilot    6: 512000 op, 86081372.00 ns, 168.1277 ns/op
WorkloadPilot    7: 1024000 op, 173589562.00 ns, 169.5211 ns/op
WorkloadPilot    8: 2048000 op, 344716408.00 ns, 168.3186 ns/op
WorkloadPilot    9: 4096000 op, 685175657.00 ns, 167.2792 ns/op

OverheadWarmup   1: 4096000 op, 14812.00 ns, 0.0036 ns/op
OverheadWarmup   2: 4096000 op, 7401.00 ns, 0.0018 ns/op
OverheadWarmup   3: 4096000 op, 10686.00 ns, 0.0026 ns/op
OverheadWarmup   4: 4096000 op, 10326.00 ns, 0.0025 ns/op
OverheadWarmup   5: 4096000 op, 7321.00 ns, 0.0018 ns/op
OverheadWarmup   6: 4096000 op, 7271.00 ns, 0.0018 ns/op
OverheadWarmup   7: 4096000 op, 10306.00 ns, 0.0025 ns/op
OverheadWarmup   8: 4096000 op, 10215.00 ns, 0.0025 ns/op

OverheadActual   1: 4096000 op, 10596.00 ns, 0.0026 ns/op
OverheadActual   2: 4096000 op, 10616.00 ns, 0.0026 ns/op
OverheadActual   3: 4096000 op, 11057.00 ns, 0.0027 ns/op
OverheadActual   4: 4096000 op, 10315.00 ns, 0.0025 ns/op
OverheadActual   5: 4096000 op, 10336.00 ns, 0.0025 ns/op
OverheadActual   6: 4096000 op, 10296.00 ns, 0.0025 ns/op
OverheadActual   7: 4096000 op, 10425.00 ns, 0.0025 ns/op
OverheadActual   8: 4096000 op, 10506.00 ns, 0.0026 ns/op
OverheadActual   9: 4096000 op, 13350.00 ns, 0.0033 ns/op
OverheadActual  10: 4096000 op, 12169.00 ns, 0.0030 ns/op
OverheadActual  11: 4096000 op, 12128.00 ns, 0.0030 ns/op
OverheadActual  12: 4096000 op, 10356.00 ns, 0.0025 ns/op
OverheadActual  13: 4096000 op, 10466.00 ns, 0.0026 ns/op
OverheadActual  14: 4096000 op, 10135.00 ns, 0.0025 ns/op
OverheadActual  15: 4096000 op, 10176.00 ns, 0.0025 ns/op

WorkloadWarmup   1: 4096000 op, 697232052.00 ns, 170.2227 ns/op
WorkloadWarmup   2: 4096000 op, 696719845.00 ns, 170.0976 ns/op
WorkloadWarmup   3: 4096000 op, 687869147.00 ns, 167.9368 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 684876701.00 ns, 167.2062 ns/op
WorkloadActual   2: 4096000 op, 690251050.00 ns, 168.5183 ns/op
WorkloadActual   3: 4096000 op, 689857825.00 ns, 168.4223 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 684866235.00 ns, 167.2037 ns/op
WorkloadResult   2: 4096000 op, 690240584.00 ns, 168.5158 ns/op
WorkloadResult   3: 4096000 op, 689847359.00 ns, 168.4198 ns/op
// GC:  105 0 0 1769472000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 5236 has exited with code 0.

Mean = 168.046 ns, StdErr = 0.422 ns (0.25%), N = 3, StdDev = 0.731 ns
Min = 167.204 ns, Q1 = 167.812 ns, Median = 168.420 ns, Q3 = 168.468 ns, Max = 168.516 ns
IQR = 0.656 ns, LowerFence = 166.828 ns, UpperFence = 169.452 ns
ConfidenceInterval = [154.703 ns; 181.390 ns] (CI 99.9%), Margin = 13.344 ns (7.94% of Mean)
Skewness = -0.38, Kurtosis = 0.67, MValue = 2

// ** Remained 2 (25.0 %) benchmark(s) to run. Estimated finish 2026-05-04 2:21 (0h 0m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 141 142 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Request --job ShortRun --benchmarkId 6 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 149686.00 ns, 149.6860 ns/op
WorkloadJitting  1: 1000 op, 1255890.00 ns, 1.2559 us/op

OverheadJitting  2: 16000 op, 183657.00 ns, 11.4786 ns/op
WorkloadJitting  2: 16000 op, 12767422.00 ns, 797.9639 ns/op

WorkloadPilot    1: 16000 op, 10012535.00 ns, 625.7834 ns/op
WorkloadPilot    2: 32000 op, 20772100.00 ns, 649.1281 ns/op
WorkloadPilot    3: 64000 op, 31535941.00 ns, 492.7491 ns/op
WorkloadPilot    4: 128000 op, 61517722.00 ns, 480.6072 ns/op
WorkloadPilot    5: 256000 op, 167280079.00 ns, 653.4378 ns/op
WorkloadPilot    6: 512000 op, 75947269.00 ns, 148.3345 ns/op
WorkloadPilot    7: 1024000 op, 143108277.00 ns, 139.7542 ns/op
WorkloadPilot    8: 2048000 op, 284494549.00 ns, 138.9134 ns/op
WorkloadPilot    9: 4096000 op, 569154198.00 ns, 138.9537 ns/op

OverheadWarmup   1: 4096000 op, 10616.00 ns, 0.0026 ns/op
OverheadWarmup   2: 4096000 op, 7451.00 ns, 0.0018 ns/op
OverheadWarmup   3: 4096000 op, 10426.00 ns, 0.0025 ns/op
OverheadWarmup   4: 4096000 op, 10526.00 ns, 0.0026 ns/op
OverheadWarmup   5: 4096000 op, 10095.00 ns, 0.0025 ns/op
OverheadWarmup   6: 4096000 op, 8293.00 ns, 0.0020 ns/op
OverheadWarmup   7: 4096000 op, 10266.00 ns, 0.0025 ns/op
OverheadWarmup   8: 4096000 op, 10426.00 ns, 0.0025 ns/op
OverheadWarmup   9: 4096000 op, 10346.00 ns, 0.0025 ns/op

OverheadActual   1: 4096000 op, 10656.00 ns, 0.0026 ns/op
OverheadActual   2: 4096000 op, 10486.00 ns, 0.0026 ns/op
OverheadActual   3: 4096000 op, 10506.00 ns, 0.0026 ns/op
OverheadActual   4: 4096000 op, 10406.00 ns, 0.0025 ns/op
OverheadActual   5: 4096000 op, 7512.00 ns, 0.0018 ns/op
OverheadActual   6: 4096000 op, 7261.00 ns, 0.0018 ns/op
OverheadActual   7: 4096000 op, 7201.00 ns, 0.0018 ns/op
OverheadActual   8: 4096000 op, 9174.00 ns, 0.0022 ns/op
OverheadActual   9: 4096000 op, 7572.00 ns, 0.0018 ns/op
OverheadActual  10: 4096000 op, 7261.00 ns, 0.0018 ns/op
OverheadActual  11: 4096000 op, 7391.00 ns, 0.0018 ns/op
OverheadActual  12: 4096000 op, 7431.00 ns, 0.0018 ns/op
OverheadActual  13: 4096000 op, 10105.00 ns, 0.0025 ns/op
OverheadActual  14: 4096000 op, 7381.00 ns, 0.0018 ns/op
OverheadActual  15: 4096000 op, 7361.00 ns, 0.0018 ns/op
OverheadActual  16: 4096000 op, 9814.00 ns, 0.0024 ns/op
OverheadActual  17: 4096000 op, 7351.00 ns, 0.0018 ns/op
OverheadActual  18: 4096000 op, 7461.00 ns, 0.0018 ns/op
OverheadActual  19: 4096000 op, 7461.00 ns, 0.0018 ns/op
OverheadActual  20: 4096000 op, 7311.00 ns, 0.0018 ns/op

WorkloadWarmup   1: 4096000 op, 580079644.00 ns, 141.6210 ns/op
WorkloadWarmup   2: 4096000 op, 578517121.00 ns, 141.2395 ns/op
WorkloadWarmup   3: 4096000 op, 572757959.00 ns, 139.8335 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 572364385.00 ns, 139.7374 ns/op
WorkloadActual   2: 4096000 op, 578561280.00 ns, 141.2503 ns/op
WorkloadActual   3: 4096000 op, 579730740.00 ns, 141.5358 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 572356924.00 ns, 139.7356 ns/op
WorkloadResult   2: 4096000 op, 578553819.00 ns, 141.2485 ns/op
WorkloadResult   3: 4096000 op, 579723279.00 ns, 141.5340 ns/op
// GC:  64 0 0 1081344000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 5245 has exited with code 0.

Mean = 140.839 ns, StdErr = 0.558 ns (0.40%), N = 3, StdDev = 0.967 ns
Min = 139.736 ns, Q1 = 140.492 ns, Median = 141.248 ns, Q3 = 141.391 ns, Max = 141.534 ns
IQR = 0.899 ns, LowerFence = 139.143 ns, UpperFence = 142.740 ns
ConfidenceInterval = [123.207 ns; 158.472 ns] (CI 99.9%), Margin = 17.633 ns (12.52% of Mean)
Skewness = -0.35, Kurtosis = 0.67, MValue = 2

// ** Remained 1 (12.5 %) benchmark(s) to run. Estimated finish 2026-05-04 2:21 (0h 0m from now) **
// **************************
// Benchmark: CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet NetMediate.Benchmarks-Job-CEIKLR-1.dll --anonymousPipes 141 142 --benchmarkName NetMediate.Benchmarks.CoreDispatchBenchmarks.Stream --job ShortRun --benchmarkId 7 in /home/runner/work/net-mediate/net-mediate/tests/NetMediate.Benchmarks/bin/Release/net10.0/NetMediate.Benchmarks-Job-CEIKLR-1/bin/Release/net10.0
// Failed to set up high priority (Permission denied). In order to run benchmarks with high priority, make sure you have the right permissions.
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.15.8
// Runtime=.NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
// Job: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

OverheadJitting  1: 1000 op, 147423.00 ns, 147.4230 ns/op
WorkloadJitting  1: 1000 op, 1509404.00 ns, 1.5094 us/op

OverheadJitting  2: 16000 op, 156136.00 ns, 9.7585 ns/op
WorkloadJitting  2: 16000 op, 19084395.00 ns, 1.1928 us/op

WorkloadPilot    1: 16000 op, 15201200.00 ns, 950.0750 ns/op
WorkloadPilot    2: 32000 op, 31571769.00 ns, 986.6178 ns/op
WorkloadPilot    3: 64000 op, 51382453.00 ns, 802.8508 ns/op
WorkloadPilot    4: 128000 op, 126702818.00 ns, 989.8658 ns/op
WorkloadPilot    5: 256000 op, 107590485.00 ns, 420.2753 ns/op
WorkloadPilot    6: 512000 op, 107774875.00 ns, 210.4978 ns/op
WorkloadPilot    7: 1024000 op, 212150546.00 ns, 207.1783 ns/op
WorkloadPilot    8: 2048000 op, 422062265.00 ns, 206.0851 ns/op
WorkloadPilot    9: 4096000 op, 845114378.00 ns, 206.3268 ns/op

OverheadWarmup   1: 4096000 op, 14222.00 ns, 0.0035 ns/op
OverheadWarmup   2: 4096000 op, 10175.00 ns, 0.0025 ns/op
OverheadWarmup   3: 4096000 op, 10556.00 ns, 0.0026 ns/op
OverheadWarmup   4: 4096000 op, 7311.00 ns, 0.0018 ns/op
OverheadWarmup   5: 4096000 op, 7331.00 ns, 0.0018 ns/op
OverheadWarmup   6: 4096000 op, 10426.00 ns, 0.0025 ns/op
OverheadWarmup   7: 4096000 op, 10206.00 ns, 0.0025 ns/op

OverheadActual   1: 4096000 op, 10436.00 ns, 0.0025 ns/op
OverheadActual   2: 4096000 op, 10746.00 ns, 0.0026 ns/op
OverheadActual   3: 4096000 op, 10716.00 ns, 0.0026 ns/op
OverheadActual   4: 4096000 op, 24247.00 ns, 0.0059 ns/op
OverheadActual   5: 4096000 op, 10576.00 ns, 0.0026 ns/op
OverheadActual   6: 4096000 op, 7351.00 ns, 0.0018 ns/op
OverheadActual   7: 4096000 op, 7381.00 ns, 0.0018 ns/op
OverheadActual   8: 4096000 op, 7391.00 ns, 0.0018 ns/op
OverheadActual   9: 4096000 op, 7301.00 ns, 0.0018 ns/op
OverheadActual  10: 4096000 op, 9074.00 ns, 0.0022 ns/op
OverheadActual  11: 4096000 op, 7271.00 ns, 0.0018 ns/op
OverheadActual  12: 4096000 op, 7321.00 ns, 0.0018 ns/op
OverheadActual  13: 4096000 op, 7251.00 ns, 0.0018 ns/op
OverheadActual  14: 4096000 op, 7311.00 ns, 0.0018 ns/op
OverheadActual  15: 4096000 op, 7311.00 ns, 0.0018 ns/op
OverheadActual  16: 4096000 op, 7331.00 ns, 0.0018 ns/op
OverheadActual  17: 4096000 op, 7412.00 ns, 0.0018 ns/op
OverheadActual  18: 4096000 op, 7331.00 ns, 0.0018 ns/op
OverheadActual  19: 4096000 op, 7381.00 ns, 0.0018 ns/op
OverheadActual  20: 4096000 op, 7391.00 ns, 0.0018 ns/op

WorkloadWarmup   1: 4096000 op, 859039577.00 ns, 209.7265 ns/op
WorkloadWarmup   2: 4096000 op, 860656913.00 ns, 210.1213 ns/op
WorkloadWarmup   3: 4096000 op, 853561598.00 ns, 208.3891 ns/op

// BeforeActualRun
WorkloadActual   1: 4096000 op, 845966878.00 ns, 206.5349 ns/op
WorkloadActual   2: 4096000 op, 840994290.00 ns, 205.3209 ns/op
WorkloadActual   3: 4096000 op, 839297719.00 ns, 204.9067 ns/op

// AfterActualRun
WorkloadResult   1: 4096000 op, 845959497.00 ns, 206.5331 ns/op
WorkloadResult   2: 4096000 op, 840986909.00 ns, 205.3191 ns/op
WorkloadResult   3: 4096000 op, 839290338.00 ns, 204.9049 ns/op
// GC:  88 0 0 1474560000 4096000
// Threading:  0 0 4096000

// AfterAll
// Benchmark Process 5254 has exited with code 0.

Mean = 205.586 ns, StdErr = 0.489 ns (0.24%), N = 3, StdDev = 0.846 ns
Min = 204.905 ns, Q1 = 205.112 ns, Median = 205.319 ns, Q3 = 205.926 ns, Max = 206.533 ns
IQR = 0.814 ns, LowerFence = 203.891 ns, UpperFence = 207.147 ns
ConfidenceInterval = [190.148 ns; 221.024 ns] (CI 99.9%), Margin = 15.438 ns (7.51% of Mean)
Skewness = 0.28, Kurtosis = 0.67, MValue = 2

// ** Remained 0 (0.0 %) benchmark(s) to run. Estimated finish 2026-05-04 2:21 (0h 0m from now) **
// ***** BenchmarkRunner: Finish  *****

// * Export *
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.csv
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report-github.md
  BenchmarkDotNet.Artifacts/results/NetMediate.Benchmarks.CoreDispatchBenchmarks-report.html

// * Detailed results *
CoreDispatchBenchmarks.'Command  Send': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 108.008 ns, StdErr = 0.086 ns (0.08%), N = 14, StdDev = 0.320 ns
Min = 107.604 ns, Q1 = 107.787 ns, Median = 107.952 ns, Q3 = 108.090 ns, Max = 108.704 ns
IQR = 0.303 ns, LowerFence = 107.333 ns, UpperFence = 108.543 ns
ConfidenceInterval = [107.647 ns; 108.369 ns] (CI 99.9%), Margin = 0.361 ns (0.33% of Mean)
Skewness = 0.78, Kurtosis = 2.62, MValue = 2
-------------------- Histogram --------------------
[107.429 ns ; 108.879 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 164.495 ns, StdErr = 0.130 ns (0.08%), N = 13, StdDev = 0.469 ns
Min = 164.007 ns, Q1 = 164.124 ns, Median = 164.328 ns, Q3 = 164.842 ns, Max = 165.441 ns
IQR = 0.718 ns, LowerFence = 163.047 ns, UpperFence = 165.919 ns
ConfidenceInterval = [163.934 ns; 165.057 ns] (CI 99.9%), Margin = 0.561 ns (0.34% of Mean)
Skewness = 0.64, Kurtosis = 1.9, MValue = 2
-------------------- Histogram --------------------
[163.745 ns ; 165.703 ns) | @@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 144.414 ns, StdErr = 0.182 ns (0.13%), N = 14, StdDev = 0.682 ns
Min = 143.657 ns, Q1 = 143.897 ns, Median = 144.250 ns, Q3 = 144.867 ns, Max = 145.738 ns
IQR = 0.969 ns, LowerFence = 142.444 ns, UpperFence = 146.320 ns
ConfidenceInterval = [143.644 ns; 145.183 ns] (CI 99.9%), Margin = 0.770 ns (0.53% of Mean)
Skewness = 0.55, Kurtosis = 1.82, MValue = 2
-------------------- Histogram --------------------
[143.285 ns ; 146.109 ns) | @@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': Job-CEIKLR(RunStrategy=Throughput)
Runtime = .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 201.581 ns, StdErr = 0.231 ns (0.11%), N = 15, StdDev = 0.895 ns
Min = 200.390 ns, Q1 = 201.009 ns, Median = 201.402 ns, Q3 = 202.166 ns, Max = 203.782 ns
IQR = 1.157 ns, LowerFence = 199.273 ns, UpperFence = 203.901 ns
ConfidenceInterval = [200.624 ns; 202.538 ns] (CI 99.9%), Margin = 0.957 ns (0.47% of Mean)
Skewness = 0.78, Kurtosis = 3.02, MValue = 2
-------------------- Histogram --------------------
[199.914 ns ; 204.258 ns) | @@@@@@@@@@@@@@@
---------------------------------------------------

CoreDispatchBenchmarks.'Command  Send': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 110.813 ns, StdErr = 0.234 ns (0.21%), N = 3, StdDev = 0.405 ns
Min = 110.361 ns, Q1 = 110.649 ns, Median = 110.936 ns, Q3 = 111.039 ns, Max = 111.142 ns
IQR = 0.390 ns, LowerFence = 110.063 ns, UpperFence = 111.625 ns
ConfidenceInterval = [103.431 ns; 118.195 ns] (CI 99.9%), Margin = 7.382 ns (6.66% of Mean)
Skewness = -0.28, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[109.993 ns ; 111.511 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Notification  Notify': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 168.046 ns, StdErr = 0.422 ns (0.25%), N = 3, StdDev = 0.731 ns
Min = 167.204 ns, Q1 = 167.812 ns, Median = 168.420 ns, Q3 = 168.468 ns, Max = 168.516 ns
IQR = 0.656 ns, LowerFence = 166.828 ns, UpperFence = 169.452 ns
ConfidenceInterval = [154.703 ns; 181.390 ns] (CI 99.9%), Margin = 13.344 ns (7.94% of Mean)
Skewness = -0.38, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[167.194 ns ; 168.525 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Request  Request': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 140.839 ns, StdErr = 0.558 ns (0.40%), N = 3, StdDev = 0.967 ns
Min = 139.736 ns, Q1 = 140.492 ns, Median = 141.248 ns, Q3 = 141.391 ns, Max = 141.534 ns
IQR = 0.899 ns, LowerFence = 139.143 ns, UpperFence = 142.740 ns
ConfidenceInterval = [123.207 ns; 158.472 ns] (CI 99.9%), Margin = 17.633 ns (12.52% of Mean)
Skewness = -0.35, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[138.856 ns ; 142.414 ns) | @@@
---------------------------------------------------

CoreDispatchBenchmarks.'Stream  RequestStream (3 items/call)': ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
Runtime = .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4; GC = Concurrent Workstation
Mean = 205.586 ns, StdErr = 0.489 ns (0.24%), N = 3, StdDev = 0.846 ns
Min = 204.905 ns, Q1 = 205.112 ns, Median = 205.319 ns, Q3 = 205.926 ns, Max = 206.533 ns
IQR = 0.814 ns, LowerFence = 203.891 ns, UpperFence = 207.147 ns
ConfidenceInterval = [190.148 ns; 221.024 ns] (CI 99.9%), Margin = 15.438 ns (7.51% of Mean)
Skewness = 0.28, Kurtosis = 0.67, MValue = 2
-------------------- Histogram --------------------
[204.135 ns ; 207.303 ns) | @@@
---------------------------------------------------

// * Summary *

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
  Job-CEIKLR : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
  ShortRun   : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4


| Method                                 | Job        | IterationCount | LaunchCount | RunStrategy | WarmupCount | Mean     | Error    | StdDev  | Gen0   | Allocated |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------ |---------:|---------:|--------:|-------:|----------:|
| 'Command  Send'                        | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 108.0 ns |  0.36 ns | 0.32 ns | 0.0115 |     192 B |
| 'Notification  Notify'                 | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 164.5 ns |  0.56 ns | 0.47 ns | 0.0256 |     432 B |
| 'Request  Request'                     | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 144.4 ns |  0.77 ns | 0.68 ns | 0.0156 |     264 B |
| 'Stream  RequestStream (3 items/call)' | Job-CEIKLR | Default        | Default     | Throughput  | Default     | 201.6 ns |  0.96 ns | 0.90 ns | 0.0215 |     360 B |
| 'Command  Send'                        | ShortRun   | 3              | 1           | Default     | 3           | 110.8 ns |  7.38 ns | 0.40 ns | 0.0115 |     192 B |
| 'Notification  Notify'                 | ShortRun   | 3              | 1           | Default     | 3           | 168.0 ns | 13.34 ns | 0.73 ns | 0.0256 |     432 B |
| 'Request  Request'                     | ShortRun   | 3              | 1           | Default     | 3           | 140.8 ns | 17.63 ns | 0.97 ns | 0.0156 |     264 B |
| 'Stream  RequestStream (3 items/call)' | ShortRun   | 3              | 1           | Default     | 3           | 205.6 ns | 15.44 ns | 0.85 ns | 0.0215 |     360 B |

// * Hints *
Outliers
  CoreDispatchBenchmarks.'Command  Send': RunStrategy=Throughput        -> 1 outlier  was  removed (108.84 ns)
  CoreDispatchBenchmarks.'Notification  Notify': RunStrategy=Throughput -> 2 outliers were removed (167.79 ns, 170.50 ns)
  CoreDispatchBenchmarks.'Request  Request': RunStrategy=Throughput     -> 1 outlier  was  removed (147.02 ns)
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
Run time: 00:01:47 (107.38 sec), executed benchmarks: 8

Global total time: 00:01:58 (118.66 sec), executed benchmarks: 8
// * Artifacts cleanup *
Artifacts cleanup is finished
```
