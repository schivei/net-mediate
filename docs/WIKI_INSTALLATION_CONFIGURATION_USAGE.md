# Wiki: Installation, Configuration, and Usage

This page centralizes installation, configuration, and usage details for each NetMediate resource.

## 1) Core package (`NetMediate`)

### Installation

```bash
dotnet add package NetMediate
```

### Configuration

```csharp
using NetMediate;

// Register handlers explicitly (required — no assembly scanning)
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterHandler<ICommandHandler<CreateUserCommand>, CreateUserCommandHandler, CreateUserCommand, Task>();
    configure.RegisterHandler<IRequestHandler<GetUserRequest, UserDto>, GetUserRequestHandler, GetUserRequest, Task<UserDto>>();
    configure.RegisterHandler<INotificationHandler<UserCreatedNotification>, UserCreatedNotificationHandler, UserCreatedNotification, Task>();
    configure.RegisterHandler<IStreamHandler<GetEventsQuery, EventDto>, GetEventsQueryHandler, GetEventsQuery, IAsyncEnumerable<EventDto>>();
});

// Or use the source generator (recommended for AOT — install NetMediate.SourceGeneration)
builder.Services.AddNetMediateGenerated();
```

### Usage

```csharp
// Command: dispatched to all registered handlers in parallel, no return value
await mediator.Send(new CreateUserCommand("user-1"), cancellationToken);

// Request: single handler, returns a response
var dto = await mediator.Request<GetUserRequest, UserDto>(new GetUserRequest("user-1"), cancellationToken);

// Notification: fire-and-forget dispatch to all registered handlers
await mediator.Notify(new UserCreatedNotification("user-1"), cancellationToken);

// Stream: all registered handlers iterated; each yields items asynchronously
await foreach (var item in mediator.RequestStream<GetEventsQuery, EventDto>(new GetEventsQuery(), cancellationToken))
    Console.WriteLine(item);
```

### Message types

No marker interfaces are required. Any plain class or record can be a message:

```csharp
public record CreateUserCommand(string Email);        // command
public record GetUserRequest(string UserId);          // request
public record UserCreatedNotification(string UserId); // notification
public record GetEventsQuery(int MaxItems);           // stream request
```

### Handler return types and dispatch semantics

All handler `Handle` methods return `Task` or `Task<TResponse>`:

| Interface | `Handle` return type | Dispatch semantics |
|---|---|---|
| `ICommandHandler<TMessage>` | `Task` | All registered handlers, in parallel (`Task.WhenAll`) |
| `IRequestHandler<TMessage, TResponse>` | `Task<TResponse>` | First registered handler only |
| `INotificationHandler<TMessage>` | `Task` | All registered handlers, fire-and-forget background dispatch |
| `IStreamHandler<TMessage, TResponse>` | `IAsyncEnumerable<TResponse>` | All registered handlers iterated; results aggregated |

## 2) Pipeline behaviors

### Configuration

Register behavior implementations using the builder or directly in DI:

```csharp
// Via builder (closed-type, fully AOT-safe)
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterBehavior<AuditRequestBehavior<MyRequest, MyResponse>, MyRequest, Task<MyResponse>>();
});

// Via open-generic DI (requires JIT; not recommended for AOT)
services.AddSingleton(typeof(IPipelineRequestBehavior<,>), typeof(AuditRequestBehavior<,>));
services.AddSingleton(typeof(IPipelineBehavior<>), typeof(LogNotificationBehavior<>));
```

### Usage

Behaviors are executed in registration order (outer-to-inner for pre, inner-to-outer for post).

The `next` delegate accepts `(message, cancellationToken)`:

```csharp
public sealed class AuditRequestBehavior<TMessage, TResponse>
    : IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{
    public async Task<TResponse> Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        // pre-processing
        var result = await next(message, cancellationToken);
        // post-processing
        return result;
    }
}
```

## 3) Resilience package (`NetMediate.Resilience`)

### Installation

```bash
dotnet add package NetMediate.Resilience
```

### Configuration

```csharp
using NetMediate.Resilience;

builder.Services.AddNetMediateResilience(
    configureRetry: retry => retry.MaxRetryCount = 2,
    configureTimeout: timeout => timeout.RequestTimeout = TimeSpan.FromSeconds(30),
    configureCircuitBreaker: cb => cb.FailureThreshold = 5
);
```

## 4) Source generation (`NetMediate.SourceGeneration`)

### Installation

```bash
dotnet add package NetMediate.SourceGeneration
```

### Usage

Add the package and the source generator automatically produces `AddNetMediateGenerated()`:

```csharp
builder.Services.AddNetMediateGenerated();
```

The generator scans your assembly for all `ICommandHandler<>`, `IRequestHandler<,>`, `INotificationHandler<>`, and `IStreamHandler<,>` implementations and emits strongly-typed `RegisterHandler<>` calls — no reflection, fully AOT-compatible.

## 5) Adapters (`NetMediate.Adapters`)

### Installation

```bash
dotnet add package NetMediate.Adapters
```

### Configuration

```csharp
using NetMediate.Adapters;

builder.Services.AddNetMediateAdapters();
// Register adapters:
builder.Services.AddSingleton<INotificationAdapter<UserCreatedNotification>, UserCreatedKafkaAdapter>();
```

## 6) Quartz (`NetMediate.Quartz`)

### Installation

```bash
dotnet add package NetMediate.Quartz
```

### Configuration

```csharp
using NetMediate.Quartz;

builder.Services.AddNetMediateQuartz();
```

## 7) Moq (`NetMediate.Moq`)

### Installation

```bash
dotnet add package NetMediate.Moq
```

### Usage

```csharp
var mediator = new MoqMediator(serviceProvider);
// Stub, verify, and spy on mediator calls in unit tests.
```
