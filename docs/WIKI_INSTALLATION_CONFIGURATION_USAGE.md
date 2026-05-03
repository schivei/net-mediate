# Wiki: Installation, Configuration, and Usage

This page centralizes installation, configuration, and usage details for each NetMediate resource.

## 1) Core package (`NetMediate`)

### Installation

```bash
dotnet add package NetMediate
```

### Configuration

All `AddNetMediate` overloads return an `IMediatorServiceBuilder`. Handler registration is always explicit — there is no assembly scanning.

```csharp
using NetMediate;

// Explicit registration (AOT-safe)
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterCommandHandler<CreateUserCommandHandler, CreateUserCommand>();
    configure.RegisterRequestHandler<GetUserRequestHandler, GetUserRequest, UserDto>();
    configure.RegisterNotificationHandler<UserCreatedNotificationHandler, UserCreatedNotification>();
    configure.RegisterStreamHandler<GetEventsQueryHandler, GetEventsQuery, EventDto>();
});

// Or use the source generator (recommended for AOT)
builder.Services.AddNetMediateGenerated();
```

### Usage

```csharp
// Command: dispatched sequentially to all registered handlers, no return value
await mediator.Send(new CreateUserCommand("user-1"), cancellationToken);

// Request: single handler, returns a response
var dto = await mediator.Request<GetUserRequest, UserDto>(new GetUserRequest("user-1"), cancellationToken);

// Notification: fire-and-forget dispatch to all registered handlers (errors logged per handler)
await mediator.Notify(new UserCreatedNotification("user-1"), cancellationToken);

// Notification (batch): each message dispatched individually
await mediator.Notify(new[] { n1, n2, n3 }, cancellationToken);

// Stream: single handler; yields items asynchronously
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

The optional `IMessage` marker interface is available if you want to constrain message types in your own abstractions.

### Handler return types and dispatch semantics

All handler `Handle` methods return `Task` or `Task<TResponse>`:

| Interface | `Handle` return type | Dispatch semantics |
|---|---|---|
| `ICommandHandler<TMessage>` | `Task` | All registered handlers, **sequential** in registration order |
| `IRequestHandler<TMessage, TResponse>` | `Task<TResponse>` | Single handler (first registered) |
| `INotificationHandler<TMessage>` | `Task` | All registered handlers, fire-and-forget; errors logged per handler |
| `IStreamHandler<TMessage, TResponse>` | `IAsyncEnumerable<TResponse>` | Single handler; yields items lazily |

> **Unhandled messages**: `Send` and `Notify` are silent no-ops when no handler is registered. `Request` and `RequestStream` throw `InvalidOperationException`.

### Optional base class

`ABaseHandler<TMessage, TResult>` is an optional abstract base that implements `IHandler<TMessage, TResult>`. You are not required to use it.

## 2) Pipeline behaviors

### Configuration

Register behavior implementations using the builder or directly in DI:

```csharp
// Via builder (closed-type, fully AOT-safe)
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterBehavior<MyLoggingBehavior, MyRequest, Task<MyResponse>>();
});

// Via open-generic DI (not recommended for NativeAOT)
services.AddSingleton(typeof(IPipelineRequestBehavior<,>), typeof(AuditRequestBehavior<,>));
services.AddSingleton(typeof(IPipelineBehavior<>), typeof(LogNotificationBehavior<>));
```

### Behavior interfaces

| Interface | Applies to |
|---|---|
| `IPipelineBehavior<TMessage, TResult>` | Any pipeline; `TResult` is `Task`, `Task<TResponse>`, or `IAsyncEnumerable<TResponse>` |
| `IPipelineBehavior<TMessage>` | Notification pipeline shorthand (`TResult = Task`) |
| `IPipelineRequestBehavior<TMessage, TResponse>` | Request pipeline shorthand (`TResult = Task<TResponse>`) |
| `IPipelineStreamBehavior<TMessage, TResponse>` | Stream pipeline shorthand (`TResult = IAsyncEnumerable<TResponse>`) |

### Usage

The `next` delegate accepts `(message, cancellationToken)`. Behaviors execute in registration order (outer-to-inner for pre, inner-to-outer for post):

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

> **Validation**: there is no built-in validation in NetMediate. Implement your own validation as a pipeline behavior. See [VALIDATION_BEHAVIOR_SAMPLE.md](VALIDATION_BEHAVIOR_SAMPLE.md) for an example.

## 3) Resilience package (`NetMediate.Resilience`)

### Installation

```bash
dotnet add package NetMediate.Resilience
```

### Configuration

```csharp
using NetMediate.Resilience;

builder.Services.AddNetMediateResilience(
    configureRetry: retry =>
    {
        retry.MaxRetryCount = 2;
        retry.Delay = TimeSpan.Zero;
    },
    configureTimeout: timeout =>
    {
        timeout.RequestTimeout = TimeSpan.FromSeconds(30);
        timeout.NotificationTimeout = TimeSpan.FromSeconds(30);
    },
    configureCircuitBreaker: cb =>
    {
        cb.FailureThreshold = 5;
        cb.OpenDuration = TimeSpan.FromSeconds(30);
    }
);
```

See [RESILIENCE.md](RESILIENCE.md) for full details.

## 4) Source generation (`NetMediate.SourceGeneration`)

### Installation

```xml
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

### Usage

```csharp
builder.Services.AddNetMediateGenerated();
```

The generator discovers all `ICommandHandler<>`, `IRequestHandler<,>`, `INotificationHandler<>`, and `IStreamHandler<,>` implementations in your project and emits strongly-typed closed-type registrations — no reflection, fully AOT-compatible. See [SOURCE_GENERATION.md](SOURCE_GENERATION.md).

## 5) Adapters (`NetMediate.Adapters`)

### Installation

```bash
dotnet add package NetMediate.Adapters
```

### Configuration

```csharp
using NetMediate.Adapters;

// Register the adapter pipeline behavior
builder.Services.AddNetMediateAdapters(opts =>
{
    opts.ThrowOnAdapterFailure = true;      // default: propagate adapter errors
    opts.InvokeAdaptersInParallel = false;  // default: sequential
});

// Register your adapter implementation
builder.Services.AddNotificationAdapter<UserCreatedNotification, UserCreatedKafkaAdapter>();
```

See [ADAPTERS.md](ADAPTERS.md) for full details.

## 6) Quartz (`NetMediate.Quartz`)

### Installation

```bash
dotnet add package NetMediate.Quartz
```

### Configuration

```csharp
using NetMediate.Quartz;

builder.Services.AddQuartz(q => q.UseMicrosoftDependencyInjectionJobFactory());
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
builder.Services.AddNetMediateQuartz(opts =>
{
    opts.GroupName = "MyApp";
    opts.MisfireRetryCount = 1;
});
```

See [QUARTZ.md](QUARTZ.md) for full details.

## 7) Moq (`NetMediate.Moq`)

### Installation

```bash
dotnet add package NetMediate.Moq
```

### Usage

```csharp
using NetMediate.Moq;

// Create and register a mediator mock
var mediatorMock = services.AddMediatorMock();
mediatorMock.Setup(m => m.Send(It.IsAny<MyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsCompletedTask();

// Replace any service with a singleton mock
var clockMock = services.AddMockSingleton<IClock>();
```

See [NETMEDIATE_MOQ_RECIPES.md](NETMEDIATE_MOQ_RECIPES.md) for full details.

## 8) DataDog integrations

### Installation

```bash
dotnet add package NetMediate.DataDog.OpenTelemetry
dotnet add package NetMediate.DataDog.Serilog
dotnet add package NetMediate.DataDog.ILogger
```

See [DATADOG.md](DATADOG.md) for full details.
