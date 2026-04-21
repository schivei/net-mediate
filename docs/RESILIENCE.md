# NetMediate.Resilience

`NetMediate.Resilience` is an optional package that keeps resilience concerns out of the `NetMediate` core package.

This follows a UNIX-style philosophy: each package should do one thing well.

## Features

- Retry behaviors for request and notification pipelines
- Timeout behaviors for request and notification pipelines
- Circuit-breaker behaviors for request and notification pipelines

> For notification flows, NetMediate still uses `onError` callbacks per handler.
> Resilience behaviors wrap the notification pipeline execution and remain compatible with this model.
>
> Circuit-breaker state is intentionally isolated per message flow (closed generic behavior type),
> so one message type opening a circuit does not block unrelated message types.

## Installation

```bash
dotnet add package NetMediate.Resilience
```

## Usage

```csharp
using NetMediate.Resilience;

builder.Services.AddNetMediate(typeof(MyHandler).Assembly);
builder.Services.AddNetMediateResilience(
    configureRetry: retry =>
    {
        retry.MaxRetryCount = 2;
        retry.Delay = TimeSpan.FromMilliseconds(25);
    },
    configureTimeout: timeout =>
    {
        timeout.RequestTimeout = TimeSpan.FromSeconds(2);
        timeout.NotificationTimeout = TimeSpan.FromSeconds(2);
    },
    configureCircuitBreaker: breaker =>
    {
        breaker.FailureThreshold = 5;
        breaker.OpenDuration = TimeSpan.FromSeconds(30);
    }
);
```

You can also register each concern independently:

```csharp
builder.Services.AddNetMediateRetry();
builder.Services.AddNetMediateTimeout();
builder.Services.AddNetMediateCircuitBreaker();
```

## Load and capacity benchmark

`ResilienceLoadPerformanceTests` executes a parallel request scenario with resilience behaviors enabled.

To provide user-facing package capacity guidance, we ran 5 executions for:

- Core request flow (`LoadPerformanceTests.RequestLoad_ShouldSustainMinimumThroughputInParallel`)
- Resilience package enabled (`ResilienceLoadPerformanceTests.RequestLoad_WithResiliencePackage_ShouldSustainMinimumThroughputInParallel`)

| Scenario | Median throughput (ops/s) | Notes |
|---|---:|---|
| core request_parallel | 118,771.33 | 10,000 operations, parallel |
| resilience_request_parallel | 84,607.52 | 10,000 operations, parallel |

Observed delta for this environment: **-28.76%** throughput with resilience behaviors enabled.

`ResilienceLoadPerformanceTests` uses environment-aware assertions:

- **CI (`GITHUB_ACTIONS=true`)**: minimum **30,000 ops/s** (shared-runner stability)
- **Local/other environments**: minimum **50,000 ops/s**

Documented median values above should continue to be used for release-level capacity analysis.

## Target coverage for package assets

`NetMediate.Resilience` is published for:

- `net10.0`
- `netstandard2.0`
- `netstandard2.1`

The measured throughput table above is produced on a `net10.0` host runtime. For `netstandard2.0`/`netstandard2.1`,
benchmark numbers depend on the runtime that hosts the package (desktop/CLI/mobile/MAUI).

Use the test output line format below to copy measured values:

```
LOAD_RESULT resilience_request_parallel tfm=<target_framework_name> ops=... elapsed_ms=... throughput_ops_s=...
```
