---
sidebar_position: 1
---

# Commands

Commands represent imperative actions in your application. This guide covers everything you need to know about using commands effectively.

## Overview

Commands are dispatched to **all** registered handlers **sequentially** in registration order. Use commands when you want to trigger side-effects across multiple consumers with no return value.

For the complete commands documentation, see the main [README](https://github.com/schivei/net-mediate#commands).

## Complete Example

The following example models an e-commerce order placement flow. Two command handlers are chained: the first persists the order, the second publishes a domain event to an external message bus.

```csharp
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate;

// ----- Messages -----
public record PlaceOrderCommand(
    string CustomerId,
    IReadOnlyList<OrderItem> Items,
    string ShippingAddress);

public record OrderItem(string ProductId, int Quantity, decimal UnitPrice);

// ----- Shared services -----
[ServiceInjection]
public interface IOrderRepository
{
    Task<string> SaveAsync(PlaceOrderCommand command, CancellationToken ct);
}

[ServiceInjection]
public interface IEventBus
{
    Task PublishAsync<T>(T @event, CancellationToken ct) where T : notnull;
}

// ----- Handler 1: persist the order -----
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public class PersistOrderHandler : ICommandHandler<PlaceOrderCommand>
{
    [Inject] public required IOrderRepository Repository { get; init; }
    [Inject] public required ILogger<PersistOrderHandler> Logger { get; init; }

    public async Task Handle(PlaceOrderCommand command, CancellationToken ct)
    {
        var orderId = await Repository.SaveAsync(command, ct);
        Logger.LogInformation("Order {OrderId} persisted for customer {CustomerId}",
            orderId, command.CustomerId);
    }
}

// ----- Handler 2: publish domain event -----
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 2)]
public class PublishOrderPlacedEventHandler : ICommandHandler<PlaceOrderCommand>
{
    [Inject] public required IEventBus EventBus { get; init; }

    public async Task Handle(PlaceOrderCommand command, CancellationToken ct) =>
        await EventBus.PublishAsync(new OrderPlacedEvent(command.CustomerId), ct);
}

public record OrderPlacedEvent(string CustomerId);

// ----- Usage (e.g. from a Minimal API endpoint) -----
app.MapPost("/orders", async (PlaceOrderCommand cmd, IMediator mediator, CancellationToken ct) =>
{
    await mediator.SendPlaceOrderCommandAsync(cmd, ct);
    return Results.Accepted();
});
```

Handler 1 runs first (lower `Order` value), then handler 2. If handler 1 throws, handler 2 is not reached and the exception propagates to the caller.



## Keyed Dispatch

Register handlers under routing keys and dispatch to a specific subset at runtime. This is useful for scenarios such as queue/topic routing, tenant isolation, or environment-specific handling:

```csharp
builder.Services.AddNetMediate();

// Dispatch to null-key (default) handlers
await mediator.SendMyCommandAsync(new MyCommand(), cancellationToken);

// Dispatch only to "audit" handlers
await mediator.SendMyCommandAsync("audit", new MyCommand(), cancellationToken);

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public sealed class DefaultHandler : ICommandHandler<MyCommand>
{
    public Task Handle(MyCommand message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 2, Key = "audit")]
public sealed class AuditHandler : ICommandHandler<MyCommand>
{
    public Task Handle(MyCommand message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
```

The `key` is propagated through the entire pipeline — behaviors receive it in their `Handle(object? key, ...)` signature and can use it for routing, logging, or conditional logic.

> **Keyless dispatch:** A `null` key flows through the pipeline unchanged. `mediator.SendMyCommandAsync(command, ct)` and `mediator.SendMyCommandAsync(null, command, ct)` are equivalent and target the non-keyed handlers registered in the container.

> **NativeAOT:** Keyed dispatch is fully NativeAOT + Trimming compatible. GenDI resolves keyed services; NetMediate dispatch uses `GetKeyedServices`/`GetRequiredKeyedService` at runtime. Both keyed and non-keyed dispatch are safe for NativeAOT and trimmed deployments.

## See Also

- [Handlers](../getting-started/handlers.md)
- [Pipeline Behaviors](./pipeline-behaviors.md)
