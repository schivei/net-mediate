---
sidebar_position: 5
---

# Pipeline Behaviors

Pipeline composition is now static and based on GenDI decorators.
Legacy `IPipeline*Behavior` interfaces and pipeline delegates are obsolete and no longer supported.
Use `DecoratorForAttribute` with handler interfaces.

## Request Decorator (logging + timing)

```csharp
using System.Diagnostics;
using GenDI.Attributes;
using Microsoft.Extensions.Logging;
using NetMediate;

public record GetProductQuery(string ProductId);
public record ProductDto(string Id, string Name, decimal Price);

[DecoratorFor<IRequestHandler<GetProductQuery, ProductDto>>(Order = 1)]
public sealed class TimingLoggingDecorator(IRequestHandler<GetProductQuery, ProductDto> inner)
    : IRequestHandler<GetProductQuery, ProductDto>
{
    [Inject] public required ILogger<TimingLoggingDecorator> Logger { get; init; }

    public async Task<ProductDto> Handle(
        GetProductQuery message,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Logger.LogInformation("Handling {Query}", nameof(GetProductQuery));

        var result = await inner.Handle(message, cancellationToken);

        Logger.LogInformation("{Query} completed in {ElapsedMs} ms", nameof(GetProductQuery), sw.ElapsedMilliseconds);
        return result;
    }
}
```

## Command Decorator (audit trail)

Records every command dispatch before forwarding to the next handler.

```csharp
using GenDI.Attributes;
using NetMediate;

[ServiceInjection]
public interface IAuditWriter
{
    Task WriteAsync(string commandName, object? key, CancellationToken ct);
}

[DecoratorFor<ICommandHandler<PlaceOrderCommand>>(Order = 1)]
public sealed class CommandAuditDecorator(ICommandHandler<PlaceOrderCommand> inner) : ICommandHandler<PlaceOrderCommand>
{
    [Inject] public required IAuditWriter Audit { get; init; }

    public async Task Handle(
        PlaceOrderCommand message,
        CancellationToken cancellationToken = default)
    {
        await Audit.WriteAsync(nameof(PlaceOrderCommand), null, cancellationToken);
        await inner.Handle(message, cancellationToken);
    }
}
```

## Notification Decorator

Wraps notification handling with context enrichment.

```csharp
using GenDI.Attributes;
using Microsoft.Extensions.Logging;
using NetMediate;

[DecoratorFor<INotificationHandler<UserRegistered>>(Order = 1)]
public sealed class NotificationLoggingDecorator(INotificationHandler<UserRegistered> inner)
    : INotificationHandler<UserRegistered>
{
    [Inject] public required ILogger<NotificationLoggingDecorator> Logger { get; init; }

    public async Task Handle(
        UserRegistered message,
        CancellationToken cancellationToken = default)
    {
        using (Logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = message.UserId,
            ["NotificationType"] = nameof(UserRegistered)
        }))
        {
            await inner.Handle(message, cancellationToken);
        }
    }
}
```

> Legacy `IPipelineBehavior<TMessage, TResult>`, `IPipelineRequestBehavior<,>`, `IPipelineNotificationBehavior<>`, `IPipelineStreamBehavior<,>`, `PipelineBehaviorDelegate<,>` and `HandlerExecutionDelegate<,,>` are obsolete. Use decorators with `DecoratorForAttribute`.
