# NetMediate.Resilience

Adds retry, timeout and circuit-breaker pipeline behaviors to your NetMediate message pipeline.

## Installation

```bash
dotnet add package NetMediate.Resilience
```

## Registration

No manual registration is required. The source generator (`NetMediate.SourceGeneration`) automatically detects the `NetMediate.Resilience` assembly reference at compile time and registers all resilience behaviors before user-defined behaviors in the pipeline.

To customize the behavior options, register the option types **before** calling `AddNetMediate()`:

```csharp
// Override retry defaults (optional — defaults are applied automatically)
builder.Services.AddSingleton(new RetryBehaviorOptions
{
    MaxRetryCount = 3,
    Delay = TimeSpan.FromMilliseconds(200),
});

builder.Services.AddSingleton(new TimeoutBehaviorOptions
{
    RequestTimeout = TimeSpan.FromSeconds(10),
    NotificationTimeout = TimeSpan.FromSeconds(5),
});

builder.Services.AddSingleton(new CircuitBreakerBehaviorOptions
{
    FailureThreshold = 5,
    OpenDuration = TimeSpan.FromSeconds(30),
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

> **Note:** Notification dispatch is fire-and-forget by design. Handler exceptions do **not** propagate back through the notification pipeline, so retry/timeout/circuit-breaker behaviors at the pipeline level do not observe handler failures for notifications. Use these behaviors to protect the pipeline itself (e.g., adapter calls, pre-processing logic).

## Retry

Retries a failed pipeline step up to `MaxRetryCount` times with an optional delay between attempts.

```csharp
builder.Services.AddSingleton(new RetryBehaviorOptions
{
    MaxRetryCount = 3,
    Delay = TimeSpan.FromMilliseconds(100),
});
```

## Timeout

Cancels the request if it exceeds the configured timeout.

```csharp
builder.Services.AddSingleton(new TimeoutBehaviorOptions
{
    RequestTimeout = TimeSpan.FromSeconds(30),
    NotificationTimeout = TimeSpan.FromSeconds(5),
});
```

## Circuit Breaker

Opens the circuit after `FailureThreshold` consecutive failures and keeps it open for `OpenDuration`.

```csharp
builder.Services.AddSingleton(new CircuitBreakerBehaviorOptions
{
    FailureThreshold = 5,
    OpenDuration = TimeSpan.FromMinutes(1),
});
```
