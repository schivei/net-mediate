# NetMediate.Adapters

`NetMediate.Adapters` is a supporting package that provides contracts, a standard envelope type, a pipeline behavior, and DI helpers for forwarding NetMediate notifications to external queues or stream systems.

## What this package does (and does not do)

**Does:**
- Provide `INotificationAdapter<TMessage>` — the contract your adapter implementations must satisfy.
- Provide `AdapterEnvelope<TMessage>` — a standard message wrapper with a unique ID, type name, and timestamp.
- Provide `NotificationAdapterBehavior<TMessage>` — a notification pipeline behavior that calls your adapters after core handlers run.
- Provide DI helpers (`AddNetMediateAdapters`, `AddNotificationAdapter`) for registration.

**Does not:**
- Include any concrete queue/stream implementations (RabbitMQ, Kafka, Azure Service Bus, AWS SNS/SQS, NATS, Redis Streams, …).
- Replace the core notification pipeline — adapters run *after* handlers.
- Require any specific serialization library.

## Installation

```bash
dotnet add package NetMediate.Adapters
```

## Quick start

### 1. Register the adapter behavior

```csharp
builder.Services.AddNetMediate();
builder.Services.AddNetMediateAdapters();
```

### 2. Implement an adapter

```csharp
public class KafkaUserRegisteredAdapter(IKafkaProducer producer) : INotificationAdapter<UserRegistered>
{
    public async Task ForwardAsync(AdapterEnvelope<UserRegistered> envelope, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(envelope);
        await producer.PublishAsync("user-registered", payload, cancellationToken);
    }
}
```

### 3. Register your adapter

```csharp
builder.Services.AddNotificationAdapter<UserRegistered, KafkaUserRegisteredAdapter>();
```

That's it. Every time `mediator.Notify(new UserRegistered(...))` is called, the `KafkaUserRegisteredAdapter` receives the message after all core handlers have run.

## Configuration

`AddNetMediateAdapters` accepts an optional `NotificationAdapterOptions` callback:

| Property | Default | Description |
|---|---|---|
| `ThrowOnAdapterFailure` | `true` | Propagates adapter exceptions to the caller. Set to `false` to swallow and log. |
| `InvokeAdaptersInParallel` | `false` | When `true`, all adapters for a message type fire concurrently via `Task.WhenAll`. |

```csharp
builder.Services.AddNetMediateAdapters(opts =>
{
    opts.ThrowOnAdapterFailure = false;     // Swallow adapter errors
    opts.InvokeAdaptersInParallel = true;   // Fire all adapters in parallel
});
```

## The AdapterEnvelope

`AdapterEnvelope<TMessage>` carries:

| Property | Type | Description |
|---|---|---|
| `MessageId` | `Guid` | Unique identifier generated per envelope. |
| `MessageType` | `string` | CLR type name of the notification. |
| `OccurredAt` | `DateTimeOffset` | UTC timestamp when the envelope was created. |
| `Message` | `TMessage` | The original notification payload. |

## Multiple adapters for the same message

You can register multiple adapters for the same message type. They are invoked in registration order (or in parallel if configured):

```csharp
builder.Services.AddNotificationAdapter<UserRegistered, KafkaUserRegisteredAdapter>();
builder.Services.AddNotificationAdapter<UserRegistered, AuditOutboxAdapter>();
```

## Pipeline position

`NotificationAdapterBehavior<TMessage>` calls `next` (running validation and all handlers) before invoking adapters. This means:

1. Validation runs.
2. Core `INotificationHandler<TMessage>` implementations run.
3. Adapters receive the `AdapterEnvelope` and forward to external systems.

## Combining with resilience and Quartz

Adapters compose naturally with other NetMediate extensions:

```csharp
builder.Services.AddNetMediate();
builder.Services.AddNetMediateResilience(retry => retry.MaxRetryCount = 3);
builder.Services.AddNetMediateAdapters(opts => opts.ThrowOnAdapterFailure = false);
builder.Services.AddNotificationAdapter<OrderPlaced, ServiceBusOrderAdapter>();
```

## Target frameworks

`NetMediate.Adapters` is published for:

- `net10.0`
- `netstandard2.0`
- `netstandard2.1`
