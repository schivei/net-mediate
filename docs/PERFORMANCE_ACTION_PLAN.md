# Performance Action Plan: Achieving 15 M ops/s

> **Status:** Analysis for next PR — no code changes in this document.  
> **Current baseline (net10.0, telemetry + validation disabled):** ~500 K ops/s  
> **Target:** ≥ 15 M ops/s for the source-generated dispatch path  
> **Reference competitors:** martinothamar/Mediator 3 (23 M ops/s), TurboMediator (19 M ops/s, net8.0)

---

## 1. Hot-Path Profiling — Where the Time Goes Today

Every `Send<TMessage>` call with no behaviors, telemetry disabled, and validation disabled still
executes the following work **before reaching handler code**:

| Step | Location | Cost class |
|------|----------|-----------|
| `configuration.EnableTelemetry` field read | `Mediator.Send` L130 | Negligible |
| `ValidateMessage` — `NeedsValidation<T>` check | `Configuration.cs` L53 | `ConcurrentDictionary.GetOrAdd` — volatile read + hash |
| `logger.IsEnabled(LogLevel.Debug)` | `Mediator.Send` L138 | Virtual interface call (not inlined) |
| `message.GetType()` inside `Resolve<T>` | `Mediator.cs` L498 | Boxing for value-type messages; virtual call |
| `logger.IsEnabled(LogLevel.Debug)` (again) | `Mediator.cs` L501 | Second virtual call per dispatch |
| `GetServiceKey(messageType)` — `ConcurrentDictionary.GetOrAdd` | `Mediator.cs` L507 | Volatile read + hash + reflection on first call |
| **`sp.GetServices<T>().ToArray()`** | `Mediator.cs` L514 | **Allocates `IEnumerable<T>` + array every call** |
| `FilterResolves` — `logger.IsEnabled` (third call) | `Mediator.cs` L537 | Third virtual logger call |
| `configuration.TryGetHandlerTypeByMessageFilter` | `Mediator.cs` L542 | `Dictionary<Type, Func<>>` lookup |
| **`HasBehaviors<TBehavior>()`** | `Mediator.cs` L148 | `IServiceProviderIsService.IsService()` — DI hash lookup |
| `async/await` state machine allocation | `Mediator.Send` | Heap allocation for the `Task` + state machine |
| `handler.Handle(msg, ct)` interface call | handler | Virtual interface dispatch |

### Root-cause summary

The three dominant costs that prevent reaching 15 M ops/s are:

1. **`GetServices<T>().ToArray()` — heap allocation every call.**  
   Even for a Singleton handler this goes through the DI engine's `IEnumerable<T>` wrapper and
   produces a fresh array.  The source-generated libraries avoid this entirely by storing handler
   references in static fields at startup.

2. **`HasBehaviors<TBehavior>()` — DI lookup every call.**  
   Called on every dispatch to decide whether to create a scope and run the pipeline.  If behaviors
   are never registered this check is useless overhead.  It must be eliminated at compile time.

3. **`async Task` state machine per call.**  
   Even when the handler completes synchronously, the compiler emits a heap-allocated state machine.
   Switching to `ValueTask` allows the JIT to avoid the allocation on the synchronous fast path.

---

## 2. Reference Architecture: How Source-Generated Libraries Achieve 23 M ops/s

martinothamar/Mediator's generator emits code equivalent to:

```csharp
// Generated at compile time — no DI, no virtual calls, no allocations
internal sealed class GeneratedMediator : IMediator
{
    private readonly MyCommandHandler _myCommandHandler;

    public GeneratedMediator(MyCommandHandler handler) // injected once
        => _myCommandHandler = handler;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask Send(MyCommand command, CancellationToken ct)
        => _myCommandHandler.Handle(command, ct);
}
```

Key design decisions:
- Handler stored as **typed field** — no virtual dispatch on resolution.
- Return type is **`ValueTask`** — no heap allocation when the handler is synchronous.
- Dispatch is a **single method call** — no pipeline check, no behavior check, no logger call.
- The generator knows at compile time whether telemetry / behaviors are registered and emits
  **no code** for disabled features.

---

## 3. Action Plan

The plan is divided into four phases, each independently shippable.  Each phase is a separate PR.

---

### Phase 1 — Source-Generated Handler Resolution (target: ~2 M ops/s, 4× gain)

**Goal:** Eliminate `GetServices<T>().ToArray()` on the source-generated path by injecting handler
references directly via constructor parameters and storing them in typed fields.

#### 1.1 — Generator emits `IMediator` implementation class

The `NetMediateRegistrationGenerator` must emit a sealed `GeneratedMediator` class (alongside the
existing `AddNetMediateGenerated()` extension) that:

- Receives every registered handler as a constructor parameter with its concrete type.
- Stores each handler in a **typed `readonly` field**.
- Overrides `Send<TMessage>`, `Request<TMessage, TResponse>`, `Notify<TMessage>`, and
  `RequestStream<TMessage, TResponse>` with switch-on-`RuntimeTypeHandle` dispatch:

```csharp
// Generated
public override Task Send<TMessage>(TMessage message, CancellationToken ct = default)
{
    if (typeof(TMessage) == typeof(CreateUserCommand))
        return Unsafe.As<ICommandHandler<CreateUserCommand>>(_createUserHandler)
                     .Handle(Unsafe.As<TMessage, CreateUserCommand>(ref message), ct);

    // ... other cases
    return base.Send(message, ct); // fallback to DI for unrecognised types
}
```

The `typeof(TMessage) == typeof(X)` comparison is **folded to a constant by the JIT** when `TMessage`
is known at the call site, making this a zero-overhead branch.

#### 1.2 — Register `GeneratedMediator` in `AddNetMediateGenerated()`

```csharp
services.TryAddSingleton<GeneratedMediator>();
services.TryAddSingleton<IMediator>(sp => sp.GetRequiredService<GeneratedMediator>());
```

#### 1.3 — Estimated improvement

Removes `GetServices<T>().ToArray()` (one heap allocation + DI scan) and `GetServiceKey()`
(`ConcurrentDictionary` lookup) on every dispatch.  Expected to increase throughput to **~2 M ops/s**
(matching or exceeding MediatR 14).

---

### Phase 2 — ValueTask Hot Path + Inline Elimination of Unused Features (target: ~5 M ops/s)

**Goal:** Eliminate async state machine allocations on the synchronous fast path and remove all
runtime feature-flag checks from the generated dispatch path.

#### 2.1 — Change handler interfaces to return `ValueTask`

**Breaking change.** All existing handler code must update return types.

```csharp
// Before
public interface ICommandHandler<TMessage>
{
    Task Handle(TMessage message, CancellationToken ct = default);
}

// After
public interface ICommandHandler<TMessage>
{
    ValueTask Handle(TMessage message, CancellationToken ct = default);
}
```

Similarly for `IRequestHandler<TMessage, TResponse>` → `ValueTask<TResponse>`.

The `NetMediate.Compat` adapter wraps the `ValueTask` return in a `Task` for MediatR-compatible
callers.

#### 2.2 — Generator emits compile-time feature guards

The generator reads the `DisableTelemetry()` / `DisableValidation()` builder calls from the
compilation unit's syntax (via `AdditionalTextsProvider` or an assembly attribute) and emits:

```csharp
// Generated — telemetry disabled at compile time
private const bool _telemetryEnabled = false;

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public override ValueTask Send<TMessage>(TMessage message, CancellationToken ct = default)
{
    // No telemetry code emitted at all when _telemetryEnabled == false
    return typeof(TMessage) == typeof(CreateUserCommand)
        ? _createUserHandler.Handle(Unsafe.As<TMessage, CreateUserCommand>(ref message), ct)
        : base.Send(message, ct);
}
```

The constant fold eliminates the branch entirely from the JIT output.

#### 2.3 — Generator emits compile-time behavior guard

The generator checks whether any `ICommandBehavior<T>`, `IRequestBehavior<T, R>`, etc. are
registered.  If **none** are registered, it emits the handler call directly without any
`HasBehaviors` check:

```csharp
// Generated — no behaviors registered for CreateUserCommand
public ValueTask Send_CreateUserCommand(CreateUserCommand msg, CancellationToken ct)
    => _createUserHandler.Handle(msg, ct);
```

#### 2.4 — Estimated improvement

Eliminates the async state machine allocation for synchronous no-op handlers and removes all
runtime checks.  Expected throughput: **~5 M ops/s**.

---

### Phase 3 — `FrozenDictionary` Fast Lookup for the Non-Generated Path (target: ~3× current)

**Goal:** For users who do **not** use the source generator, replace `GetServices<T>().ToArray()` 
with a pre-built `FrozenDictionary<RuntimeTypeHandle, object>` populated at DI build time.

#### 3.1 — Build a handler registry at startup

```csharp
// In MediatorServiceBuilder.Build() / IStartupFilter
var registry = new HandlerRegistry();
registry.Freeze(serviceProvider); // builds FrozenDictionary from all registered handlers
services.AddSingleton(registry);
```

The `FrozenDictionary` has ~O(1) lookup with no allocation and its perfect-hash generation makes
it faster than `ConcurrentDictionary` for read-heavy workloads.

#### 3.2 — `Mediator.Resolve<T>` uses the registry

```csharp
private T ResolveOne<T>(object message)
{
    var handle = typeof(T).TypeHandle;
    return (T)_registry.GetHandler(handle); // FrozenDictionary, zero allocation
}
```

#### 3.3 — Estimated improvement

Removes the DI scan from the non-generated path.  Expected throughput for non-generated:
**~1.5 M ops/s** (3× current).

---

### Phase 4 — Full Source-Generated Switch Dispatch (target: ≥ 15 M ops/s)

**Goal:** Complete rewrite of the generated `IMediator` implementation using a switch table over
`Type.TypeHandle` values — the same technique used by martinothamar/Mediator.

#### 4.1 — Type-handle switch pattern

```csharp
// Generated dispatch — after Phase 1+2 changes + ValueTask
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public override ValueTask Send<TMessage>(TMessage message, CancellationToken ct)
{
    // RuntimeHelpers.GetHashCode(typeof(TMessage)) is a compile-time constant
    // when TMessage is a closed generic type known at the call site.
    // The JIT folds this to a single conditional branch or inline.
    if (typeof(TMessage) == typeof(CreateUserCommand))
    {
        ref var m = ref Unsafe.As<TMessage, CreateUserCommand>(ref message);
        return _createUserHandler.Handle(m, ct);
    }
    if (typeof(TMessage) == typeof(UpdateUserCommand))
    {
        ref var m = ref Unsafe.As<TMessage, UpdateUserCommand>(ref message);
        return _updateUserHandler.Handle(m, ct);
    }
    return base.Send(message, ct); // unknown type, fall through to DI
}
```

#### 4.2 — Handler fields as `readonly` struct wrappers (optional micro-optimisation)

If handler implementations are `sealed` classes with no virtual methods, the JIT can devirtualise
the `Handle` call entirely when the field type is the concrete class, not the interface:

```csharp
// Generated field — concrete type, not interface
private readonly CreateUserCommandHandler _createUserHandler;
```

#### 4.3 — Remove `serviceProvider` dependency from `GeneratedMediator`

The generated mediator no longer needs `IServiceProvider` for the common dispatch path.
`IServiceProvider` is still injected but only used for the `base.Send` fallback.

#### 4.4 — Eliminate `ILogger` call on the fast path

In the generated path, all `logger.IsEnabled` / `logger.LogDebug` calls are omitted when the
generator detects that `DisableLogging()` or `LogLevel.Warning+` is configured:

```csharp
// Generated — logging level above Debug (common production setting)
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public override ValueTask Send<TMessage>(TMessage message, CancellationToken ct)
{
    // No logger calls — fully eliminated at source generation time
    if (typeof(TMessage) == typeof(CreateUserCommand))
        return _createUserHandler.Handle(Unsafe.As<TMessage, CreateUserCommand>(ref message), ct);
    ...
}
```

#### 4.5 — Estimated improvement

After Phases 1–3, the remaining overhead is primarily the method-call indirection through
`IMediator` and the `async Task` wrapper (fixed in Phase 2).  With all four phases applied:

| Component removed | Estimated cycles saved per call |
|-------------------|---------------------------------|
| `GetServices<T>().ToArray()` | ~150–300 cycles |
| Async state machine (Task → ValueTask) | ~80–200 cycles |
| `HasBehaviors` DI lookup | ~50–100 cycles |
| `logger.IsEnabled` × 3 | ~30–60 cycles |
| `ConcurrentDictionary` lookups × 2 | ~20–40 cycles |
| Virtual dispatch on handler | ~5–10 cycles |

At ~3.5 GHz this corresponds to moving from ~500 K to **15–20 M ops/s** for the no-op no-overhead
benchmark case.

---

## 4. Breaking Changes Required

| Change | Severity | Migration |
|--------|----------|-----------|
| `ICommandHandler<T>.Handle` → `ValueTask` | **Breaking** | Replace `Task` with `ValueTask`; wrap synchronous results in `ValueTask.CompletedTask` |
| `IRequestHandler<T,R>.Handle` → `ValueTask<R>` | **Breaking** | Replace `Task<R>` with `ValueTask<R>`; wrap with `ValueTask.FromResult(value)` |
| `INotificationHandler<T>.Handle` → `ValueTask` | **Breaking** | Same as command |
| `IStreamHandler<T,R>.Handle` signature unchanged | None | `IAsyncEnumerable<R>` stays the same |
| `NetMediate.Compat` adapter updated | Internal only | Compat wraps `ValueTask` in `Task` automatically |
| Source generator must be re-run after any handler change | Build-time only | `dotnet build` re-runs generator |

---

## 5. Non-Breaking Micro-Optimisations (can ship incrementally)

These changes do not require interface changes and can ship in the current PR or a quick-follow PR:

| Optimisation | File | Expected gain |
|---|---|---|
| Pre-cache `typeof(T).TypeHandle` → handler reference in `HandlerRegistry` using `FrozenDictionary` | `Mediator.cs` | Replaces `GetServices<T>` allocation — moderate |
| Replace `logger.IsEnabled` guard with `AggressiveInlining` + `bool` field cache | `Mediator.cs` | Minor, removes virtual call |
| Replace `s_serviceKeyCache.GetOrAdd` with `FrozenDictionary<Type, string?>` (built at startup) | `Mediator.cs` | Minor |
| Replace `_filters Dictionary` with `FrozenDictionary` | `Configuration.cs` | Minor for read-heavy workloads |
| Add `[MethodImpl(AggressiveInlining)]` to `Send`, `Request` generated overrides | Generator | Allows JIT to fold type checks |
| Mark `Mediator` `sealed` | `Mediator.cs` | Allows JIT to devirtualise internal calls |

---

## 6. Implementation Priorities (suggested PR sequence)

| PR | Changes | Expected throughput |
|----|---------|-------------------|
| **This PR** (current) | Documentation only | ~500 K ops/s (baseline) |
| **PR N+1** | Phase 3: `FrozenDictionary` handler registry (non-breaking) | ~1.5 M ops/s |
| **PR N+2** | Phase 1: Generator emits `GeneratedMediator` with typed fields | ~2–3 M ops/s |
| **PR N+3** | Phase 2a: `ValueTask` interface change (breaking) | ~5–8 M ops/s |
| **PR N+4** | Phase 2b: Generator emits compile-time feature guards | ~8–12 M ops/s |
| **PR N+5** | Phase 4: Full switch-table dispatch in generator | ≥ 15 M ops/s |

---

## 7. Measuring Progress

Run the benchmark suite with:

```bash
NETMEDIATE_RUN_PERFORMANCE_TESTS=true dotnet run \
    --project tests/NetMediate.Benchmarks \
    --configuration Release \
    -- --filter "LibraryBenchmarkTests.*" --exporters json
```

The `docs/BENCHMARK_COMPARISON.md` is auto-updated from the benchmark output.

Key BenchmarkDotNet metrics to watch per phase:

| Metric | Target |
|--------|--------|
| `Mean` (ns/op) | ≤ 66 ns/op (= 15 M ops/s at 1 GHz equivalent) |
| `Allocated` (bytes) | 0 on the generated synchronous fast path |
| `Gen0` collections | 0 per 1000 ops |

---

## 8. Risk Register

| Risk | Mitigation |
|------|-----------|
| ValueTask breaking change causes migration friction | Provide `NetMediate.Compat` adapter; publish migration guide; increment major version |
| Generator emits incorrect dispatch for edge cases (keyed services, filters) | All generated paths fall through to `base.Send` for unrecognised patterns; 100% test coverage required |
| AOT / trimmer incompatibility with `Unsafe.As` casts | Cast is safe as long as type identity is verified by the `typeof(TMessage) ==` guard; no IL rewriting needed |
| `FrozenDictionary` build time at startup increases | Only built when `NetMediate.SourceGeneration` is not used; invisible when using the generator |
| TFM compatibility — `FrozenDictionary` requires net8+ | Provide polyfill for netstandard2.0 via `System.Collections.Frozen` NuGet package |
