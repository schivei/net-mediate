# NetMediate Benchmark Results

> **GenDI pattern:** The benchmark scenarios assume the current NetMediate ecosystem, where startup projects use `NetMediate.SourceGeneration` and supporting services can follow the GenDI `[Injectable]` + `[Inject]` model.

<!-- netmediate-bench-baseline: [{"cmd":90.82,"notify":189.43,"request":89.91,"stream":198.95,"cmd_a":48.0,"notify_a":432.0,"request_a":112.0,"stream_a":216.0},{"cmd":90.55,"notify":102.32,"request":95.3,"stream":196.07,"cmd_a":48.0,"notify_a":112.0,"request_a":112.0,"stream_a":216.0},{"cmd":84.24,"notify":128.85,"request":88.62,"stream":177.08,"cmd_a":48.0,"notify_a":288.0,"request_a":120.0,"stream_a":216.0}] -->

This document describes the performance characteristics of NetMediate under the current implementation, which uses **explicit handler registration only** (no assembly scanning) and **closed-type pipeline executors** registered at startup.

---

## Reference benchmark environment

The table below is updated automatically by CI on every PR benchmark run. System info comes from the BenchmarkDotNet host environment.

<!-- ci-environment-start -->
| Key | Value |
|---|---|
| OS | Linux Ubuntu 24.04.4 LTS (Noble Numbat) |
| CPU | Intel Xeon Platinum 8370C CPU 2.80GHz (Max: 2.75GHz), 1 CPU, 4 logical and 2 physical cores |
| .NET SDK | 10.0.300 |
| Runtime | .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v4 |
| Last CI run | 2026-05-12 23:04 UTC |
| Branch | `copilot/increase-notification-capacity` |
| Commit | `91f7cd7` |
<!-- ci-environment-end -->

---

## Core dispatch throughput

Measured with BenchmarkDotNet (`CoreDispatchBenchmarks`) — no behaviors, no resilience, no adapters registered.
`Mean` is the BenchmarkDotNet Throughput-job mean (ns/op). `Throughput` is the derived ops/s.
`Alloc Δ` compares per-call allocation bytes against the baseline run — allocations are deterministic
and unaffected by CPU load, making this the most reliable regression signal.
The `vs timing` column compares dispatch time against the same-run base-branch measurement when
available, or against stored target-branch values otherwise (±10% = no change on shared CI hardware;
✅ = improved, ⚠️ = degraded).

> Improvement plan for current regressions is tracked in [PERFORMANCE_IMPROVEMENTS.md](PERFORMANCE_IMPROVEMENTS.md).

<!-- ci-throughput-start -->
| Benchmark | Mean | Error | Gen0 | Allocated | Alloc Δ | Throughput | vs timing |
|---|---|---|---|---|---|---|---|
| Command `Send` | 68.28 ns | ±0.314 ns | 0.0012 | 32 B | ✅ -16 B | ~14.6M msg/s | ✅ improved (-24.6%) |
| Request `Request` | 49.87 ns | ±0.186 ns | 0.0029 | 72 B | ✅ -40 B | ~20.1M msg/s | ✅ improved (-44.5%) |
| Stream `RequestStream` | 128.89 ns | ±0.865 ns | 0.0049 | 128 B | ✅ -88 B | ~7.8M msg/s | ✅ improved (-34.3%) |
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

All handlers are registered through source generation and standard DI:

```csharp
builder.Services.AddNetMediate();
```

At startup the generated registrations add the handler implementations plus the corresponding executors:

| Handler kind | Executor registered |
|---|---|
| `ICommandHandler<TMsg>` | `PipelineExecutor<TMsg, Task, ICommandHandler<TMsg>>` |
| `INotificationHandler<TMsg>` | `NotificationPipelineExecutor<TMsg>` |
| `IRequestHandler<TMsg, TResp>` | `RequestPipelineExecutor<TMsg, TResp>` |
| `IStreamHandler<TMsg, TResp>` | `StreamPipelineExecutor<TMsg, TResp>` |

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

Behaviors are registered as closed DI services (for example `IPipelineRequestBehavior<TMessage, TResponse>`) and the resolved behavior arrays are cached per message-result type in the same `ConcurrentDictionary<Type, Lazy<T[]>>` as handlers, so no DI enumeration occurs on the hot path after the first dispatch of a given message type.

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

Run: 2026-05-12 23:04 UTC | Branch: `copilot/increase-notification-capacity` | Commit: `91f7cd7`

> ℹ️ Timing baseline loaded from stored target-branch docs (different run — ±10% is noise).

### System specification

```
Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Platinum 8370C CPU 2.80GHz (Max: 2.75GHz), 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
Runtime: .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v4
```

### Performance summary (BenchmarkDotNet — Throughput job)

| Benchmark | Mean | Error | Gen0 | Allocated | Alloc Δ | Throughput | vs timing |
|---|---|---|---|---|---|---|---|
| Command `Send` | 68.28 ns | ±0.314 ns | 0.0012 | 32 B | ✅ -16 B | ~14.6M msg/s | ✅ improved (-24.6%) |
| Request `Request` | 49.87 ns | ±0.186 ns | 0.0029 | 72 B | ✅ -40 B | ~20.1M msg/s | ✅ improved (-44.5%) |
| Stream `RequestStream` | 128.89 ns | ±0.865 ns | 0.0049 | 128 B | ✅ -88 B | ~7.8M msg/s | ✅ improved (-34.3%) |

### Comparison vs baseline (`main`, median of ≤3 runs)

> Timing: ✅ improved (>10% faster) |  ≈ no change (±10%) |  ⚠️ degraded (>10% slower)
> Alloc Δ: ✅ same / ✅ −N B (less) / ⚠️ +N B (more)

| Benchmark | Baseline (`main`, median of ≤3 runs) | Current | Δ timing | Alloc Δ |
|---|---|---|---|---|
| Command `Send` | 90.55 ns | 68.28 ns | ✅ -24.6% | ✅ -16 B |
| Request `Request` | 89.91 ns | 49.87 ns | ✅ -44.5% | ✅ -40 B |
| Stream `RequestStream` | 196.07 ns | 128.89 ns | ✅ -34.3% | ✅ -88 B |