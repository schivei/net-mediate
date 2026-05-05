---
sidebar_position: 3
---

# NetMediate.Resilience

Adds retry, timeout and circuit-breaker pipeline behaviors to your NetMediate message pipeline.

## Installation

```bash
dotnet add package NetMediate.Resilience
```

## Registration

No manual registration is required. The source generator (`NetMediate.SourceGeneration`) automatically detects the `NetMediate.Resilience` assembly reference at compile time and registers all resilience behaviors before user-defined behaviors in the pipeline.

To customize the behavior options, use `ConfigureOptions` or `Configure<T>` **before** calling `AddNetMediate()`:

```csharp
// Override retry defaults (optional — defaults are applied automatically)
builder.Services.Configure<RetryBehaviorOptions>(opts =>
{
    opts.MaxRetryCount = 3;
    opts.Delay = TimeSpan.FromMilliseconds(200);
});

builder.Services.Configure<TimeoutBehaviorOptions>(opts =>
{
    opts.RequestTimeout = TimeSpan.FromSeconds(10);
    opts.NotificationTimeout = TimeSpan.FromSeconds(5);
});

builder.Services.Configure<CircuitBreakerBehaviorOptions>(opts =>
{
    opts.FailureThreshold = 5;
    opts.OpenDuration = TimeSpan.FromSeconds(30);
});

// The source generator emits this call — no manual call needed
builder.Services.AddNetMediate();
```

Each option is independent — override only the ones you need.

## Behaviors

| Behavior | Registration service type | Applies to |
|---|---|---|
| `RetryRequestBehavior<TMessage, TResponse>` | `IPipelineRequestBehavior<,>` | Request pipeline |
| `RetryNotificationBehavior<TMessage>` | `IPipelineBehavior<>` | Notification pipeline |
| `TimeoutRequestBehavior<TMessage, TResponse>` | `IPipelineRequestBehavior<,>` | Request pipeline |
| `TimeoutNotificationBehavior<TMessage>` | `IPipelineBehavior<>` | Notification pipeline |
| `CircuitBreakerRequestBehavior<TMessage, TResponse>` | `IPipelineRequestBehavior<,>` | Request pipeline |
| `CircuitBreakerNotificationBehavior<TMessage>` | `IPipelineBehavior<>` | Notification pipeline |

All resilience behaviors follow the standard `IPipelineBehavior<TMessage, TResult>` contract, which means their `Handle` signature accepts `object? key` as the first parameter. The `key` is forwarded transparently so that keyed dispatch (e.g. `mediator.Send("audit", command, ct)`) passes the routing key through the full resilience pipeline.

> **Note:** Notification dispatch is fire-and-forget by design. Handler exceptions do **not** propagate back through the notification pipeline, so retry/timeout/circuit-breaker behaviors at the pipeline level do not observe handler failures for notifications. Use these behaviors to protect the pipeline itself (e.g., adapter calls, pre-processing logic).

## Retry

Retries a failed pipeline step up to `MaxRetryCount` times with an optional delay between attempts.

```csharp
builder.Services.Configure<RetryBehaviorOptions>(opts =>
{
    opts.MaxRetryCount = 3;
    opts.Delay = TimeSpan.FromMilliseconds(100);
});
```

## Timeout

Cancels the request if it exceeds the configured timeout.

```csharp
builder.Services.Configure<TimeoutBehaviorOptions>(opts =>
{
    opts.RequestTimeout = TimeSpan.FromSeconds(30);
    opts.NotificationTimeout = TimeSpan.FromSeconds(5);
});
```

## Circuit Breaker

Opens the circuit after `FailureThreshold` consecutive failures and keeps it open for `OpenDuration`.

```csharp
builder.Services.Configure<CircuitBreakerBehaviorOptions>(opts =>
{
    opts.FailureThreshold = 5;
    opts.OpenDuration = TimeSpan.FromMinutes(1);
});
```
