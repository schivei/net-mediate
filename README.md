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
  - [Validations](#validations)
  - [Simplified Messages](#simplified-messages)
  - [Advanced Configuration](#advanced-configuration)
- [Framework Support](#framework-support)
- [Companion Guides](#companion-guides)
- [Contributing](#contributing)
- [License](#license)
- [Fixed problems](#fixed-problems)

## Introduction

NetMediate is a mediator pattern library for .NET that enables decoupled communication between components in your application. It provides a simple and flexible way to send commands, publish notifications, make requests, and handle streaming responses while maintaining clean architecture principles.

### Key Features

- **Commands**: Send one-way messages to all registered handlers simultaneously
- **Notifications**: Publish messages to multiple handlers
- **Requests**: Send messages and receive responses
- **Streaming**: Handle requests that return multiple responses over time
- **Validation**: Built-in message validation support with custom validators
- **Pipeline Behaviors**: Interceptors with pre/post flow for Send/Request/Notify/Stream
- **Optional resilience package**: Retry, timeout, and circuit-breaker behaviors in `NetMediate.Resilience`
- **OpenTelemetry-ready diagnostics**: Built-in `ActivitySource`/`Meter` for Send/Request/Notify/Stream
- **Optional DataDog integrations**: OpenTelemetry, Serilog, and ILogger support packages
- **Dependency Injection**: Seamless integration with Microsoft.Extensions.DependencyInjection
- **Keyed Services**: Support for keyed service registration and resolution
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

- **NetMediate.Moq**: adds lightweight Moq helpers for cleaner unit and integration tests (`Mocking.Create`, `AddMockSingleton`, and async setup extensions).
- **NetMediate.Resilience**: adds optional retry, timeout, and circuit-breaker pipeline behaviors for request and notification flows.
- **NetMediate.Quartz**: persists notifications as Quartz.NET jobs, enabling crash recovery and cluster-distributed notification execution.
- **NetMediate.Adapters**: provides contracts, a standard envelope, and a pipeline behavior for forwarding notifications to external queues or streams (RabbitMQ, Kafka, Azure Service Bus, etc.).
- **NetMediate.SourceGeneration**: generates `AddNetMediateGenerated(...)` to register handlers at compile-time and reduce reflection cost at startup.
- **NetMediate.DataDog.OpenTelemetry**: wires NetMediate traces/metrics to DataDog through OpenTelemetry OTLP exporters.
- **NetMediate.DataDog.Serilog**: adds DataDog Serilog sink configuration and NetMediate observability enrichers.
- **NetMediate.DataDog.ILogger**: adds ILogger scope helpers with DataDog-compatible fields and NetMediate correlation values.

## Companion Guides

- [NetMediate.Moq recipes](docs/NETMEDIATE_MOQ_RECIPES.md)
- [API/Worker/Minimal API samples](docs/SAMPLES.md)
- [Diagnostics (structured logs + metrics)](docs/DIAGNOSTICS.md)
- [Resilience package guide and load capacity](docs/RESILIENCE.md)
- [Benchmark results (all scenarios)](docs/BENCHMARKS.md)
- [Quartz persistent notifications](docs/QUARTZ.md)
- [Notification adapters (external queues/streams)](docs/ADAPTERS.md)
- [Source generation guide](docs/SOURCE_GENERATION.md)
- [AOT / NativeAOT and trimming guide](docs/AOT.md)
- [DataDog integrations guide](docs/DATADOG.md)
- [Wiki index](docs/WIKI.md)

## Quick Start

Here's a minimal example to get you started with NetMediate:

```csharp
// 1. Install the package
// dotnet add package NetMediate

// 2. Register services
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterHandler<INotificationHandler<UserCreated>, UserCreatedHandler, UserCreated, Task>();
});

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

First, register NetMediate services in your dependency injection container:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate;

var builder = Host.CreateApplicationBuilder();

// Register NetMediate and explicitly register all handlers
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterHandler<INotificationHandler<MyNotification>, MyNotificationHandler, MyNotification, Task>();
    configure.RegisterHandler<ICommandHandler<MyCommand>, MyCommandHandler, MyCommand, Task>();
    configure.RegisterHandler<IRequestHandler<MyRequest, MyResponse>, MyRequestHandler, MyRequest, Task<MyResponse>>();
});

// Or use the source generator to auto-generate registrations (recommended for AOT)
// Install NetMediate.SourceGeneration and use: builder.Services.AddNetMediateGenerated();

var host = builder.Build();
var mediator = host.Services.GetRequiredService<IMediator>();
```

### Notifications

Notifications are written to an in-memory channel and dispatched by a background worker. All registered handlers for the same message type are called **sequentially** in registration order. `Notify` returns as soon as the message is enqueued — handler execution happens asynchronously. Exceptions thrown by notification handlers are caught by the background worker and logged as warnings.

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

Commands are dispatched to **all** registered handlers in parallel (`Task.WhenAll`). Use `Send` when you want to trigger a side-effect across multiple consumers with no return value.

#### Define a Command
```csharp
// No marker interface required — any plain class or record works
public record CreateUserCommand(string Email, string FirstName, string LastName);
```

#### Create a Command Handler

Multiple handlers can be registered for the same command type — all run in parallel on each `Send` call.

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

### Validations

NetMediate previously supported message validation through `IValidatable` and `IValidationHandler<T>`. These interfaces have been removed in the current version. Validation should now be handled using pipeline behaviors.

#### Validation via Pipeline Behavior
```csharp
public sealed class ValidationBehavior<TMessage, TResult>(IValidator<TMessage> validator)
    : IPipelineBehavior<TMessage, TResult>
    where TMessage : notnull
    where TResult : notnull
{
    public async TResult Handle(TMessage message, PipelineBehaviorDelegate<TMessage, TResult> next, CancellationToken cancellationToken)
    {
        var result = validator.Validate(message);
        if (!result.IsValid)
            throw new ValidationException(result.Errors);
        return await next(message, cancellationToken);
    }
}
```

### Message type summary

NetMediate messages are plain records or classes — **no marker interfaces are required**. The message type and the handler type are always separate.

| Message kind | Handler interface | Dispatch semantics |
|---|---|---|
| Command | `ICommandHandler<TMessage>` | All registered handlers, in parallel (`Task.WhenAll`) |
| Request | `IRequestHandler<TMessage, TResponse>` | First registered handler only; returns `TResponse` |
| Notification | `INotificationHandler<TMessage>` | All registered handlers, fire-and-forget background dispatch |
| Stream | `IStreamHandler<TMessage, TResponse>` | All registered handlers iterated; each yields items |

```csharp
// Command — no return value, dispatched to all registered handlers in parallel
public record DeleteUserCommand(string UserId);

// Request — single handler, returns a response
public record GetUserQuery(string UserId);

// Notification — dispatched to all registered handlers (fire-and-forget)
public record UserDeleted(string UserId);

// Stream — all registered handlers are iterated; each yields results asynchronously
public record GetRecentEventsQuery(int MaxItems);
```


### Advanced Configuration

#### Ignoring unhandled messages

By default, NetMediate throws `InvalidOperationException` when no handler is registered for a message. To suppress this:

```csharp
builder.Services.AddNetMediate(configure =>
{
    // ... register your handlers ...
})
    .IgnoreUnhandledMessages(ignore: true);
```

#### Pipeline Behaviors / Interceptors

Behaviors wrap the handler pipeline. Register them via `IMediatorServiceBuilder.RegisterBehavior<>()` or directly in the DI container using `IPipelineBehavior<TMessage, TResult>`:

```csharp
// Open-generic: runs for every request type (register via DI)
builder.Services.AddSingleton(typeof(IPipelineRequestBehavior<,>), typeof(AuditRequestBehavior<,>));

// Notification-specific: runs for every notification pipeline
builder.Services.AddSingleton(typeof(IPipelineBehavior<>), typeof(LogNotificationBehavior<>));

// Closed-generic via builder: runs only for a specific message type
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterBehavior<ValidationCommandBehavior, CreateUserCommand, Task>();
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
}
```

## Framework Support

### Supported package TFMs

All runtime packages (`NetMediate`, `NetMediate.Moq`, `NetMediate.Resilience`, `NetMediate.Quartz`, `NetMediate.Adapters`) are published with:

- `net10.0`
- `netstandard2.0`
- `netstandard2.1`

`NetMediate.SourceGeneration` remains an analyzer package (`netstandard2.0`) and works from all supported host TFMs.

### Application types covered

Because packages expose `netstandard2.0` and `netstandard2.1` assets, they can be consumed by:

- desktop applications
- CLI applications
- mobile applications
- MAUI applications
- server/web applications

Always validate your specific app stack (DI host model, platform runtime, and trimming/AOT profile) in your CI pipeline.

### Benchmark note by target

Performance scenarios are measured from runnable host runtimes. Current benchmark executions are reported for `net10.0`.
For `netstandard2.0`/`netstandard2.1`, throughput is determined by the concrete runtime hosting those assets (desktop/CLI/mobile/MAUI).

## Contributing

Contributions are welcome! We appreciate your interest in making NetMediate better.

Please read our [Contributing Guidelines](CONTRIBUTING.md) for detailed information about:
- Development setup and prerequisites
- Code style and formatting requirements
- Testing guidelines and coverage requirements
- Pull request process and expectations

We also ask that all contributors follow our [Code of Conduct](CODE_OF_CONDUCT.md) to ensure a welcoming and inclusive environment for everyone.

For major changes, please open an issue first to discuss what you would like to change.

## Emergency Publishing

For critical situations requiring immediate package publishing, see the [Emergency Publishing Guide](docs/EMERGENCY_PUBLISHING.md). This functionality is restricted to the repository owner and bypasses normal change detection mechanisms.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Fixed problems

- Notification exceptions no longer stop execution: exceptions thrown by handlers inside the background notification worker are caught and logged as warnings, so other notifications continue to be dispatched.
- Added batch publishing support for notifications.
- Improved consistency across handler interfaces via the IHandler base.
- Refactored internals for clearer, more maintainable code.
