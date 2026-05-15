# NetMediate Benchmark Results

> **GenDI pattern:** The benchmark scenarios assume the current NetMediate ecosystem, where startup projects use `NetMediate.SourceGeneration` and supporting services can follow the GenDI `[Injectable]` + `[Inject]` model.

<!-- netmediate-bench-baseline: [{"cmd":90.82,"notify":189.43,"request":89.91,"stream":198.95,"cmd_a":48.0,"notify_a":432.0,"request_a":112.0,"stream_a":216.0},{"cmd":90.55,"notify":102.32,"request":95.3,"stream":196.07,"cmd_a":48.0,"notify_a":112.0,"request_a":112.0,"stream_a":216.0},{"cmd":84.24,"notify":128.85,"request":88.62,"stream":177.08,"cmd_a":48.0,"notify_a":288.0,"request_a":120.0,"stream_a":216.0}] -->

This document describes the performance characteristics of NetMediate under the current implementation, which uses **compile-time source generation** (no assembly scanning) and **cached handler resolution** — handler arrays are resolved from DI once per message type and reused on every subsequent dispatch.

---

## Reference benchmark environment

The table below is updated automatically by CI on every PR benchmark run. System info comes from the BenchmarkDotNet host environment.

<!-- ci-environment-start -->
| Key | Value |
|---|---|
| OS | Linux Ubuntu 24.04.4 LTS (Noble Numbat) |
| CPU | AMD EPYC 7763 2.73GHz, 1 CPU, 4 logical and 2 physical cores |
| .NET SDK | 10.0.300 |
| Runtime | .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3 |
| Last CI run | 2026-05-14 00:46 UTC |
| Branch | `copilot/update-documentation-discrepancies` |
| Commit | `74e06a8` |
<!-- ci-environment-end -->

---

## Core dispatch throughput

Measured with BenchmarkDotNet (`CoreDispatchBenchmarks`) — no decorators, no resilience, no adapters registered.
`Mean` is the BenchmarkDotNet ShortRun mean (ns/op). `Throughput` is the derived ops/s.
`Alloc Δ` compares per-call allocation bytes against the baseline — allocations are deterministic
and unaffected by CPU load, making this the most reliable regression signal.
The `vs timing` column compares dispatch time against stored target-branch values
(±10% = no change on shared CI hardware; ✅ = improved, ⚠️ = degraded).

> Improvement plan for current regressions is tracked in [PERFORMANCE_IMPROVEMENTS.md](PERFORMANCE_IMPROVEMENTS.md).

<!-- ci-throughput-start -->
| Benchmark | Mean | Error | Gen0 | Gen1 | Gen2 | Allocated | Alloc Δ | Throughput | vs timing |
|---|---|---|---|---|---|---|---|---|---|
| Command `Send` | 74.69 ns | ±0.252 ns | 0.0018 | 0 | 0 | 32 B | ✅ -16 B | ~13.4M msg/s | ✅ improved (-17.5%) |
| Notification `Notify` | 32.52 ns | ±0.037 ns | 0 | 0 | 0 | - | ✅ -288 B | ~30.8M msg/s | ✅ improved (-74.8%) |
| Request `Request` | 58.47 ns | ±0.919 ns | 0.0062 | 0 | 0 | 104 B | ✅ same | ~17.1M msg/s | ✅ improved (-35.0%) |
| Stream `RequestStream` | 136.63 ns | ±0.771 ns | 0.0076 | 0 | 0 | 128 B | ✅ -88 B | ~7.3M msg/s | ✅ improved (-30.3%) |
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
| `Command Send` | `IMediator.Send<BenchCommand>()` — no decorators |
| `Notification Notify` | `IMediator.Notify<BenchNotification>()` — no decorators |
| `Request Request` | `IMediator.Request<BenchRequest, BenchResponse>()` — no decorators |
| `Stream RequestStream (3 items/call)` | `IMediator.RequestStream<BenchStreamRequest, BenchStreamItem>()` — drains 3 items per invocation |

BenchmarkDotNet output columns: `Method`, `Mean`, `Error`, `StdDev`, `Gen0`, `Gen1`, `Gen2`, `Allocated`.  The `--job Short` flag runs 3 warmup + 3 measured iterations.

---



### Hot-path throughput

Once warm, **JIT and NativeAOT produce identical throughput**. The handler cache (`ConcurrentDictionary<Type, object>` per-Mediator-instance) eliminates DI resolution on the hot path after the first dispatch of each message type. NativeAOT has no advantage or disadvantage in per-message throughput.

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

At startup the source generator registers each handler implementation directly as its interface. Cross-cutting concerns (logging, resilience, etc.) are applied via **GenDI decorators** using `[DecoratorFor]`:

```csharp
[DecoratorFor<ICommandHandler<MyCommand>>]
public sealed class MyCommandDecorator(ICommandHandler<MyCommand> inner) : ICommandHandler<MyCommand>
{
    public async Task Handle(MyCommand message, CancellationToken cancellationToken = default)
    {
        // pre-processing
        await inner.Handle(message, cancellationToken);
        // post-processing
    }
}
```

GenDI registers the decorator chain in DI automatically. No `MakeGenericType`, no assembly scanning — fully NativeAOT-compatible.

---

## Dispatch semantics

| Operation | Method | Semantics |
|---|---|---|
| `Send` | `IMediator.Send<TMsg>` | All `ICommandHandler<TMsg>` instances iterated sequentially |
| `Request` | `IMediator.Request<TMsg, TResp>` | Single `IRequestHandler<TMsg, TResp>` (first registered) |
| `Notify` | `IMediator.Notify<TMsg>` | Fire-and-forget per handler; all `INotificationHandler<TMsg>` instances started individually; exceptions logged |
| `RequestStream` | `IMediator.RequestStream<TMsg, TResp>` | All registered `IStreamHandler<TMsg, TResp>` instances, items merged sequentially |

---

## Handler cache

Resolved handler arrays are cached in **per-Mediator-instance** `ConcurrentDictionary` fields. Since Mediator is a long-lived singleton within its DI container, this gives per-container isolation (no cross-container contamination between test suites or multi-tenant hosts) and eliminates repeated DI enumerations on the hot path.

```
First dispatch for TMsg  →  DI resolution + cache fill  →  O(n) one-time cost
All subsequent calls     →  ConcurrentDictionary read   →  O(1)
```

---

## How to reproduce benchmarks

```bash
NETMEDIATE_RUN_PERFORMANCE_TESTS=true \
dotnet test tests/NetMediate.Tests/ --configuration Release \
  --filter "FullyQualifiedName~BenchmarkSystemInfo" \
  --logger "console;verbosity=detailed"
```

---

## Minimum CI assertions

| Test class | Scenario | Threshold |
|---|---|---:|
| `BenchmarkSystemInfoTests` | System info print | always runs |

Thresholds are deliberately lenient to remain green on any CI hardware. The BenchmarkDotNet `--job Short` run on every PR provides the authoritative throughput numbers and regression gate.

---

## See Also

- [RESILIENCE.md](RESILIENCE.md) — resilience package guide
- [AOT.md](AOT.md) — AOT/NativeAOT compatibility guide
- [SOURCE_GENERATION.md](SOURCE_GENERATION.md) — source generator guide

---

## Latest CI Benchmark Run

Run: 2026-05-14 00:46 UTC | Branch: `copilot/update-documentation-discrepancies` | Commit: `74e06a8`

> ℹ️ Timing baseline loaded from stored target-branch docs (different run — ±10% is noise).

### System specification

```
Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.73GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
Runtime: .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
```

### Performance summary (BenchmarkDotNet — ShortRun job)

| Benchmark | Mean | Error | Gen0 | Gen1 | Gen2 | Allocated | Alloc Δ | Throughput | vs timing |
|---|---|---|---|---|---|---|---|---|---|
| Command `Send` | 74.69 ns | ±0.252 ns | 0.0018 | 0 | 0 | 32 B | ✅ -16 B | ~13.4M msg/s | ✅ improved (-17.5%) |
| Notification `Notify` | 32.52 ns | ±0.037 ns | 0 | 0 | 0 | - | ✅ -288 B | ~30.8M msg/s | ✅ improved (-74.8%) |
| Request `Request` | 58.47 ns | ±0.919 ns | 0.0062 | 0 | 0 | 104 B | ✅ same | ~17.1M msg/s | ✅ improved (-35.0%) |
| Stream `RequestStream` | 136.63 ns | ±0.771 ns | 0.0076 | 0 | 0 | 128 B | ✅ -88 B | ~7.3M msg/s | ✅ improved (-30.3%) |

### Comparison vs baseline (`main`, median of ≤3 runs)

> Timing: ✅ improved (>10% faster) |  ≈ no change (±10%) |  ⚠️ degraded (>10% slower)
> Alloc Δ: ✅ same / ✅ −N B (less) / ⚠️ +N B (more)

| Benchmark | Baseline (`main`, median of ≤3 runs) | Current | Δ timing | Alloc Δ |
|---|---|---|---|---|
| Command `Send` | 90.55 ns | 74.69 ns | ✅ -17.5% | ✅ -16 B |
| Notification `Notify` | 128.85 ns | 32.52 ns | ✅ -74.8% | ✅ -288 B |
| Request `Request` | 89.91 ns | 58.47 ns | ✅ -35.0% | ✅ same |
| Stream `RequestStream` | 196.07 ns | 136.63 ns | ✅ -30.3% | ✅ -88 B |