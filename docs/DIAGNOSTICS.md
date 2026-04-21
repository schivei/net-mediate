# Diagnostics: Structured Logs and Metrics by Message Type

## Structured logs

NetMediate already emits structured message-type logs in core mediator operations, for example:

- `Sending message of type {MessageType}`
- `Notifying message of type {MessageType}`
- `Resolved {HandlerCount} handlers for message of type {MessageType}`

This allows filtering by `MessageType` in log platforms.

## Built-in OpenTelemetry-style traces and metrics

NetMediate now emits built-in diagnostics primitives:

- **ActivitySource**: `NetMediate`
- **Meter**: `NetMediate`
- **Counters**:
  - `netmediate.send.count`
  - `netmediate.request.count`
  - `netmediate.notify.count`
  - `netmediate.stream.count`

For each operation (`Send`, `Request`, `Notify`, `RequestStream`), activities include:

- `netmediate.operation`
- `netmediate.message_type`

### Quick integration example

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(NetMediateDiagnostics.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(NetMediateDiagnostics.MeterName));
```

## Metrics by message type (application-level instrumentation)

You can still add application-level metrics around mediator calls when needed:

```csharp
using System.Diagnostics.Metrics;

public sealed class MediatorDiagnostics
{
    private static readonly Meter Meter = new("NetMediate.App");
    private static readonly Counter<long> SendCounter = Meter.CreateCounter<long>("netmediate.send.count");

    public static async Task SendWithMetrics<TMessage>(IMediator mediator, TMessage message, CancellationToken ct)
    {
        SendCounter.Add(1, KeyValuePair.Create<string, object?>("message_type", typeof(TMessage).Name));
        await mediator.Send(message, ct);
    }
}
```

For MediatR contracts using compat, the same approach applies with `MediatR.IMediator`.

## Performance comparison (main vs current branch)

Measurements were captured with the same load scenarios used in tests, using **5 runs per branch** and reporting the **median throughput**:

- command: 20,000 operations
- request_parallel: 10,000 operations

Environment used for these measurements:

- CPU/vCPU: **AMD EPYC 7763** with **4 vCPU**
- Memory: **15 GiB RAM**

| Scenario | `origin/main` median throughput (ops/s) | Current branch median throughput (ops/s) | Delta |
|---|---:|---:|---:|
| command | 172,650.29 | 179,843.50 | +4.17% |
| request_parallel | 280,944.65 | 118,619.88 | -57.78% |

## Benchmark matrix by package target

NetMediate runtime packages now publish assets for `net10.0`, `netstandard2.0`, and `netstandard2.1`.
Benchmarks execute on runnable app runtimes, so measured throughput is reported for `net10.0` here.

| Target asset | Benchmark status | Notes |
|---|---|---|
| `net10.0` | Measured | See throughput table above |
| `netstandard2.0` | Host-runtime dependent | Throughput must be measured on the concrete host runtime (desktop/CLI/mobile/MAUI) |
| `netstandard2.1` | Host-runtime dependent | Throughput must be measured on the concrete host runtime (desktop/CLI/mobile/MAUI) |

Use the load output format below to capture per-target results:

```
LOAD_RESULT <scenario> tfm=<target_framework_name> ops=... elapsed_ms=... throughput_ops_s=...
```

> Notes:
> - command throughput improved slightly with the current branch in this environment.
> - request-parallel throughput is lower because this branch adds extra per-request work in the hot path (behavior-chain resolution + diagnostics counters/tags + activity lifecycle checks).
> - this benchmark uses trivial in-memory handlers, so framework overhead dominates; in real handlers with I/O/database calls, this relative cost is typically much smaller.
> - performance tests are sensitive to runtime environment noise (CPU contention, warmup state, parallel load). Re-run multiple times for stable medians before release decisions.

## Validation checklist

- [x] Structured log templates include message type
- [x] Message-type metric tags documented for app-level counters
- [x] Built-in ActivitySource/Meter names and metrics documented
- [x] Comparison table between `origin/main` and current branch generated from test runs
- [x] Shared parity tests validate message dispatch in both package modes
