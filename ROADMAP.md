# Roadmap

This roadmap consolidates improvement ideas and new features for the NetMediate ecosystem.

## Completed

- [x] Pipeline behaviors/interceptors (pre/post processing) via `IPipelineBehavior<TMessage, TResult>`, `IPipelineBehavior<TMessage>`, `IPipelineRequestBehavior<TMessage, TResponse>`, and `IPipelineStreamBehavior<TMessage, TResponse>`.
- [x] Retry, timeout, and circuit-breaker strategies for notification/request handlers via `NetMediate.Resilience`.
- [x] Source generator support (`NetMediate.SourceGeneration`) — generates `AddNetMediateGenerated()` with fully AOT-safe closed-type `Register*Handler<>` calls at compile time.
- [x] OpenTelemetry traces and metrics for `Send`/`Request`/`Notify`/`RequestStream` via built-in `ActivitySource`/`Meter` (`NetMediateDiagnostics`).
- [x] Benchmark suite with load and pipeline-variant tests covering commands, requests, notifications, and streams.
- [x] NativeAOT and trimming compatibility — no `MakeGenericType`, no assembly scanning, no `typeof(TResult)` runtime switches; closed-type executors registered per handler at startup.
- [x] Quartz.NET integration (`NetMediate.Quartz`) for persistent, crash-recoverable, and cluster-distributed notification execution.
- [x] Notification adapter contracts and utilities (`NetMediate.Adapters`) for forwarding notifications to external queues and streams.
- [x] DataDog integration packages (`NetMediate.DataDog.OpenTelemetry`, `NetMediate.DataDog.Serilog`, `NetMediate.DataDog.ILogger`).
- [x] `NetMediate.Moq` helper package with fluent async setup extensions and mediator mock registration.
- [x] Marker-interface-free messaging — any plain class or record can be a message type.
- [x] `Task`-based handler contracts — all handler `Handle` methods return `Task` or `Task<TResponse>`.
- [x] Dedicated `NotificationPipelineExecutor<TMessage>` — resolves both `IPipelineBehavior<TMessage, Task>` and `IPipelineBehavior<TMessage>` without a runtime type switch, keeping the notification pipeline AOT-safe.
- [x] Sample applications (API, Worker, Minimal API) in `docs/SAMPLES.md`.
- [x] Full documentation suite: installation, configuration, resilience, adapters, source generation, AOT, DataDog, Moq recipes, diagnostics, Quartz, benchmarks.

## Near term

- [ ] **Coverage gate** — enforce 100 % line coverage for `src/NetMediate` in CI so no internal path goes untested.
- [x] **BenchmarkDotNet suite** — dedicated `NetMediate.Benchmarks` console project with `CoreDispatchBenchmarks` covering command, notification, request, and stream; `[MemoryDiagnoser]` reports mean, alloc/op, gen0; supports both JIT and NativeAOT runs via `-p:AotBenchmark=true`.
- [ ] **Per-commit throughput regression gate** — fail CI if the `command` scenario drops more than 5 % from the previous commit baseline.

## Medium term

- [ ] **Synchronous fire-and-forget notifier** — optional `INotifiable` implementation that dispatches notification handlers inline (no `Channel<T>` + `BackgroundService` overhead) for scenarios where latency matters more than isolation.
- [ ] **Pre-compiled behavior chain** — build the behavior delegate chain once at startup per message type and cache it in a static generic field, eliminating the per-call `Reverse`/`Aggregate`/closure allocation.
- [ ] **Single-handler fast path for `Send`** — when exactly one `ICommandHandler<T>` is registered, invoke it directly without the `foreach` loop.
- [ ] **`IPipelineNotificationBehavior<TMessage>` shorthand** — a dedicated interface mirroring `IPipelineRequestBehavior<,>` so notification-specific behaviors have a symmetric registration experience.
- [ ] **Structured error context** — surface handler exceptions through a typed `MediatorException` carrying the originating message type, handler type, and activity trace ID.

## Long term

- [x] **`NetMediate.Diagnostics` package** — `NetMediateDiagnostics` (`ActivitySource`/`Meter`) extracted from the core assembly into `NetMediate.Diagnostics`; implemented as pipeline behaviors (`TelemetryNotificationBehavior`, `TelemetryRequestBehavior`, `TelemetryStreamBehavior`); auto-registered by the source generator when the package is referenced (first in pipeline order).
- [ ] **`NetMediate.Validation` package** — optional validation layer implemented as a pipeline behavior, decoupled from the core package and compatible with FluentValidation, DataAnnotations, or custom validators.
- [ ] **Streaming fan-out** — allow multiple `IStreamHandler<TMsg, TResp>` registrations whose items are merged into a single `IAsyncEnumerable<TResp>`, analogous to how `Send` fans out to multiple command handlers.
- [ ] **Keyed handler registration** — support `IKeyedServiceProvider` so multiple handlers for the same message type can be addressed by a named key, enabling runtime routing strategies.
- [ ] **Activity-link propagation** — automatically link the mediator `Activity` to the parent `Activity` from the ambient `Activity.Current`, ensuring distributed traces are correctly parented across async boundaries.

