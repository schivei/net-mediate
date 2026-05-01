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
