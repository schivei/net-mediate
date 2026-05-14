---
sidebar_position: 3
---

# Message Types

> **GenDI pattern:** Message handlers and supporting services in the examples below are expected to follow the same GenDI-based activation model used across the ecosystem: `[Injectable]` + `[Inject]` when you need DI-controlled implementations.

NetMediate supports four different message types, each designed for specific communication patterns. **No marker interfaces are required** - any class or record can be a message.

## Overview

| Message Type | Use Case | Handler Count | Return Value |
|--------------|----------|---------------|--------------|
| **Command** | Side-effects, multi-handler actions | Multiple (sequential) | `Task` |
| **Request** | Query/response pattern | Single | `Task<TResponse>` |
| **Notification** | Fire-and-forget events | Multiple (parallel, fire-and-forget) | `Task` |
| **Stream** | Async data streams | Multiple (merged) | `IAsyncEnumerable<T>` |

## Commands

Commands represent imperative actions that should be executed. All registered handlers run sequentially.

### Characteristics
- ✅ Multiple handlers allowed
- ✅ Handlers execute sequentially in registration order
- ✅ No return value
- ✅ Exceptions propagate to caller

### Example

```csharp
// Define a command
public record ProcessPaymentCommand(string OrderId, decimal Amount);

// Create handlers
public class PaymentProcessor : ICommandHandler<ProcessPaymentCommand>
{
    public async Task Handle(ProcessPaymentCommand command, CancellationToken ct)
    {
        // Process payment logic
        await Task.CompletedTask;
    }
}

public class PaymentAuditor : ICommandHandler<ProcessPaymentCommand>
{
    public async Task Handle(ProcessPaymentCommand command, CancellationToken ct)
    {
        // Audit logic - runs after PaymentProcessor
        await Task.CompletedTask;
    }
}

// Usage
await mediator.SendProcessPaymentCommandAsync(new ProcessPaymentCommand("ORD-123", 99.99m));
```

## Requests

Requests follow the request-response pattern. Only one handler is invoked, returning a typed result.

### Characteristics
- ✅ Single handler only (first registered)
- ✅ Returns a typed response
- ✅ Async execution (`Task<TResponse>`)
- ✅ Exceptions propagate to caller

### Example

```csharp
// Usage
var user = await mediator.RequestGetUserQueryAsync(
    new GetUserQuery("123"));

// Define request and response
public record GetUserQuery(string UserId);
public record UserDto(string Id, string Name, string Email);

// Create handler
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetUserQuery query, CancellationToken ct)
    {
        // Fetch user from database
        return new UserDto("123", "John Doe", "john@example.com");
    }
}
```

## Notifications

Notifications are events dispatched to multiple handlers simultaneously. Handlers are started concurrently (fire-and-forget) — handler exceptions are logged by the executor but do not propagate to the caller.

### Characteristics
- ✅ Multiple handlers allowed
- ✅ All handlers started concurrently (fire-and-forget)
- ✅ Handler exceptions are logged by the executor (do not propagate to caller)
- ✅ No return value
- ✅ Best for event-driven architectures

### Example

```csharp
// Usage - all handlers started concurrently (fire-and-forget)
await mediator.NotifyOrderShippedAsync(new OrderShipped("ORD-456", "TRACK-789", DateTime.UtcNow));

// Define a notification
public record OrderShipped(string OrderId, string TrackingNumber, DateTime ShippedAt);

// Create handlers
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public class EmailNotifier : INotificationHandler<OrderShipped>
{
    public async Task Handle(OrderShipped notification, CancellationToken ct)
    {
        // Send email to customer
        await Task.CompletedTask;
    }
}

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 2)]
public class InventoryUpdater : INotificationHandler<OrderShipped>
{
    public async Task Handle(OrderShipped notification, CancellationToken ct)
    {
        // Update inventory
        await Task.CompletedTask;
    }
}
```

### Batch Notifications

Send multiple notifications at once. Each message's pipeline is dispatched in parallel (`Task.WhenAll` across messages). Within each message, all handlers are also started in parallel (fire-and-forget):

```csharp
var notifications = new[]
{
    new OrderShipped("ORD-1", "TRACK-1", DateTime.UtcNow),
    new OrderShipped("ORD-2", "TRACK-2", DateTime.UtcNow),
    new OrderShipped("ORD-3", "TRACK-3", DateTime.UtcNow)
};

await mediator.NotifyOrderShippedAsync(notifications);
```

## Streams

Streams handle requests that return multiple values over time using `IAsyncEnumerable<T>`.

### Characteristics
- ✅ Multiple handlers supported (items merged sequentially)
- ✅ Returns `IAsyncEnumerable<TResponse>`
- ✅ Supports backpressure
- ✅ Cancellation-aware

### Example

```csharp
// Usage
await foreach (var order in mediator.StreamGetRecentOrdersQueryAsync(
    new GetRecentOrdersQuery("CUST-123", 10)))
{
    Console.WriteLine($"Order: {order.OrderId}, Total: {order.Total}");
}

// Define stream request and response
public record GetRecentOrdersQuery(string CustomerId, int MaxResults);
public record OrderSummary(string OrderId, decimal Total, DateTime CreatedAt);

// Create handler
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public class GetRecentOrdersHandler : IStreamHandler<GetRecentOrdersQuery, OrderSummary>
{
    public async IAsyncEnumerable<OrderSummary> Handle(
        GetRecentOrdersQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Simulate streaming results from database
        for (int i = 0; i < query.MaxResults; i++)
        {
            if (ct.IsCancellationRequested)
                yield break;

            yield return new OrderSummary(
                $"ORD-{i}",
                100.0m * i,
                DateTime.UtcNow.AddDays(-i));

            await Task.Delay(100, ct); // Simulate processing
        }
    }
}
```

## Message Design Best Practices

### Use Records

Records provide value-based equality and are perfect for messages:

```csharp
// ✅ Good - immutable record
public record CreateOrderCommand(string CustomerId, List<OrderItem> Items);

// ❌ Avoid - mutable class
public class CreateOrderCommand
{
    public string CustomerId { get; set; }
    public List<OrderItem> Items { get; set; }
}
```

### Keep Messages Simple

Messages should only contain data, no behavior:

```csharp
// ✅ Good - data only
public record PlaceOrderCommand(string CustomerId, decimal Total);

// ❌ Avoid - contains logic
public record PlaceOrderCommand(string CustomerId, decimal Total)
{
    public bool IsValid() => Total > 0; // Business logic doesn't belong here
}
```

### Use Descriptive Names

Choose names that clearly describe the intent:

```csharp
// ✅ Good - clear intent
public record CancelOrderCommand(string OrderId, string Reason);
public record GetOrderByIdQuery(string OrderId);
public record OrderCancelledNotification(string OrderId, DateTime CancelledAt);

// ❌ Avoid - vague names
public record UpdateOrder(string Id);
public record OrderEvent(string Data);
```

## Optional IMessage Marker

While not required, you can use the `IMessage` marker interface for your own abstractions:

```csharp
public record MyCommand(...) : IMessage;

// Generic constraint example
public void LogMessage<T>(T message) where T : IMessage
{
    Console.WriteLine($"Message: {typeof(T).Name}");
}
```

## Unhandled Messages

Behavior when no handlers are registered:

- **Commands** (`Send`): Silent no-op
- **Notifications** (`Notify`): Silent no-op
- **Requests** (`Request`): Throws `InvalidOperationException`
- **Streams** (`RequestStream`): Throws `InvalidOperationException`

## Next Steps

- [Handlers](./handlers.md) - Learn how to implement handlers
- [Commands Guide](../guides/commands.md) - Deep dive into commands
- [Requests Guide](../guides/requests.md) - Deep dive into requests
- [Notifications Guide](../guides/notifications.md) - Deep dive into notifications
- [Streams Guide](../guides/streams.md) - Deep dive into streams
