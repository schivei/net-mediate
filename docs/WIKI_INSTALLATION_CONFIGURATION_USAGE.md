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

// Scan all loaded assemblies
builder.Services.AddNetMediate();

// Scan specific assemblies
builder.Services.AddNetMediate(typeof(MyHandler).Assembly);

// Scan via containing type
builder.Services.AddNetMediate<MyHandler>();
```

### Usage

```csharp
// Command (fire-and-forget)
await mediator.Send(new CreateUserCommand("user-1"), cancellationToken);

// Request (query/response)
var dto = await mediator.Request<GetUserRequest, UserDto>(new GetUserRequest("user-1"), cancellationToken);

// Notification (fan-out)
await mediator.Notify(new UserCreatedNotification("user-1"), cancellationToken);

// Streaming
await foreach (var item in mediator.RequestStream<GetUserActivityQuery, ActivityDto>(query, cancellationToken))
    Console.WriteLine(item.Action);
```

## 2) Pipeline behaviors

### Configuration

Register behavior implementations in DI:

```csharp
services.AddScoped(typeof(IRequestBehavior<,>), typeof(MyRequestBehavior<,>));
services.AddScoped(typeof(ICommandBehavior<>), typeof(MyCommandBehavior<>));
services.AddScoped(typeof(INotificationBehavior<>), typeof(MyNotificationBehavior<>));
services.AddScoped(typeof(IStreamBehavior<,>), typeof(MyStreamBehavior<,>));
```

### Usage

Behaviors are executed in registration order (outer-to-inner for pre, inner-to-outer for post).

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

## 4) Source generation package (`NetMediate.SourceGeneration`)

### Installation

```xml
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

### Configuration

```csharp
builder.Services.AddNetMediateGenerated();
```

### Usage

Generated registration replaces reflection scanning at startup — zero startup reflection,
AOT-safe.  See [SOURCE_GENERATION.md](SOURCE_GENERATION.md) for full details.

## 5) Channel-based background notifications (`NetMediate.InternalNotifier`)

### Installation

```bash
dotnet add package NetMediate.InternalNotifier
```

### Configuration

```csharp
using NetMediate.InternalNotifier;

builder.Services.AddNetMediate().AddNetMediateInternalNotifier();
```

### Usage

Notifications are written to an unbounded `Channel<T>` and consumed by a `BackgroundService`
worker.  The `mediator.Notify(...)` call returns **immediately** without waiting for handlers.

## 6) Inline test notification provider (`NetMediate.InternalNotifier.Test`)

### Installation

```bash
dotnet add package NetMediate.InternalNotifier.Test
```

### Configuration

```csharp
using NetMediate.InternalNotifier.Test;

services.AddNetMediate().AddNetMediateTestNotifier();
```

### Usage

Dispatches notifications **inline and synchronously** — no `Task.Delay` needed in tests to wait
for handler completion.

## 7) Custom notification provider base (`NetMediate.Notifications`)

### Installation

```bash
dotnet add package NetMediate.Notifications
```

### Usage

```csharp
using NetMediate.Notifications;

public class MyBusNotificationProvider : NotificationProviderBase
{
    public override ValueTask EnqueueAsync<TMessage>(
        TMessage message,
        CancellationToken ct)
    {
        // publish to your own bus, outbox, etc.
        // inject INotificationDispatcher to dispatch to handlers when the message is consumed
        return ValueTask.CompletedTask;
    }
}

// Registration
builder.Services.AddNetMediate().UseCustomNotificationProvider<MyBusNotificationProvider>();
```

## 8) DataDog integration packages

### Installation

```bash
dotnet add package NetMediate.DataDog.OpenTelemetry
dotnet add package NetMediate.DataDog.Serilog
dotnet add package NetMediate.DataDog.ILogger
```

### Configuration and usage

See complete guide in [DATADOG.md](DATADOG.md).

## 9) Compatibility package (`NetMediate.Compat`)

### Installation

```bash
dotnet add package NetMediate.Compat
```

### Usage

Use MediatR-compatible contracts (`MediatR.IMediator`, `IRequest`, `INotification`, handler
interfaces, `AddMediatR`) while delegating runtime execution to NetMediate.
See [MEDIATR_MIGRATION_GUIDE.md](MEDIATR_MIGRATION_GUIDE.md) for step-by-step instructions.

## 10) Moq helpers package (`NetMediate.Moq`)

### Installation

```bash
dotnet add package NetMediate.Moq
```

### Usage

Use helper extensions for concise test setup. See [NETMEDIATE_MOQ_RECIPES.md](NETMEDIATE_MOQ_RECIPES.md).
