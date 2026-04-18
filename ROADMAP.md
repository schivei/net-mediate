# Roadmap

This roadmap consolidates improvement ideas and new features for the NetMediate ecosystem.

## Short term

- [ ] Publish complete migration guides from MediatR to `NetMediate.Compat` (basic, advanced, and troubleshooting scenarios).
- [ ] Expand `NetMediate.Moq` documentation with unit and integration testing recipes.
- [ ] Add sample applications (API, Worker, and Minimal API) using `NetMediate`, `NetMediate.Compat`, and `NetMediate.Moq`.
- [ ] Cover diagnostics scenarios with structured logs and message-type metrics.

## Mid term

- [ ] Add pipeline behaviors/interceptors compatible with the MediatR processing flow (pre/post processing).
- [ ] Include retry, timeout, and circuit-breaker strategies for notification/request handlers.
- [ ] Provide optional source generator support to reduce reflection cost at startup.
- [ ] Evolve observability support (OpenTelemetry traces/metrics for Send/Request/Notify/Stream).

## Long term

- [ ] Create an integration package for popular validators (for example, FluentValidation) without mandatory coupling.
- [ ] Publish a benchmark suite comparing NetMediate, MediatR, and high-throughput scenarios.
- [ ] Explore an AOT-friendly mode with trimming and NativeAOT optimizations.
- [ ] Define an official ecosystem extension track (testing, diagnostics, resilience, and adapters).
