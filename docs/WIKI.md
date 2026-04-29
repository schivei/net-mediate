# NetMediate Wiki (In-Repo)

This page is the wiki entry point for the repository documentation set.

## Core guides

- [README](../README.md) — overview, quick start, usage examples
- [Installation, configuration, and usage by resource](WIKI_INSTALLATION_CONFIGURATION_USAGE.md)
- [Diagnostics](DIAGNOSTICS.md)
- [Resilience](RESILIENCE.md)
- [Source generation](SOURCE_GENERATION.md)
- [DataDog integrations](DATADOG.md)
- [MediatR migration guide](MEDIATR_MIGRATION_GUIDE.md)
- [NetMediate.Moq recipes](NETMEDIATE_MOQ_RECIPES.md)
- [Samples (API / Worker / Minimal API)](SAMPLES.md)
- [Library comparison](LIBRARY_COMPARISON.md)
- [Benchmark comparison](BENCHMARK_COMPARISON.md)
- [Performance action plan (15 M ops/s roadmap)](PERFORMANCE_ACTION_PLAN.md)

## Package overview

| Package | Purpose |
|---------|---------|
| `NetMediate` | Core: commands, requests, notifications, streams, validation, telemetry |
| `NetMediate.Compat` | MediatR compatibility shim |
| `NetMediate.Moq` | Unit-test helpers |
| `NetMediate.Resilience` | Retry / timeout / circuit-breaker (Polly v8) |
| `NetMediate.FluentValidation` | FluentValidation bridge (net8+) |
| `NetMediate.SourceGeneration` | Compile-time handler registration (AOT-safe) |
| `NetMediate.InternalNotifier` | Channel-based background notification dispatch |
| `NetMediate.InternalNotifier.Test` | Inline synchronous notification dispatch for tests |
| `NetMediate.Notifications` | Base class for custom notification providers |
| `NetMediate.DataDog.OpenTelemetry` | DataDog OTLP exporter wiring |
| `NetMediate.DataDog.Serilog` | DataDog Serilog sink |
| `NetMediate.DataDog.ILogger` | DataDog ILogger enrichment |

## Platform and framework coverage

Runtime packages are multi-targeted for:

- `net10.0`
- `netstandard2.0`
- `netstandard2.1`

This enables usage from desktop, CLI, mobile, MAUI, and server applications, depending on host runtime support.

## Benchmarks

Performance benchmark outputs are documented in:

- [Benchmark comparison](BENCHMARK_COMPARISON.md)
- [Diagnostics benchmark table](DIAGNOSTICS.md#performance-comparison-main-vs-current-branch)
- [Resilience benchmark table](RESILIENCE.md#load-and-capacity-benchmark)

`netstandard2.0` and `netstandard2.1` assets are host-runtime assets; benchmark throughput must be measured in the concrete target app runtime.
