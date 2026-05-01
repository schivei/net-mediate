# Roadmap

This roadmap consolidates improvement ideas and new features for the NetMediate ecosystem.

## Short term

- [x] Expand `NetMediate.Moq` documentation with unit and integration testing recipes. See `docs/NETMEDIATE_MOQ_RECIPES.md`.
- [x] Add sample applications (API, Worker, and Minimal API) using `NetMediate` and `NetMediate.Moq`. See `docs/SAMPLES.md`.
- [x] Cover diagnostics scenarios with structured logs and message-type metrics. See `docs/DIAGNOSTICS.md`.

## Mid term

- [x] Add pipeline behaviors/interceptors (pre/post processing). Implemented through `ICommandBehavior<TMessage>`, `IRequestBehavior<TMessage,TResponse>`, `INotificationBehavior<TMessage>`, and `IStreamBehavior<TMessage,TResponse>` in `src/NetMediate`, validated in `tests/NetMediate.Tests/PipelineBehaviorTests.cs`.
- [x] Include retry, timeout, and circuit-breaker strategies for notification/request handlers. Delivered as a dedicated package (`src/NetMediate.Resilience`) to keep the core mediator package focused and allow optional adoption.
- [x] Provide optional source generator support to reduce reflection cost at startup. Delivered in `src/NetMediate.SourceGeneration` with generated `AddNetMediateGenerated(...)` registration and explicit no-scan registration path via `AddNetMediate(IServiceCollection, Action<IMediatorServiceBuilder>)`.
- [x] Evolve observability support (OpenTelemetry traces/metrics for Send/Request/Notify/Stream). Implemented with built-in `ActivitySource`/`Meter` in `NetMediateDiagnostics` and covered by `tests/NetMediate.Tests/Internals/DiagnosticsTelemetryTests.cs`.

## Long term

- [x] Publish a benchmark suite comparing NetMediate across high-throughput scenarios. See `docs/BENCHMARKS.md` for full results (command, request, and resilience scenarios) and `docs/RESILIENCE.md` for resilience-specific guidance.
- [x] Explore an AOT-friendly mode with trimming and NativeAOT optimizations. Assembly-scanning APIs annotated with `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`; explicit registration and source-generation paths are fully AOT-safe. See `docs/AOT.md`.
- [x] Define an official ecosystem extension track (testing, diagnostics, resilience, and adapters).
- [x] Add integration package for Quartz.NET to persist notifications and enable cluster-distributed execution (`src/NetMediate.Quartz`). See `docs/QUARTZ.md`.
- [x] Add supporting adapter contracts and utilities for delivering notifications to external queue/stream mechanisms (`src/NetMediate.Adapters`). See `docs/ADAPTERS.md`.

## Performance — target 15 M ops/s (net10.0, command, sequential)

Current baseline (GitHub-hosted runner, net10.0, 5-run median): **~450 k ops/s** for `command` (sequential).  
Target: **≥ 15 M ops/s** under the same conditions.

The items below are ordered roughly by expected impact-per-effort.

### Core pipeline — eliminate per-call allocations

- [ ] **Cache the resolved handler list per message type.** `MountPipeline` resolves `IEnumerable<THandler>` / `IEnumerable<TBehavior>` through DI on every call.  Replace with a compile-time-frozen handler cache (`FrozenDictionary<Type, Delegate>` or compiled `Action<…>`) built once at host startup so each dispatch is a single dictionary lookup + delegate invoke.
- [ ] **Eliminate the `IServiceScope` per Send/Request call.** Creating and disposing `IServiceScopeFactory.CreateScope()` on every `Send`/`Request` is the largest single allocator in the hot path.  Move to a lifetime model where singleton handlers skip scoping entirely and a shared scope is used for scoped handlers.
- [ ] **Replace `GetAllServices<T>` LINQ enumeration with pre-built arrays.**  `Extensions.GetAllServices<T>` enumerates `IServiceProvider` on every call.  Cache the resulting `T[]` per message type at first access using a `ConcreteTypeCache<T>`.
- [ ] **Remove `Task.WhenAll` allocation in `Send` for the common single-handler case.**  When there is exactly one `ICommandHandler<T>` registered, call `handler.Handle(…)` directly without the array + `Task.WhenAll` allocation.
- [ ] **Pre-compile the behavior chain per message type** into a single delegate stored in a static generic field, eliminating the per-call `foreach`/`Reverse`/closure chain.

### Notifications — remove channel overhead for in-process scenarios

- [ ] **Introduce a synchronous (fire-and-forget) `INotifiable` implementation** as an alternative to the current `Channel<T>` + `BackgroundService` model.  Register it as `NetMediate.Sync` optional package or a flag on `AddNetMediate`.  Callers that can tolerate synchronous dispatch avoid the channel write + context switch entirely.
- [ ] **Batch-drain the notification channel** in the worker (`TryRead` loop) instead of one item per `WaitToReadAsync` tick to reduce scheduling overhead.

### Reflection / diagnostic overhead

- [ ] **Make `NetMediateDiagnostics` (ActivitySource / Meter) opt-in** via a separate `NetMediate.Diagnostics` package or a build-time constant.  When diagnostics are disabled, remove all `StartActivity` and `RecordXxx` calls from the hot path (currently always-on overhead even when no listener is attached).
- [ ] **Guard `StartActivity` with `ActivitySource.HasListeners`** immediately to avoid allocating `Activity` objects when no trace listener is present.

### Garbage reduction

- [ ] **Pool `ValueTask` continuations** for the common synchronous-handler path (handlers that complete synchronously should return `ValueTask.CompletedTask` without allocating).
- [ ] **Replace `List<T>` / LINQ projections** inside `MountPipeline` and `Mediator.NotifyCore` with `Span<T>`/stack-allocated arrays where the handler count is small and known.
- [ ] **Eliminate `ConfigureAwait(false)` wrapper overhead** — profile whether stripping unnecessary `await`/`ConfigureAwait` in zero-async paths (sync-completing handlers) reduces allocations.

### Footprint / optional packages

- [ ] **Move `NetMediateDiagnostics` (OpenTelemetry instrumentation)** to `NetMediate.Diagnostics` so the core package has zero tracing/metrics dependency.
- [ ] **Move `IValidationHandler<T>` support** to `NetMediate.Validation` so applications that do not use validation skip the `GetAllServices<IValidationHandler<T>>` resolution entirely.
- [ ] **Move `IStreamHandler<T,R>` support** to `NetMediate.Streaming` — streaming is rarely needed in the hot path.

### Benchmarking infrastructure

- [ ] **Add a `PerformanceBenchmarks` project** using BenchmarkDotNet to produce reproducible, artifact-stable results (mean, alloc/op, gen0/1/2 columns) that CI can track over time.
- [ ] **Add a per-commit throughput-regression gate** in CI: if the `command` scenario drops more than 5 % from the previous commit, fail the build.
