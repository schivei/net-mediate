# NetMediate.Resilience

Adds retry, timeout and circuit-breaker pipeline behaviors to your NetMediate message pipeline.

## Installation

```bash
dotnet add package NetMediate.Resilience
```

## Registration

```csharp
using NetMediate.Resilience;

builder.Services.AddNetMediateResilience(
    configureRetry: options =>
    {
        options.MaxRetryCount = 3;
        options.Delay = TimeSpan.FromMilliseconds(200);
    },
    configureTimeout: options =>
    {
        options.RequestTimeout = TimeSpan.FromSeconds(10);
        options.NotificationTimeout = TimeSpan.FromSeconds(5);
    },
    configureCircuitBreaker: options =>
    {
        options.FailureThreshold = 5;
        options.OpenDuration = TimeSpan.FromSeconds(30);
    }
);
```

Each option is independent — pass only the ones you need.

## Behaviors

| Behavior | Registration service type | Applies to |
|---|---|---|
| `RetryRequestBehavior<TMessage, TResponse>` | `IPipelineRequestBehavior<,>` | Request pipeline |
| `RetryNotificationBehavior<TMessage>` | `IPipelineBehavior<>` | Notification pipeline |
| `TimeoutRequestBehavior<TMessage, TResponse>` | `IPipelineRequestBehavior<,>` | Request pipeline |
| `TimeoutNotificationBehavior<TMessage>` | `IPipelineBehavior<>` | Notification pipeline |
| `CircuitBreakerRequestBehavior<TMessage, TResponse>` | `IPipelineRequestBehavior<,>` | Request pipeline |
| `CircuitBreakerNotificationBehavior<TMessage>` | `IPipelineBehavior<>` | Notification pipeline |

> **Note:** Notification dispatch is fire-and-forget by design. Handler exceptions do **not** propagate back through the notification pipeline, so retry/timeout/circuit-breaker behaviors at the pipeline level do not observe handler failures for notifications. Use these behaviors to protect the pipeline itself (e.g., adapter calls, pre-processing logic).

## Retry

Retries a failed pipeline step up to `MaxRetryCount` times with an optional delay between attempts.

```csharp
builder.Services.AddNetMediateResilience(configureRetry: options =>
{
    options.MaxRetryCount = 3;
    options.Delay = TimeSpan.FromMilliseconds(100);
});
```

## Timeout

Cancels the request if it exceeds the configured timeout.

```csharp
builder.Services.AddNetMediateResilience(configureTimeout: options =>
{
    options.RequestTimeout = TimeSpan.FromSeconds(30);
    options.NotificationTimeout = TimeSpan.FromSeconds(5);
});
```

## Circuit Breaker

Opens the circuit after `FailureThreshold` consecutive failures and keeps it open for `OpenDuration`.

```csharp
builder.Services.AddNetMediateResilience(configureCircuitBreaker: options =>
{
    options.FailureThreshold = 5;
    options.OpenDuration = TimeSpan.FromMinutes(1);
});
```
