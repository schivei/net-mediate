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
await mediator.Send(new CreateUserCommand("user-1"), cancellationToken);
var dto = await mediator.Request(new GetUserRequest("user-1"), cancellationToken);
await mediator.Notify(new UserCreatedNotification("user-1"), cancellationToken);
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

```bash
dotnet add package NetMediate.SourceGeneration
```

### Configuration

```csharp
builder.Services.AddNetMediateGenerated();
```

### Usage

Generated registration removes reflection scanning cost at startup for discovered handlers.

## 5) DataDog integration packages

### Installation

```bash
dotnet add package NetMediate.DataDog.OpenTelemetry
dotnet add package NetMediate.DataDog.Serilog
dotnet add package NetMediate.DataDog.ILogger
```

### Configuration and usage

See complete guide in [DATADOG.md](DATADOG.md).

## 6) Moq helpers package (`NetMediate.Moq`)

### Installation

```bash
dotnet add package NetMediate.Moq
```

### Usage

Use helper extensions for concise test setup. See [NETMEDIATE_MOQ_RECIPES.md](NETMEDIATE_MOQ_RECIPES.md).
