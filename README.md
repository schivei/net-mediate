# NetMediate

[![CI/CD Pipeline](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml)
[![NuGet](https://img.shields.io/nuget/v/NetMediate?style=flat)](https://www.nuget.org/packages/NetMediate/)

A lightweight and efficient .NET implementation of the Mediator pattern for in-process messaging and communication between components.

## Table of Contents

- [Introduction](#introduction)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Usage Examples](#usage-examples)
  - [Notifications](#notifications)
  - [Commands](#commands)
  - [Requests](#requests)
  - [Streams](#streams)
  - [Pipeline Behaviors](#pipeline-behaviors--interceptors)
- [Framework Support](#framework-support)
- [Companion Guides](#companion-guides)
- [Contributing](#contributing)
- [License](#license)

## Introduction

NetMediate is a mediator pattern library for .NET that enables decoupled communication between components in your application. It provides a simple and flexible way to send commands, publish notifications, make requests, and handle streaming responses while maintaining clean architecture principles.

### Key Features

- **Commands**: Send one-way messages to all registered handlers sequentially
- **Notifications**: Publish messages to multiple handlers (fire-and-forget; per-handler errors are logged)
- **Requests**: Send a message to a single handler and receive a typed response
- **Streaming**: Handle requests that return multiple responses over time via `IAsyncEnumerable`
- **Pipeline Behaviors**: Interceptors with pre/post flow for every message kind
- **Optional resilience package**: Retry, timeout, and circuit-breaker behaviors in `NetMediate.Resilience`
- **OpenTelemetry-ready diagnostics**: Built-in `ActivitySource`/`Meter` for Send/Request/Notify/Stream
- **Optional DataDog integrations**: OpenTelemetry, Serilog, and ILogger support packages
- **Dependency Injection**: Seamless integration with Microsoft.Extensions.DependencyInjection
- **Cancellation Support**: Full cancellation token support across all operations
- **Broad runtime compatibility**: Multi-targeted for `net10.0`, `netstandard2.0`, and `netstandard2.1`

## Installation

### Package Manager Console
```powershell
Install-Package NetMediate
```

### .NET CLI
```bash
dotnet add package NetMediate
```

### PackageReference
```xml
<PackageReference Include="NetMediate" Version="x.x.x" />
```

### Optional companion packages
```xml
<PackageReference Include="NetMediate.Moq" Version="x.x.x" />
<PackageReference Include="NetMediate.Resilience" Version="x.x.x" />
<PackageReference Include="NetMediate.Quartz" Version="x.x.x" />
<PackageReference Include="NetMediate.Adapters" Version="x.x.x" />
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<PackageReference Include="NetMediate.DataDog.OpenTelemetry" Version="x.x.x" />
<PackageReference Include="NetMediate.DataDog.Serilog" Version="x.x.x" />
<PackageReference Include="NetMediate.DataDog.ILogger" Version="x.x.x" />
```

- **NetMediate.Moq**: lightweight Moq helpers for unit and integration tests (`Mocking.Create`, `AddMockSingleton`, async setup extensions).
- **NetMediate.Resilience**: optional retry, timeout, and circuit-breaker pipeline behaviors for request and notification flows.
- **NetMediate.Quartz**: persists notifications as Quartz.NET jobs, enabling crash recovery and cluster-distributed notification execution.
- **NetMediate.Adapters**: contracts, a standard envelope, and a pipeline behavior for forwarding notifications to external queues or streams (RabbitMQ, Kafka, Azure Service Bus, etc.).
- **NetMediate.SourceGeneration**: generates `AddNetMediate()` to register handlers at compile-time — no reflection, fully AOT-safe.
- **NetMediate.DataDog.OpenTelemetry**: wires NetMediate traces/metrics to DataDog through OpenTelemetry OTLP exporters.
- **NetMediate.DataDog.Serilog**: attaches the DataDog Serilog sink and enriches logs with NetMediate activity fields.
- **NetMediate.DataDog.ILogger**: `ILogger` scope helpers with DataDog-compatible fields and NetMediate correlation values.

## Companion Guides

- [NetMediate.Moq recipes](docs/NETMEDIATE_MOQ_RECIPES.md)
- [API/Worker/Minimal API samples](docs/SAMPLES.md)
- [Diagnostics (traces + metrics)](docs/DIAGNOSTICS.md)
- [Resilience package guide](docs/RESILIENCE.md)
- [Benchmark results](docs/BENCHMARKS.md)
- [Quartz persistent notifications](docs/QUARTZ.md)
- [Notification adapters (external queues/streams)](docs/ADAPTERS.md)
- [Source generation guide](docs/SOURCE_GENERATION.md)
- [AOT / NativeAOT and trimming guide](docs/AOT.md)
- [DataDog integrations guide](docs/DATADOG.md)
- [Wiki index](docs/WIKI.md)

## Quick Start

Here's a minimal example to get you started with NetMediate:

```csharp
// 1. Install the packages
// dotnet add package NetMediate
// dotnet add package NetMediate.SourceGeneration  (as analyzer — see Installation)

// 2. Register services — source generator discovers all handlers automatically
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddNetMediate(); // all handlers in your project are registered here

// 3. Define a notification (no marker interface required)
public record UserCreated(string UserId, string Email);

// 4. Create a handler (Handle returns Task)
public class UserCreatedHandler : INotificationHandler<UserCreated>
{
    public Task Handle(UserCreated notification, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"User {notification.UserId} was created!");
        return Task.CompletedTask;
    }
}

// 5. Use the mediator
var host = builder.Build();
await host.StartAsync();
var mediator = host.Services.GetRequiredService<IMediator>();
await mediator.Notify(new UserCreated("123", "user@example.com"));
```

For more detailed examples, see the [Usage Examples](#usage-examples) section below.

## Usage Examples

### Basic Setup

Register NetMediate services using the source generator:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate;

var builder = Host.CreateApplicationBuilder();

// Source generation automatically discovers and registers all handlers at compile time.
// Install NetMediate.SourceGeneration as an analyzer and call:
builder.Services.AddNetMediate();

var host = builder.Build();
var mediator = host.Services.GetRequiredService<IMediator>();
```

### Notifications

`Notify` runs the notification pipeline synchronously and dispatches each registered handler as an individual fire-and-forget task. The handler `Task` objects are started immediately and not awaited by `Notify` itself — the calling code regains control once all handlers are started. If a handler throws, the exception is caught and logged as an error; other handlers continue normally.

#### Define a Notification Message
```csharp
// No marker interface required — any plain class or record works
public record UserRegistered(string UserId, string Email, DateTime RegisteredAt);
```

#### Create Notification Handlers
```csharp
public class EmailNotificationHandler : INotificationHandler<UserRegistered>
{
    private readonly IEmailService _emailService;

    public EmailNotificationHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    // Handle must return Task, not Task
    public async Task Handle(UserRegistered notification, CancellationToken cancellationToken = default)
    {
        await _emailService.SendWelcomeEmailAsync(notification.Email, cancellationToken);
    }
}

public class AuditLogHandler : INotificationHandler<UserRegistered>
{
    private readonly IAuditService _auditService;

    public AuditLogHandler(IAuditService auditService)
    {
        _auditService = auditService;
    }

    public async Task Handle(UserRegistered notification, CancellationToken cancellationToken = default)
    {
        await _auditService.LogEventAsync($"User {notification.UserId} registered", cancellationToken);
    }
}
```

#### Publish Notifications
```csharp
var notification = new UserRegistered("user123", "user@example.com", DateTime.UtcNow);
await mediator.Notify(notification, cancellationToken);
```

Batch notifications in one call:
```csharp
var notifications = new[]
{
    new UserRegistered("user123", "user@example.com", DateTime.UtcNow),
    new UserRegistered("user321", "user2@example.com", DateTime.UtcNow)
};
await mediator.Notify(notifications, cancellationToken);
```

### Commands

Commands are dispatched to **all** registered handlers **sequentially** (one after another in registration order). Use `Send` when you want to trigger a side-effect across multiple consumers with no return value.

#### Define a Command
```csharp
// No marker interface required — any plain class or record works
public record CreateUserCommand(string Email, string FirstName, string LastName);
```

#### Create a Command Handler

Multiple handlers can be registered for the same command type — all run sequentially on each `Send` call.

```csharp
public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    private readonly IUserRepository _userRepository;

    public CreateUserCommandHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    // Handle must return Task
    public async Task Handle(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        var user = new User
        {
            Email = command.Email,
            FirstName = command.FirstName,
            LastName = command.LastName
        };

        await _userRepository.CreateAsync(user, cancellationToken);
    }
}
```

#### Send Commands
```csharp
var command = new CreateUserCommand("user@example.com", "John", "Doe");
await mediator.Send(command);
```

### Requests

Requests are sent to a handler and return a response.

#### Define a Request and Response
```csharp
// No marker interface required
public record GetUserQuery(string UserId);
public record UserDto(string Id, string Email, string FirstName, string LastName);
```

#### Create a Request Handler
```csharp
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    private readonly IUserRepository _userRepository;

    public GetUserQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    // Handle must return Task<TResponse>
    public async Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(query.UserId, cancellationToken);

        return new UserDto(user.Id, user.Email, user.FirstName, user.LastName);
    }
}
```

#### Send Requests
```csharp
var query = new GetUserQuery("user123");
var userDto = await mediator.Request<GetUserQuery, UserDto>(query);
```

### Streams

Streams allow handlers to return multiple responses over time.

#### Define a Stream Request
```csharp
// No marker interface required
public record GetUserActivityQuery(string UserId, DateTime FromDate);
public record ActivityDto(string Id, string Action, DateTime Timestamp);
```

#### Create a Stream Handler
```csharp
public class GetUserActivityQueryHandler : IStreamHandler<GetUserActivityQuery, ActivityDto>
{
    private readonly IActivityRepository _activityRepository;
    
    public GetUserActivityQueryHandler(IActivityRepository activityRepository)
    {
        _activityRepository = activityRepository;
    }
    
    public async IAsyncEnumerable<ActivityDto> Handle(
        GetUserActivityQuery query, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var activity in _activityRepository.GetUserActivityStreamAsync(
            query.UserId, query.FromDate, cancellationToken))
        {
            yield return new ActivityDto(activity.Id, activity.Action, activity.Timestamp);
        }
    }
}
```

#### Process Streams
```csharp
var query = new GetUserActivityQuery("user123", DateTime.UtcNow.AddDays(-30));

await foreach (var activity in mediator.RequestStream<GetUserActivityQuery, ActivityDto>(query))
{
    Console.WriteLine($"{activity.Timestamp}: {activity.Action}");
}
```

### Message type summary

NetMediate messages are plain records or classes — **no marker interfaces are required**. The message type and the handler type are always separate.

| Message kind | Handler interface | Dispatch semantics |
|---|---|---|
| Command | `ICommandHandler<TMessage>` | All registered handlers, sequential in registration order |
| Request | `IRequestHandler<TMessage, TResponse>` | First registered handler only; returns `TResponse` |
| Notification | `INotificationHandler<TMessage>` | All registered handlers, individual fire-and-forget per handler |
| Stream | `IStreamHandler<TMessage, TResponse>` | Single registered handler; yields items asynchronously |

```csharp
// Command — no return value, dispatched to all registered handlers sequentially
public record DeleteUserCommand(string UserId);

// Request — single handler, returns a response
public record GetUserQuery(string UserId);

// Notification — dispatched to all registered handlers (fire-and-forget; errors logged)
public record UserDeleted(string UserId);

// Stream — single handler; yields results asynchronously
public record GetRecentEventsQuery(int MaxItems);
```


### Pipeline Behaviors / Interceptors

Behaviors wrap the handler pipeline and run in registration order. Register them via the builder using closed types — this is the only supported pattern, and it is fully AOT-safe:

```csharp
builder.Services.UseNetMediate(configure =>
{
    configure.RegisterBehavior<AuditCommandBehavior, CreateUserCommand, Task>();
    configure.RegisterBehavior<AuditRequestBehavior<GetUserQuery, UserDto>, GetUserQuery, Task<UserDto>>();
    configure.RegisterBehavior<LogNotificationBehavior<UserCreatedNotification>, UserCreatedNotification, Task>();
});
```

Example behavior — audit timing for requests:

```csharp
public sealed class AuditRequestBehavior<TMessage, TResponse>
    : IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{
    // Handle returns Task<TResponse>; next delegate accepts (message, cancellationToken)
    public async Task<TResponse> Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var response = await next(message, cancellationToken);
        Console.WriteLine($"{typeof(TMessage).Name} handled in {DateTimeOffset.UtcNow - startedAt}");
        return response;
    }
}
```

Example notification behavior:

```csharp
public sealed class LogNotificationBehavior<TMessage>
    : IPipelineBehavior<TMessage>
    where TMessage : notnull
{
    public async Task Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching {typeof(TMessage).Name}");
        await next(message, cancellationToken);
        Console.WriteLine($"Dispatched {typeof(TMessage).Name}");
    }
}
```

> **Note on validation**: NetMediate does not include a built-in validation layer. Implement validation as a pipeline behavior. See [docs/VALIDATION_BEHAVIOR_SAMPLE.md](docs/VALIDATION_BEHAVIOR_SAMPLE.md) for an example.

## Framework Support

### Supported package TFMs

All runtime packages are published with:

- `net10.0`
- `netstandard2.0`
- `netstandard2.1`

`NetMediate.SourceGeneration` is an analyzer-only package (`netstandard2.0`) and works from all supported host TFMs.

### Application types covered

Because packages expose `netstandard2.0` and `netstandard2.1` assets they can be consumed by desktop, CLI, mobile, MAUI, and server/web applications.

## Contributing

Contributions are welcome! Please read our [Contributing Guidelines](CONTRIBUTING.md) and [Code of Conduct](CODE_OF_CONDUCT.md).

## Emergency Publishing

For critical situations requiring immediate package publishing, see the [Emergency Publishing Guide](docs/EMERGENCY_PUBLISHING.md).

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
