---
sidebar_position: 4
---

# NetMediate Diagnostics

## Built-in OpenTelemetry traces and metrics

NetMediate emits diagnostics primitives for every mediator operation via the standard .NET `ActivitySource` and `Meter` APIs.

### ActivitySource

Name: **`NetMediate`** (`NetMediateDiagnostics.ActivitySourceName`)

An `Activity` is started for each operation:

| Operation | Activity name | Tags |
|---|---|---|
| `IMediator.Send` | `NetMediate.Send` | `netmediate.operation`, `netmediate.message_type` |
| `IMediator.Request` | `NetMediate.Request` | `netmediate.operation`, `netmediate.message_type` |
| `IMediator.Notify` | `NetMediate.Notify` | `netmediate.operation`, `netmediate.message_type` |
| `IMediator.RequestStream` | `NetMediate.RequestStream` | `netmediate.operation`, `netmediate.message_type` |

Activities are only started when the `ActivitySource` has at least one listener — there is no overhead when tracing is not configured.

### Meter

Name: **`NetMediate`** (`NetMediateDiagnostics.MeterName`)

| Counter name | Constant | Incremented by |
|---|---|---|
| `netmediate.send.count` | `NetMediateDiagnostics.SendCountMetricName` | `IMediator.Send` |
| `netmediate.request.count` | `NetMediateDiagnostics.RequestCountMetricName` | `IMediator.Request` |
| `netmediate.notify.count` | `NetMediateDiagnostics.NotifyCountMetricName` | `IMediator.Notify` |
| `netmediate.dispatch.count` | `NetMediateDiagnostics.DispatchCountMetricName` | internal handler dispatch |
| `netmediate.stream.count` | `NetMediateDiagnostics.StreamCountMetricName` | `IMediator.RequestStream` |

All counters carry a `message_type` tag with the message CLR type name. Counters are only recorded when the `Counter` is enabled — there is no overhead when metrics are not configured.

## Quick integration example

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(NetMediateDiagnostics.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(NetMediateDiagnostics.MeterName));
```

For DataDog, use the dedicated package instead:

```csharp
builder.Services.AddNetMediateDataDogOpenTelemetry(options =>
{
    options.ServiceName    = "my-service";
    options.OtlpEndpoint   = new Uri("http://localhost:4318");
});
```

See [DATADOG.md](DATADOG.md) for full details.

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

> Notes:
> - command throughput improved slightly with the current branch in this environment.
> - request throughput is lower because this branch adds extra per-request work in the hot path (behavior-chain resolution + diagnostics counters/tags + activity lifecycle checks).
> - benchmark results are sensitive to runtime environment noise. Re-run multiple times for stable medians before release decisions.

## See Also

- [Benchmarks](../performance/benchmarks) — full benchmark matrix and reproduction steps
- [DataDog Integration](./datadog) — DataDog integration guide
