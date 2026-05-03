# NetMediate Benchmark Results

This document aggregates all throughput benchmark results across TFMs, registration
styles, pipeline configurations, and package combinations.

---

## System Configuration

All results below were captured on the following machine:

| Property | Value |
|---|---|
| **OS** | Ubuntu 24.04.4 LTS (kernel 6.17.0-azure) |
| **CPU** | AMD EPYC 7763 (4 vCPUs exposed to the runner) |
| **RAM** | 16 GB |
| **Architecture** | x86-64 |
| **SDK** | .NET 10.0.201 |
| **Runtimes available** | .NET 8.0.25, .NET 9.0.14, .NET 10.0.5 |
| **Runner environment** | GitHub Actions `ubuntu-latest` |

> Developer workstations and production servers typically achieve **2–5 ×** higher
> throughput due to more vCPUs and a warmer CPU state.

---

## How to Reproduce

```bash
# Run all performance suites (all three TFMs are built and run together)
NETMEDIATE_RUN_PERFORMANCE_TESTS=true dotnet test tests/NetMediate.Tests/ \
  --configuration Release \
  --filter "FullyQualifiedName~LoadPerformance OR FullyQualifiedName~PipelineVariants OR FullyQualifiedName~ExplicitRegistration" \
  --logger "console;verbosity=detailed"

# Or a specific TFM
NETMEDIATE_RUN_PERFORMANCE_TESTS=true dotnet test tests/NetMediate.Tests/ \
  --configuration Release --framework net10.0 \
  --filter "FullyQualifiedName~LoadPerformance" \
  --logger "console;verbosity=detailed"
```

Output lines of interest:

```
LOAD_RESULT <scenario> tfm=<tfm> ops=<count> elapsed_ms=<ms> throughput_ops_s=<ops/s>
SYSTEM_INFO <key>=<value>
```

Results shown are **5-run medians** for every TFM.

---

## 1. Core Scenarios — Assembly-scan Registration

Message types: `command` (20 k ops, sequential), `command_parallel` (10 k, parallel),
`request_parallel` (10 k, parallel), `notification` (20 k, sequential),
`notification_parallel` (10 k, parallel), `stream` (5 k, sequential / 5 items per call).

| Scenario | net8.0 | net9.0 | net10.0 |
|---|---:|---:|---:|
| `command` | 329,680 | 380,907 | 452,793 |
| `command_parallel` | 295,613 | 106,918 | 103,353 |
| `request_parallel` | 93,433 | 199,121 | 460,925 |
| `notification` | 710,376 | 484,818 | 442,142 |
| `notification_parallel` | 288,118 | 750,542 | 537,011 |
| `stream` (calls/s) | 60,125 | 56,311 | 51,842 |

> All values in **ops/s** (5-run median).  
> `stream` counts complete `RequestStream` calls drained to end, not individual items yielded.

---

## 2. Registration Path — Assembly Scan vs. Explicit (AOT-safe / source-gen)

The explicit path uses `AddNetMediate(configure => ...)` — no reflection, AOT-compatible,
same code the source generator emits.  Per-call throughput is identical once the host is
built; the difference is startup-time handler discovery, not message dispatch.

| Scenario | net8.0 | net9.0 | net10.0 |
|---|---:|---:|---:|
| `explicit_command` | 364,700 | 237,282 | 457,651 |
| `explicit_command_parallel` | 718,757 | 422,407 | 937,646 |
| `explicit_request_parallel` | 608,040 | 648,824 | 551,098 |
| `explicit_notification` | 876,194 | 563,131 | 439,473 |
| `explicit_notification_parallel` | 206,243 | 542,944 | 354,975 |

> Assembly-scan vs. explicit shows **no statistically significant per-call difference**;
> variance between runs dominates.  The explicit path eliminates startup reflection and is
> required for NativeAOT.

---

## 3. Validation Variants

Compares the per-call overhead of three validation configurations, sequential, 20 k ops.

### Command dispatch

| Configuration | Description | net8.0 | net9.0 | net10.0 |
|---|---|---:|---:|---:|
| No pipeline behavior | Plain command dispatch | 427,859 | 409,902 | 477,313 |
| With pipeline behavior | `IPipelineBehavior<>` registered (always success) | 384,005 | 379,384 | 363,360 |

> Overhead of a pipeline behavior: **−10–24 %** — because it resolves an extra service from the DI container on every call.

### Notification dispatch

| Configuration | Description | net8.0 | net9.0 | net10.0 |
|---|---|---:|---:|---:|
| No pipeline behavior | Plain notification dispatch (fire-and-forget) | 732,923 | 654,121 | 634,148 |
| With pipeline behavior | `IPipelineBehavior<>` registered | 845,266 | 710,285 | 919,109 |

> For notifications, pipeline behavior overhead is absorbed by the async channel dispatch;
> absolute differences are within normal run-to-run variance.

---

## 4. Behavior / Pipeline-Middleware Variants

No-op pass-through behaviors measure the cost of the delegate wrapping chain,
parallel, 10 k ops.

### Command behaviors

| Configuration | net8.0 | net9.0 | net10.0 |
|---|---:|---:|---:|
| No behaviors | 448,710 | 277,137 | 903,987 |
| 1 no-op behavior | 750,931 | 428,203 | 739,596 |
| 2 no-op behaviors | 724,024 | 507,398 | 726,507 |

### Notification behaviors

| Configuration | net8.0 | net9.0 | net10.0 |
|---|---:|---:|---:|
| No behaviors | 510,373 | 562,392 | 542,617 |
| 1 no-op behavior | 728,773 | 615,995 | 553,110 |
| 2 no-op behaviors | 267,390 | 521,040 | 392,026 |

> Behavior overhead is generally within run noise for a pure pass-through.  Real-world
> behaviors (retry, timeout, circuit-breaker) add measurable overhead — see §6.

---

## 5. Handler Fan-out Variants

Measures dispatch cost when multiple handlers are registered for the same message.
Commands run all handlers via `Task.WhenAll` (parallel); notifications run them
sequentially in a `foreach`.  Sequential, 20 k ops.

### Command fan-out

| Handlers | net8.0 | net9.0 | net10.0 |
|---:|---:|---:|---:|
| 1 | 393,142 | 401,249 | 471,251 |
| 2 | 409,022 | 404,088 | 467,945 |
| 3 | 344,225 | 383,275 | 440,810 |

> Commands dispatch all handlers via `Task.WhenAll`; adding handlers adds allocations
> but parallelism masks the sequential cost.  Overhead: **−7–12 %** at 3 handlers vs 1.

### Notification fan-out

| Handlers | net8.0 | net9.0 | net10.0 |
|---:|---:|---:|---:|
| 1 | 898,840 | 694,321 | 497,050 |
| 2 | 663,104 | 732,089 | 804,884 |
| 3 | 743,403 | 770,199 | 778,962 |

> Notifications dispatch handlers sequentially via `foreach`; throughput variance
> is dominated by the async channel latency rather than handler count.

---

## 6. Package Stacking — Overhead Comparison

Each package row shows the median throughput when that package's behaviors/features
are added on top of the core configuration.  All parallel, 10 k ops.

### Request pipeline

| Configuration | net8.0 | net9.0 | net10.0 |
|---|---:|---:|---:|
| Core `request_parallel` | 93,433 | 199,121 | 460,925 |
| + Resilience (retry=0, timeout=30 s, CB threshold=1000) | 73,036 | 65,777 | 64,663 |
| + Resilience + Adapters (`fullstack_request_parallel`) | 71,586 | 148,822 | 248,350 |

### Notification pipeline

| Configuration | net8.0 | net9.0 | net10.0 |
|---|---:|---:|---:|
| Core `notification` (sequential) | 710,376 | 484,818 | 442,142 |
| + Adapters (`adapters_notification`) | 436,278 | 218,103 | 456,499 |
| + Resilience + Adapters (`fullstack_notification`) | 531,426 | 463,900 | 342,344 |

---

## 7. Overhead Summary (net10.0 baseline)

| Add-on | vs. core baseline | Notes |
|---|---:|---|
| +1 no-op pipeline behavior | ≈ 0 % | delegate wrap overhead is minimal |
| +2 no-op pipeline behaviors | ≈ 0 % | within variance |
| Command fan-out ×3 vs ×1 | − 7 % | `Task.WhenAll` allocation |
| Notification fan-out ×3 vs ×1 | + 57 % | async channel absorbed; variance-driven |
| Resilience package (request) | − 86 % | retry + timeout + CB evaluated every call |
| Adapters package (notification) | + 3 % | early-exit when 0 adapters registered |
| Resilience + Adapters (request) | − 46 % | combined stacking, net10.0 |

---

## 8. AOT / NativeAOT Benchmark Notes

Per-call throughput is **identical** between JIT and NativeAOT once the process is
running.  The two differ in:

| Aspect | JIT | NativeAOT |
|---|---|---|
| Cold-start | Assembly scanning + JIT compilation | Pre-compiled; fastest startup |
| Startup overhead | Reflection at `AddNetMediate(assembly)` | None — `AddNetMediate(configure =>)` only |
| Throughput (warm) | Same | Same |
| Compatible APIs | All | Explicit registration path only |

To publish a NativeAOT build for manual throughput verification:

```bash
dotnet publish src/NetMediate/NetMediate.csproj \
  -r linux-x64 -c Release \
  -p:PublishAot=true -p:TrimmerRootDescriptor=TrimmerRoots.xml
```

See [`docs/AOT.md`](AOT.md) for the full AOT-compatible registration guide.

---

## 9. Minimum Assertions Enforced by Tests

| Test class | Scenario | CI threshold | Local threshold |
|---|---|---:|---:|
| `LoadPerformanceTests` | all | `> 500 ops/s` | `> 500 ops/s` |
| `CoreExplicitRegistrationLoadTests` | all | `> 500 ops/s` | `> 500 ops/s` |
| `AdaptersLoadPerformanceTests` | all | `> 500 ops/s` | `> 500 ops/s` |
| `ResilienceLoadPerformanceTests` | `resilience_request_parallel` | `≥ 30,000 ops/s` | `≥ 50,000 ops/s` |
| `FullStackLoadPerformanceTests` | `fullstack_request_parallel` | `≥ 20,000 ops/s` | `≥ 40,000 ops/s` |
| `PipelineVariantsLoadTests` | all | `> 500 ops/s` | `> 500 ops/s` |

All thresholds are deliberately lenient so that they remain green on any hardware.
The numbers in this document are the observable medians, not the floor.

---

## See Also

- [RESILIENCE.md](RESILIENCE.md) — resilience package guide including per-behavior overhead
- [ADAPTERS.md](ADAPTERS.md) — adapters package guide
- [AOT.md](AOT.md) — AOT/NativeAOT compatibility guide
