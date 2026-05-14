---
sidebar_position: 5
---

# Pipeline Behaviors

> **GenDI pattern:** Use `[Injectable]` + `[Inject]` for regular application services. Concrete non-generic pipeline behaviors can also use `[Injectable]`. Reserve manual `builder.Services` registration for generic/open behavior implementations.

Pipeline behaviors are middleware-style interceptors that wrap handler execution. Each behavior receives the routing key, the message, the `next` delegate, and a cancellation token. Calling `next` continues the pipeline; returning without calling `next` short-circuits it.

All behavior interfaces default to `ServiceLifetime.Transient` with `ThreadIsolationPolicy.Transient` (see the [Handlers lifetime table](../getting-started/handlers.md#handler-lifetime)). Override with `[Injectable]` when a different lifetime is needed.

## Request Behavior (logging + timing)

Wraps every request dispatch with structured log entries and elapsed-time measurement.

```csharp
using System.Diagnostics;
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate;

public record GetProductQuery(string ProductId);
public record ProductDto(string Id, string Name, decimal Price);

[Injectable(ServiceLifetime.Singleton, Group = 10, Order = 1)]
public sealed class TimingLoggingBehavior : IPipelineRequestBehavior<GetProductQuery, ProductDto>
{
    [Inject] public required ILogger<TimingLoggingBehavior> Logger { get; init; }

    public async Task<ProductDto> Handle(
        object? key,
        GetProductQuery message,
        PipelineBehaviorDelegate<GetProductQuery, Task<ProductDto>> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        Logger.LogInformation("Handling {Query} (key={Key})", nameof(GetProductQuery), key);

        var result = await next(key, message, cancellationToken);

        Logger.LogInformation("{Query} completed in {ElapsedMs} ms", nameof(GetProductQuery), sw.ElapsedMilliseconds);
        return result;
    }
}

builder.Services.AddNetMediate();
```

## Command Behavior (audit trail)

Records every command dispatch to an audit log before forwarding to the handlers.

```csharp
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

[ServiceInjection]
public interface IAuditWriter
{
    Task WriteAsync(string commandName, object? key, CancellationToken ct);
}

[Injectable(ServiceLifetime.Transient, Group = 5, Order = 1)]
public sealed class CommandAuditBehavior : IPipelineCommandBehavior<PlaceOrderCommand>
{
    [Inject] public required IAuditWriter Audit { get; init; }

    public async Task Handle(
        object? key,
        PlaceOrderCommand message,
        PipelineBehaviorDelegate<PlaceOrderCommand, Task> next,
        CancellationToken cancellationToken)
    {
        await Audit.WriteAsync(nameof(PlaceOrderCommand), key, cancellationToken);
        await next(key, message, cancellationToken);
    }
}
```

## Notification Behavior (error context enrichment)

Enriches the logging scope before the handler phase so that all handler log lines carry the notification type.

```csharp
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate;

[Injectable(ServiceLifetime.Transient, Group = 5, Order = 1)]
public sealed class NotificationLoggingBehavior : IPipelineNotificationBehavior<UserRegistered>
{
    [Inject] public required ILogger<NotificationLoggingBehavior> Logger { get; init; }

    public async Task Handle(
        object? key,
        UserRegistered message,
        PipelineBehaviorDelegate<UserRegistered, Task> next,
        CancellationToken cancellationToken)
    {
        using (Logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = message.UserId,
            ["NotificationType"] = nameof(UserRegistered)
        }))
        {
            await next(key, message, cancellationToken);
        }
    }
}
```

> **Note:** Because `IMediator.Notify` is fire-and-forget, behavior exceptions are logged and suppressed by the `NotificationPipelineExecutor` — they do not propagate to the caller. See [Notifications](./notifications.md) for details.

