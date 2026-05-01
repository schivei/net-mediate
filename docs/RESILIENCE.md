# NetMediate.Resilience

`NetMediate.Resilience` is an optional package that keeps resilience concerns out of the `NetMediate` core package.

This follows a UNIX-style philosophy: each package should do one thing well.

## Features

- Retry behaviors for request and notification pipelines
- Timeout behaviors for request and notification pipelines
- Circuit-breaker behaviors for request and notification pipelines

> For notification flows, exceptions thrown by handlers are caught by the background notification worker
> and logged as warnings (they do not propagate to the caller of `mediator.Notify`). Resilience behaviors
> wrap the notification dispatch pipeline (validation + all handlers) executed inside the worker.
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

`LoadPerformanceTests` and `ResilienceLoadPerformanceTests` exercise sequential and parallel scenarios with and without the resilience behaviors package enabled.

### How to reproduce

```bash
NETMEDIATE_RUN_PERFORMANCE_TESTS=true dotnet test tests/NetMediate.Tests/NetMediate.Tests.csproj \
  --configuration Release --filter "FullyQualifiedName~LoadPerformance" \
  --logger "console;verbosity=detailed"
```

Each test prints a `LOAD_RESULT` line of the form:

```
LOAD_RESULT <scenario> tfm=<tfm> ops=<count> elapsed_ms=<ms> throughput_ops_s=<ops/s>
```

### Measured results (net10.0 — 5-run median, GitHub-hosted runner)

| Scenario | Operations | Mode | Median throughput (ops/s) | Notes |
|---|---:|---|---:|---|
| `command` | 20,000 | Sequential | 404,930 | `ICommandHandler<T>` baseline |
| `request_parallel` | 10,000 | Parallel | 136,357 | `IRequestHandler<T,R>` parallel baseline |
| `resilience_request_parallel` | 10,000 | Parallel | 118,867 | Same as above + all three resilience behaviors |

Observed overhead for resilience behaviors: **−12.83 %** compared to the parallel request baseline.

> Absolute values depend on runner hardware and parallelism. These figures are captured on the
> standard GitHub Actions runner (`ubuntu-latest`). Expect **2–5×** higher throughput on
> developer workstations and production servers.

### Minimum assertions

`LoadPerformanceTests` uses a deliberately lenient threshold (`> 500 ops/s`) to stay green on any
hardware. `ResilienceLoadPerformanceTests` applies an environment-aware minimum:

- **CI (`GITHUB_ACTIONS=true`)**: `≥ 30,000 ops/s`
- **Local/other**: `≥ 50,000 ops/s`

### Target coverage for package assets

`NetMediate.Resilience` is published for:

- `net10.0`
- `netstandard2.0`
- `netstandard2.1`

The measured throughput table above is produced on a `net10.0` host runtime. For `netstandard2.0`/`netstandard2.1`,
benchmark numbers depend on the runtime that hosts the package (desktop/CLI/mobile/MAUI).
