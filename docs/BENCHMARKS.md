# NetMediate Benchmark Results

<!-- netmediate-bench-baseline: [{"cmd":84.24,"notify":128.85,"request":88.62,"stream":177.08,"cmd_a":48.0,"notify_a":288.0,"request_a":120.0,"stream_a":216.0},{"cmd":67.23,"notify":100.77,"request":68.72,"stream":136.65,"cmd_a":48.0,"notify_a":288.0,"request_a":120.0,"stream_a":216.0}] -->

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
| Last CI run | 2026-05-05 23:51 UTC |
| Branch | `feature/service-order-long-term` |
| Commit | `ce0e595` |
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

<!-- ci-throughput-start -->
| Benchmark | Mean | Error | Gen0 | Allocated | Alloc Δ | Throughput | vs timing |
|---|---|---|---|---|---|---|---|
| Command `Send` | 89.56 ns | ±0.114 ns | 0.0028 | 48 B | — | ~11.2M msg/s | ≈ (+4.2%) |
| Notification `Notify` | 145.94 ns | ±0.831 ns | 0.0171 | 288 B | — | ~6.9M msg/s | ⚠️ degraded (+10.4%) |
| Request `Request` | 103.55 ns | ±0.407 ns | 0.0071 | 120 B | — | ~9.7M msg/s | ⚠️ degraded (+18.5%) |
| Stream `RequestStream` | 187.37 ns | ±0.695 ns | 0.0127 | 216 B | — | ~5.3M msg/s | ≈ (+0.8%) |
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

Run: 2026-05-05 23:51 UTC | Branch: `feature/service-order-long-term` | Commit: `ce0e595`

> ℹ️ Timing baseline loaded from stored target-branch docs (different run — ±10% is noise).

### System specification

```
Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.203
Runtime: .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
```

### Performance summary (BenchmarkDotNet — Throughput job)

| Benchmark | Mean | Error | Gen0 | Allocated | Alloc Δ | Throughput | vs timing |
|---|---|---|---|---|---|---|---|
| Command `Send` | 89.56 ns | ±0.114 ns | 0.0028 | 48 B | — | ~11.2M msg/s | ≈ (+4.2%) |
| Notification `Notify` | 145.94 ns | ±0.831 ns | 0.0171 | 288 B | — | ~6.9M msg/s | ⚠️ degraded (+10.4%) |
| Request `Request` | 103.55 ns | ±0.407 ns | 0.0071 | 120 B | — | ~9.7M msg/s | ⚠️ degraded (+18.5%) |
| Stream `RequestStream` | 187.37 ns | ±0.695 ns | 0.0127 | 216 B | — | ~5.3M msg/s | ≈ (+0.8%) |

### Comparison vs baseline (`main`, median of ≤3 runs)

> Timing: ✅ improved (>10% faster) |  ≈ no change (±10%) |  ⚠️ degraded (>10% slower)
> Alloc Δ: ✅ same / ✅ −N B (less) / ⚠️ +N B (more)

| Benchmark | Baseline (`main`, median of ≤3 runs) | Current | Δ timing | Alloc Δ |
|---|---|---|---|---|
| Command `Send` | 85.91 ns | 89.56 ns | ≈ +4.2% | — |
| Notification `Notify` | 132.22 ns | 145.94 ns | ⚠️ +10.4% | — |
| Request `Request` | 87.39 ns | 103.55 ns | ⚠️ +18.5% | — |
| Stream `RequestStream` | 185.95 ns | 187.37 ns | ≈ +0.8% | — |