# Roadmap

This roadmap consolidates improvement ideas and new features for the NetMediate ecosystem.

## Short term

- [x] Publish complete migration guides from MediatR to `NetMediate.Compat` (basic, advanced, and troubleshooting scenarios). See `docs/MEDIATR_MIGRATION_GUIDE.md`.
- [x] Expand `NetMediate.Moq` documentation with unit and integration testing recipes. See `docs/NETMEDIATE_MOQ_RECIPES.md`.
- [x] Add sample applications (API, Worker, and Minimal API) using `NetMediate`, `NetMediate.Compat`, and `NetMediate.Moq`. See `docs/SAMPLES.md`.
- [x] Cover diagnostics scenarios with structured logs and message-type metrics. See `docs/DIAGNOSTICS.md`.

## Mid term

- [x] Add pipeline behaviors/interceptors compatible with the MediatR processing flow (pre/post processing). Implemented through `ICommandBehavior<TMessage>`, `IRequestBehavior<TMessage,TResponse>`, `INotificationBehavior<TMessage>`, and `IStreamBehavior<TMessage,TResponse>` in `src/NetMediate`, validated in `tests/NetMediate.Tests/PipelineBehaviorTests.cs`.
- [x] Include retry, timeout, and circuit-breaker strategies for notification/request handlers. Delivered as a dedicated package (`src/NetMediate.Resilience`) to keep the core mediator package focused and allow optional adoption.
- [ ] Provide optional source generator support to reduce reflection cost at startup.
- [x] Evolve observability support (OpenTelemetry traces/metrics for Send/Request/Notify/Stream). Implemented with built-in `ActivitySource`/`Meter` in `NetMediateDiagnostics` and covered by `tests/NetMediate.Tests/Internals/DiagnosticsTelemetryTests.cs`.

## Long term

- [ ] Create an integration package for popular validators (for example, FluentValidation) without mandatory coupling.
- [ ] Publish a benchmark suite comparing NetMediate, MediatR, and high-throughput scenarios.
- [ ] Explore an AOT-friendly mode with trimming and NativeAOT optimizations.
- [ ] Define an official ecosystem extension track (testing, diagnostics, resilience, and adapters).
