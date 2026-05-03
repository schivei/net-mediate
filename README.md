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
builder.Services.AddNetMediate();

// 3. Define a notification (must implement INotification)
public record UserCreated(string UserId, string Email) : INotification;

// 4. Create a handler (Handle returns Task, not Task)
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

// Register NetMediate and scan all loaded assemblies for handlers
builder.Services.AddNetMediate();

// Or scan specific assemblies
builder.Services.AddNetMediate(typeof(MyHandler).Assembly);

var host = builder.Build();
var mediator = host.Services.GetRequiredService<IMediator>();
```

### Notifications

Notifications are written to an in-memory channel and dispatched by a background worker. All registered handlers for the same message type are called **sequentially** in registration order. `Notify` returns as soon as the message is enqueued — handler execution happens asynchronously. Exceptions thrown by notification handlers are caught by the background worker and logged as warnings.

#### Define a Notification Message
```csharp
// Must implement INotification
public record UserRegistered(string UserId, string Email, DateTime RegisteredAt) : INotification;
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
// Must implement ICommand
public record CreateUserCommand(string Email, string FirstName, string LastName) : ICommand;
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

    // Handle must return Task, not Task
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
// Must implement IRequest<TResponse>
public record GetUserQuery(string UserId) : IRequest<UserDto>;
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

    // Handle must return Task<TResponse>, not Task<TResponse>
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
// Must implement IStream<TResponse>
public record GetUserActivityQuery(string UserId, DateTime FromDate) : IStream<ActivityDto>;
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

NetMediate supports message validation through multiple approaches:

#### Self-Validating Messages
```csharp
using System.ComponentModel.DataAnnotations;

// Self-validating command: implements both ICommand and IValidatable
public record CreateUserCommand(string Email, string FirstName, string LastName) : ICommand, IValidatable
{
    public Task<ValidationResult> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
            return Task.FromResult(new ValidationResult("Email is required", new[] { nameof(Email) }));

        if (!Email.Contains('@'))
            return Task.FromResult(new ValidationResult("Invalid email format", new[] { nameof(Email) }));

        if (string.IsNullOrWhiteSpace(FirstName))
            return Task.FromResult(new ValidationResult("First name is required", new[] { nameof(FirstName) }));

        return Task.FromResult(ValidationResult.Success!);
    }
}
```

#### External Validation Handlers
```csharp
public class CreateUserCommandValidator : IValidationHandler<CreateUserCommand>
{
    private readonly IUserRepository _userRepository;
    
    public CreateUserCommandValidator(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }
    
    public async Task<ValidationResult> ValidateAsync(
        CreateUserCommand message, 
        CancellationToken cancellationToken = default)
    {
        var existingUser = await _userRepository.GetByEmailAsync(message.Email, cancellationToken);
        
        if (existingUser != null)
            return new ValidationResult("Email already exists", new[] { nameof(message.Email) });
            
        return ValidationResult.Success!;
    }
}
```

#### Validation Exceptions
When validation fails, NetMediate throws a `MessageValidationException`:

```csharp
try
{
    var command = new CreateUserCommand("", "John", "Doe"); // Invalid email
    await mediator.Send(command);
}
catch (MessageValidationException ex)
{
    Console.WriteLine($"Validation failed: {ex.Message}");
}
```

### Message type summary

NetMediate messages are plain records or classes that implement one of the four marker interfaces. There are no generic self-handler interfaces — the message type and the handler type are always separate.

| Message kind | Marker interface | Handler interface | Dispatch semantics |
|---|---|---|---|
| Command | `ICommand` | `ICommandHandler<TMessage>` | All registered handlers, in parallel (`Task.WhenAll`) |
| Request | `IRequest<TResponse>` | `IRequestHandler<TMessage, TResponse>` | First registered handler only; returns `TResponse` |
| Notification | `INotification` | `INotificationHandler<TMessage>` | All registered handlers, sequentially, via background worker |
| Stream | `IStream<TResponse>` | `IStreamHandler<TMessage, TResponse>` | All registered handlers iterated; each yields items |

```csharp
// Command — no return value, dispatched to all registered handlers in parallel
public record DeleteUserCommand(string UserId) : ICommand;

// Request — single handler, returns a response
public record GetUserQuery(string UserId) : IRequest<UserDto>;

// Notification — dispatched to all registered handlers sequentially (via background worker)
public record UserDeleted(string UserId) : INotification;

// Stream — all registered handlers are iterated; each yields results asynchronously
public record GetRecentEventsQuery(int MaxItems) : IStream<EventDto>;
```


### Advanced Configuration

#### Ignoring unhandled messages

By default, NetMediate throws `InvalidOperationException` when no handler is registered for a message. To suppress this:

```csharp
builder.Services.AddNetMediate(typeof(MyHandler).Assembly)
    .IgnoreUnhandledMessages(ignore: true);
```

#### Pipeline Behaviors / Interceptors

Behaviors wrap the handler pipeline. Register them via DI using the appropriate behavior interface:

```csharp
// Open-generic: runs for every request type
builder.Services.AddSingleton(typeof(IRequestBehavior<,>), typeof(AuditRequestBehavior<,>));

// Closed-generic: runs only for a specific message type
builder.Services.AddSingleton<ICommandBehavior<CreateUserCommand>, ValidationCommandBehavior>();
```

Example behavior — audit timing for requests:

```csharp
public sealed class AuditRequestBehavior<TMessage, TResponse>
    : IRequestBehavior<TMessage, TResponse>
    where TMessage : notnull, IRequest<TResponse>
{
    // Handle returns Task<TResponse>; next delegate accepts (message, cancellationToken)
    public async Task<TResponse> Handle(
        TMessage message,
        RequestHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken = default)
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
    : INotificationBehavior<TMessage>
    where TMessage : notnull, INotification
{
    public async Task Handle(
        TMessage message,
        NotificationHandlerDelegate<TMessage> next,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Dispatching {typeof(TMessage).Name}");
        await next(message, cancellationToken);
        Console.WriteLine($"Dispatched {typeof(TMessage).Name}");
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
