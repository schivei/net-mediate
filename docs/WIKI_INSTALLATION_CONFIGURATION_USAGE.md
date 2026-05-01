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

builder.Services.AddNetMediate(typeof(MyHandler).Assembly);
```

### Usage

```csharp
// ICommand: single handler, no return value
await mediator.Send(new CreateUserCommand("user-1"), cancellationToken);

// IRequest<TResponse>: single handler, returns a response
var dto = await mediator.Request<GetUserRequest, UserDto>(new GetUserRequest("user-1"), cancellationToken);

// INotification: dispatched to all registered handlers
await mediator.Notify(new UserCreatedNotification("user-1"), cancellationToken);

// IStream<TResponse>: handler yields multiple items asynchronously
await foreach (var item in mediator.RequestStream<GetEventsQuery, EventDto>(new GetEventsQuery(), cancellationToken))
    Console.WriteLine(item);
```

### Handler return types

All handler methods return `ValueTask` (not `Task`):

| Interface | `Handle` return type |
|---|---|
| `ICommandHandler<TMessage>` | `ValueTask` |
| `IRequestHandler<TMessage, TResponse>` | `ValueTask<TResponse>` |
| `INotificationHandler<TMessage>` | `ValueTask` |
| `IStreamHandler<TMessage, TResponse>` | `IAsyncEnumerable<TResponse>` |

## 2) Pipeline behaviors

### Configuration

Register behavior implementations in DI:

```csharp
services.AddSingleton(typeof(IRequestBehavior<,>), typeof(MyRequestBehavior<,>));
services.AddSingleton(typeof(ICommandBehavior<>), typeof(MyCommandBehavior<>));
services.AddSingleton(typeof(INotificationBehavior<>), typeof(MyNotificationBehavior<>));
services.AddSingleton(typeof(IStreamBehavior<,>), typeof(MyStreamBehavior<,>));
```

### Usage

Behaviors are executed in registration order (outer-to-inner for pre, inner-to-outer for post).

The `next` delegate always accepts `(message, cancellationToken)`:

```csharp
public sealed class MyRequestBehavior<TMessage, TResponse>
    : IRequestBehavior<TMessage, TResponse>
    where TMessage : notnull, IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        RequestHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken = default)
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
    configureTimeout: timeout => timeout.RequestTimeout = TimeSpan.FromSeconds(2),
    configureCircuitBreaker: breaker => breaker.FailureThreshold = 5
);
```

### Usage

Resilience behavior is applied transparently through mediator pipeline execution.

## 4) Quartz integration package (`NetMediate.Quartz`)

### Installation

```bash
dotnet add package NetMediate.Quartz
dotnet add package Quartz.Extensions.DependencyInjection
dotnet add package Quartz.Extensions.Hosting
```

### Configuration

```csharp
using NetMediate.Quartz;

builder.Services.AddQuartz(q => q.UseMicrosoftDependencyInjectionJobFactory());
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
builder.Services.AddNetMediateQuartz(opts => opts.GroupName = "MyApp");
```

### Usage

Notifications are scheduled as Quartz jobs before dispatch. Use a persistent `AdoJobStore` for crash recovery. See [QUARTZ.md](QUARTZ.md).

## 5) Adapters package (`NetMediate.Adapters`)

### Installation

```bash
dotnet add package NetMediate.Adapters
```

### Configuration

```csharp
using NetMediate.Adapters;

builder.Services.AddNetMediate(typeof(MyHandler).Assembly);
builder.Services.AddNetMediateAdapters(opts => opts.ThrowOnAdapterFailure = false);
builder.Services.AddNotificationAdapter<OrderPlaced, ServiceBusOrderAdapter>();
```

### Usage

Implement `INotificationAdapter<TMessage>` to forward notifications to external systems. See [ADAPTERS.md](ADAPTERS.md).

## 6) Source generation package (`NetMediate.SourceGeneration`)

### Installation

```bash
dotnet add package NetMediate.SourceGeneration
```

### Configuration

```csharp
builder.Services.AddNetMediateGenerated();
```

### Usage

Generated registration removes reflection scanning cost at startup for discovered handlers.

## 7) DataDog integration packages

### Installation

```bash
dotnet add package NetMediate.DataDog.OpenTelemetry
dotnet add package NetMediate.DataDog.Serilog
dotnet add package NetMediate.DataDog.ILogger
```

### Configuration and usage

See complete guide in [DATADOG.md](DATADOG.md).

## 8) Moq helpers package (`NetMediate.Moq`)

### Installation

```bash
dotnet add package NetMediate.Moq
```

### Usage

Use helper extensions for concise test setup. See [NETMEDIATE_MOQ_RECIPES.md](NETMEDIATE_MOQ_RECIPES.md).

