# NetMediate Benchmark Results

This document aggregates the load benchmark results across all NetMediate packages.

Results are produced by the test suite in `tests/NetMediate.Tests/` and captured from 5
consecutive runs on a GitHub Actions `ubuntu-latest` runner (5-run median).

## How to reproduce

```bash
NETMEDIATE_RUN_PERFORMANCE_TESTS=true dotnet test tests/NetMediate.Tests/NetMediate.Tests.csproj \
  --configuration Release --filter "FullyQualifiedName~LoadPerformance" \
  --logger "console;verbosity=detailed"
```

Output format per run:

```
LOAD_RESULT <scenario> tfm=<tfm> ops=<count> elapsed_ms=<ms> throughput_ops_s=<ops/s>
```

## Results — net10.0 (GitHub Actions ubuntu-latest, 5-run median)

### Core package (`NetMediate`)

| Test | Scenario | Operations | Mode | Median throughput (ops/s) |
|---|---|---:|---|---:|
| `CommandLoad_ShouldSustainMinimumThroughput` | `command` | 20,000 | Sequential | 404,930 |
| `RequestLoad_ShouldSustainMinimumThroughputInParallel` | `request_parallel` | 10,000 | Parallel | 136,357 |

**Raw runs:**

| Run | command (ops/s) | request_parallel (ops/s) |
|---:|---:|---:|
| 1 | 405,989 | 122,007 |
| 2 | 393,452 | 136,357 |
| 3 | 404,930 | 138,984 |
| 4 | 411,061 | 143,873 |
| 5 | 398,847 | 135,895 |
| **Median** | **404,930** | **136,357** |

### Resilience package (`NetMediate.Resilience`)

| Test | Scenario | Operations | Mode | Median throughput (ops/s) |
|---|---|---:|---|---:|
| `RequestLoad_WithResiliencePackage_ShouldSustainMinimumThroughputInParallel` | `resilience_request_parallel` | 10,000 | Parallel | 118,867 |

**Raw runs:**

| Run | resilience_request_parallel (ops/s) |
|---:|---:|
| 1 | 111,044 |
| 2 | 116,924 |
| 3 | 118,867 |
| 4 | 121,338 |
| 5 | 127,979 |
| **Median** | **118,867** |

### Overhead comparison

| Scenario | Throughput (ops/s) | vs. parallel baseline |
|---|---:|---:|
| `request_parallel` (core) | 136,357 | — |
| `resilience_request_parallel` | 118,867 | **−12.83 %** |

The resilience overhead (retry + timeout + circuit-breaker behaviors on every call) adds
approximately **−12.83 %** compared to the core parallel request baseline.

## Minimum assertions enforced by tests

| Test class | CI threshold | Local threshold |
|---|---:|---:|
| `LoadPerformanceTests` | `> 500 ops/s` | `> 500 ops/s` |
| `ResilienceLoadPerformanceTests` | `≥ 30,000 ops/s` | `≥ 50,000 ops/s` |

`LoadPerformanceTests` uses a lenient threshold to remain green on any hardware.
`ResilienceLoadPerformanceTests` has an environment-aware minimum set via `GITHUB_ACTIONS`.

## Notes

- All figures are for the `net10.0` TFM running on a standard GitHub Actions runner.
- Developer workstations and production servers typically achieve **2–5×** higher throughput.
- Performance tests only run when the environment variable `NETMEDIATE_RUN_PERFORMANCE_TESTS=true` is set,
  so they do not slow down the regular CI test run.
- For `netstandard2.0`/`netstandard2.1` assets, throughput depends on the host runtime
  (desktop/CLI/mobile/MAUI).

## See also

- [RESILIENCE.md](RESILIENCE.md) — detailed resilience package guide including throughput notes
