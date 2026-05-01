# NetMediate Wiki (In-Repo)

This page is the wiki entry point for the repository documentation set.

## Core guides

- [README](../README.md)
- [Installation, configuration, and usage by resource](WIKI_INSTALLATION_CONFIGURATION_USAGE.md)
- [Diagnostics](DIAGNOSTICS.md)
- [Resilience](RESILIENCE.md)
- [Quartz persistent notifications](QUARTZ.md)
- [Notification adapters (external queues/streams)](ADAPTERS.md)
- [Source generation](SOURCE_GENERATION.md)
- [DataDog integrations](DATADOG.md)
- [NetMediate.Moq recipes](NETMEDIATE_MOQ_RECIPES.md)
- [Samples](SAMPLES.md)

## Platform and framework coverage

Runtime packages are multi-targeted for:

- `net10.0`
- `netstandard2.0`
- `netstandard2.1`

This enables usage from desktop, CLI, mobile, MAUI, and server applications, depending on host runtime support.

## Benchmarks

Performance benchmark outputs are documented in:

- [Diagnostics benchmark table](DIAGNOSTICS.md#performance-comparison-main-vs-current-branch)
- [Resilience benchmark table](RESILIENCE.md#load-and-capacity-benchmark)

`netstandard2.0` and `netstandard2.1` assets are host-runtime assets; benchmark throughput must be measured in the concrete target app runtime.
