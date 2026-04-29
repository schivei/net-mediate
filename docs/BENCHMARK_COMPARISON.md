# Benchmark Comparison: NetMediate · MediatR 14 · martinothamar/Mediator 3 · TurboMediator

> **Auto-generated** by `LibraryBenchmarkTests.Comparison_WritesBenchmarkDocs`.
> Re-run with `NETMEDIATE_RUN_PERFORMANCE_TESTS=true` to refresh.

**Last run:** 2026-04-29 00:05 UTC  
**Target framework (main run):** `.NETCoreApp,Version=v10.0`

> TurboMediator benchmarked on **.NETCoreApp,Version=v8.0** at 2026-04-29 00:05 UTC.

## Benchmark Modes

| Mode | Description |
|------|-------------|
| **No Code Gen · No AOT** | Reflection-based assembly scan at startup, DI dispatch at runtime |
| **Code Gen · No AOT** | Explicit / source-generated handler registration, DI or switch-gen dispatch |
| **No Code Gen · AOT** | AOT publishing without a source generator — no library supports this |
| **Code Gen · AOT** | Source-gen registration + Native AOT publishing; same runtime throughput as Code Gen |

## Command Dispatch Throughput (ops/s — higher is better)

| Library | No Code Gen · No AOT | Code Gen · No AOT | No Code Gen · AOT | Code Gen · AOT |
|---------|:--------------------:|:-----------------:|:-----------------:|:--------------:|
| **NetMediate** | 452,842 | 504,204 | NOT SUPPORTED | ≈ Code Gen |
| **MediatR 14** | 2,004,611 | NOT SUPPORTED | NOT SUPPORTED | NOT SUPPORTED |
| **martinothamar/Mediator 3** | NOT SUPPORTED | 23,691,068 | NOT SUPPORTED | ≈ Code Gen |
| **TurboMediator** | NOT SUPPORTED | 19,749,185 *(net8.0)* | NOT SUPPORTED | ≈ Code Gen *(net8.0)* |

## Request/Response Throughput (ops/s — higher is better)

| Library | No Code Gen · No AOT | Code Gen · No AOT | No Code Gen · AOT | Code Gen · AOT |
|---------|:--------------------:|:-----------------:|:-----------------:|:--------------:|
| **NetMediate** | 496,433 | 485,769 | NOT SUPPORTED | ≈ Code Gen |
| **MediatR 14** | 2,562,788 | NOT SUPPORTED | NOT SUPPORTED | NOT SUPPORTED |
| **martinothamar/Mediator 3** | NOT SUPPORTED | 20,738,283 | NOT SUPPORTED | ≈ Code Gen |
| **TurboMediator** | NOT SUPPORTED | 17,301,038 *(net8.0)* | NOT SUPPORTED | ≈ Code Gen *(net8.0)* |

## Mode Support Matrix

| Library | No Code Gen · No AOT | Code Gen · No AOT | No Code Gen · AOT | Code Gen · AOT |
|---------|:--------------------:|:-----------------:|:-----------------:|:--------------:|
| **NetMediate** | ✅ | ✅ | ❌ | ✅ |
| **MediatR 14** | ✅ | ❌ | ❌ | ❌ |
| **martinothamar/Mediator 3** | ❌ (source gen required) | ✅ | ❌ | ✅ |
| **TurboMediator** | ❌ (source gen required) | ✅ *(net8.0)* | ❌ | ✅ *(net8.0)* |

## Per-dispatch Feature Comparison

| Feature | NetMediate *(benchmarked)* | MediatR 14 | martinothamar/Mediator 3 | TurboMediator |
|---|:---:|:---:|:---:|:---:|
| New DI scope per dispatch | ✅ always | ❌ no | ❌ no | ❌ no |
| Validation pipeline | ✅ disabled for bench | ❌ no | ❌ no | ✅ optional |
| OpenTelemetry activity | ✅ disabled for bench | ❌ no | ❌ no | ✅ optional package |
| Background async logging | ✅ channel-queued | varies | varies | varies |
| Source-generated switch dispatch | ❌ DI-based | ❌ DI-based | ✅ | ✅ |
| .NET 10 compatible | ✅ | ✅ | ✅ | ⚠️ issue v0.9.3 |
| netstandard2.0 support | ✅ | ❌ | ❌ | ❌ |

## Measurement Details

| Metric | Command | Request |
|--------|---------|---------|
| Operations per timed pass | 20,000 | 10,000 |
| NetMediate (No Code Gen) elapsed | 44.2 ms | 20.1 ms |
| NetMediate (Code Gen) elapsed | 39.7 ms | 20.6 ms |
| MediatR 14 elapsed | 10.0 ms | 3.9 ms |
| martinothamar/Mediator elapsed | 0.8 ms | 0.5 ms |
| TurboMediator elapsed *(net8.0)* | 1.0 ms | 0.6 ms |

## Test Environment

| | Main run (net10.0) | TurboMediator run (net8.0) |
|---|---|---|
| OS | Ubuntu 24.04.4 LTS | Ubuntu 24.04.4 LTS |
| Processors | 4 | — |
| Runtime | .NET 10.0.5 | .NET 8.0.25 |

## Methodology

One warm-up pass (JIT) followed by a single timed sequential pass.
No-op handlers.  Logging set to `Warning` for all libraries.
NetMediate benchmarks run with `DisableTelemetry() + DisableValidation()` for a fair baseline.
TurboMediator is benchmarked in a separate `net8.0` project due to a source-generator
incompatibility with net10.0 (v0.9.3); results are merged via a JSON sidecar file.

See `tests/NetMediate.Benchmarks/LibraryBenchmarkTests.cs` and
`tests/NetMediate.Benchmarks.TurboMediator/TurboMediatorBenchmarkTests.cs` for the full source.
