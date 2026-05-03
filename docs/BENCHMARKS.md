# NetMediate Benchmark Results

This document describes the performance characteristics of NetMediate under the current implementation, which uses **explicit handler registration only** (no assembly scanning) and **closed-type pipeline executors** registered at startup.

---

## Implementation model

All handlers are registered explicitly via `IMediatorServiceBuilder` methods or the source generator:

```csharp
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterCommandHandler<MyCommandHandler, MyCommand>();
    configure.RegisterRequestHandler<MyRequestHandler, MyRequest, MyResponse>();
    configure.RegisterNotificationHandler<MyNotificationHandler, MyNotification>();
    configure.RegisterStreamHandler<MyStreamHandler, MyStream, MyItem>();
});

// Or via source generator (identical registrations, generated at compile time)
builder.Services.AddNetMediateGenerated();
```

At startup each `Register*Handler<>` call performs two `TryAddTransient<>` registrations:

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
| `Notify` | `IMediator.Notify<TMsg>` | Fire-and-forget; all `INotificationHandler<TMsg>` iterated sequentially; exceptions logged |
| `RequestStream` | `IMediator.RequestStream<TMsg, TResp>` | Single `IStreamHandler<TMsg, TResp>`; yields items lazily |

---

## Pipeline behavior resolution

### Command pipeline (`PipelineExecutor<TMsg, Task, ICommandHandler<TMsg>>`)

Resolves `IPipelineBehavior<TMsg, Task>` — two-parameter closed-type lookup.

### Notification pipeline (`NotificationPipelineExecutor<TMsg>`)

Resolves both:
1. `IPipelineBehavior<TMsg, Task>` — two-parameter closed-type lookup
2. `IPipelineBehavior<TMsg>` — one-parameter open-generic (adapters, resilience notification behaviors)

This dual lookup is done inside the executor's constructor-injected types — no runtime type switches.

### Request pipeline (`RequestPipelineExecutor<TMsg, TResp>`)

Resolves both:
1. `IPipelineBehavior<TMsg, Task<TResp>>` — two-parameter closed-type lookup
2. `IPipelineRequestBehavior<TMsg, TResp>` — open-generic (resilience request behaviors)

### Stream pipeline (`StreamPipelineExecutor<TMsg, TResp>`)

Resolves both:
1. `IPipelineBehavior<TMsg, IAsyncEnumerable<TResp>>` — two-parameter closed-type lookup
2. `IPipelineStreamBehavior<TMsg, TResp>` — open-generic

---

## Handler cache

Resolved handler arrays are cached per service type in a `MemoryCache` instance shared across the application lifetime. The cache expiry is adaptive: it starts at 5 minutes and grows by 1 minute per access, capped at 1 hour. This means a frequently-called handler's array is kept resident with no DI lookup cost.

---

## How to reproduce benchmarks

Install the test dependencies and run the performance suites:

```bash
NETMEDIATE_RUN_PERFORMANCE_TESTS=true dotnet test tests/NetMediate.Tests/ \
  --configuration Release \
  --filter "FullyQualifiedName~LoadPerformance OR FullyQualifiedName~PipelineVariants OR FullyQualifiedName~ExplicitRegistration" \
  --logger "console;verbosity=detailed"
```

Output lines of interest:

```
LOAD_RESULT <scenario> tfm=<tfm> ops=<count> elapsed_ms=<ms> throughput_ops_s=<ops/s>
SYSTEM_INFO <key>=<value>
```

---

## Minimum CI assertions

| Test class | Scenario | Threshold |
|---|---|---:|
| `LoadPerformanceTests` | all | `> 500 ops/s` |
| `CoreExplicitRegistrationLoadTests` | all | `> 500 ops/s` |
| `AdaptersLoadPerformanceTests` | all | `> 500 ops/s` |
| `ResilienceLoadPerformanceTests` | `resilience_request_parallel` | `≥ 30,000 ops/s` |
| `FullStackLoadPerformanceTests` | `fullstack_request_parallel` | `≥ 20,000 ops/s` |
| `PipelineVariantsLoadTests` | all | `> 500 ops/s` |

Thresholds are deliberately lenient to remain green on any CI hardware. Local developer machines typically produce 2–5× higher throughput.

---

## AOT / NativeAOT notes

Per-call throughput is identical between JIT and NativeAOT once the process is running. The difference is startup:

| Aspect | JIT | NativeAOT |
|---|---|---|
| Cold-start | JIT compilation | Pre-compiled; fastest startup |
| Startup overhead | None (explicit registration only) | None |
| Throughput (warm) | Same | Same |
| Compatible APIs | All | Explicit registration + source generator |

---

## See Also

- [RESILIENCE.md](RESILIENCE.md) — resilience package guide
- [ADAPTERS.md](ADAPTERS.md) — adapters package guide
- [AOT.md](AOT.md) — AOT/NativeAOT compatibility guide
- [SOURCE_GENERATION.md](SOURCE_GENERATION.md) — source generator guide

