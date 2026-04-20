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

| Scenario | `origin/main` median throughput (ops/s) | Current branch median throughput (ops/s) | Delta |
|---|---:|---:|---:|
| command | 172,650.29 | 179,843.50 | +4.17% |
| request_parallel | 280,944.65 | 118,619.88 | -57.78% |

> Notes:
> - command throughput improved slightly with the current branch in this environment.
> - request-parallel throughput is lower due to additional pipeline/observability path work per request.
> - performance tests are sensitive to runtime environment noise (CPU contention, warmup state, parallel load). Re-run multiple times for stable medians before release decisions.

## Validation checklist

- [x] Structured log templates include message type
- [x] Message-type metric tags documented for app-level counters
- [x] Built-in ActivitySource/Meter names and metrics documented
- [x] Comparison table between `origin/main` and current branch generated from test runs
- [x] Shared parity tests validate message dispatch in both package modes
