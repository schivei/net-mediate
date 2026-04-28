# Benchmark Comparison: NetMediate vs MediatR

> **Auto-generated** by `LibraryBenchmarkTests.Comparison_WritesBenchmarkDocs`.
> Re-run with `NETMEDIATE_RUN_PERFORMANCE_TESTS=true` to refresh.

**Last run:** 2026-04-28 22:40 UTC  
**Target framework:** `.NETCoreApp,Version=v10.0`

## Throughput (operations / second, higher is better)

| Scenario | NetMediate | MediatR 14 | Comparison |
|----------|------------|------------|------------|
| Command (fire & forget) | 243,879 | 1,399,110 | 83% slower |
| Request (query/response) | 246,861 | 1,747,091 | 86% slower |

## What the numbers mean

MediatR 14 is faster in raw sequential throughput because it focuses exclusively
on message dispatch with minimal overhead.  NetMediate deliberately includes a
richer feature set per dispatch cycle:

| Per-dispatch cost | NetMediate | MediatR 14 |
|-------------------|-----------|-----------|
| New DI scope (isolation) | ✅ yes | ❌ no |
| Message validation | ✅ yes (no-op if no validator) | ❌ no |
| OpenTelemetry activity | ✅ yes (always) | ❌ no |
| Debug log per dispatch | ✅ yes | ❌ no |
| Pipeline behaviour resolution | ✅ yes | ✅ yes |

For handlers that perform any real I/O (database, HTTP, etc.) these costs are
completely dominated by the I/O latency.  The difference only becomes noticeable
in tight micro-benchmark loops with no-op handlers.

If raw throughput is the primary concern you can disable the Activity creation
by not registering the `ActivitySource`, and reduce scope overhead by reusing
the root service provider or supplying handlers as singletons.

## Measurement details

| Metric | Command | Request |
|--------|---------|---------|
| Operations | 20,000 | 10,000 |
| NetMediate elapsed | 82.0 ms | 40.5 ms |
| MediatR elapsed    | 14.3 ms | 5.7 ms |

## Test environment

- **OS:** Ubuntu 24.04.4 LTS
- **Processor count:** 4
- **Runtime:** .NET 10.0.5

## Methodology

Each scenario runs one warm-up pass (to JIT compile the path) followed by a
single timed pass.  All operations run **sequentially** to measure single-thread
throughput rather than parallelism.  Both libraries share the same handler
implementation and the same DI host.

See `tests/NetMediate.Benchmarks/LibraryBenchmarkTests.cs` for the full source.
