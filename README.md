# NetMediate

[![CI/CD Pipeline](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml)
[![NuGet](https://img.shields.io/nuget/v/NetMediate?style=flat)](https://www.nuget.org/packages/NetMediate/)

A lightweight and efficient .NET implementation of the Mediator pattern, providing a clean alternative to MediatR for in-process messaging and communication between components.

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

- **Commands**: Send one-way messages to single handlers
- **Notifications**: Publish messages to multiple handlers simultaneously
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
<PackageReference Include="NetMediate.Compat" Version="x.x.x" />
<PackageReference Include="NetMediate.Moq" Version="x.x.x" />
<PackageReference Include="NetMediate.Resilience" Version="x.x.x" />
<PackageReference Include="NetMediate.FluentValidation" Version="x.x.x" />
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<PackageReference Include="NetMediate.DataDog.OpenTelemetry" Version="x.x.x" />
<PackageReference Include="NetMediate.DataDog.Serilog" Version="x.x.x" />
<PackageReference Include="NetMediate.DataDog.ILogger" Version="x.x.x" />
```

- **NetMediate.Compat**: keeps MediatR contracts (`MediatR.IMediator`, `IRequest`, `INotification`, handlers, and `AddMediatR`) so migration to NetMediate can be done without rewriting contracts.
- **NetMediate.Moq**: adds lightweight Moq helpers for cleaner unit and integration tests (`Mocking.Create`, `AddMockSingleton`, and async setup extensions).
- **NetMediate.Resilience**: adds optional retry, timeout, and circuit-breaker pipeline behaviors for request and notification flows.
- **NetMediate.FluentValidation**: bridges FluentValidation `IValidator<T>` into the NetMediate validation pipeline without mandatory coupling (requires net8+).
- **NetMediate.SourceGeneration**: generates `AddNetMediateGenerated(...)` to register handlers at compile-time and reduce reflection cost at startup.
- **NetMediate.DataDog.OpenTelemetry**: wires NetMediate traces/metrics to DataDog through OpenTelemetry OTLP exporters.
- **NetMediate.DataDog.Serilog**: adds DataDog Serilog sink configuration and NetMediate observability enrichers.
- **NetMediate.DataDog.ILogger**: adds ILogger scope helpers with DataDog-compatible fields and NetMediate correlation values.

## Companion Guides

- [MediatR migration guide](docs/MEDIATR_MIGRATION_GUIDE.md)
- [NetMediate.Moq recipes](docs/NETMEDIATE_MOQ_RECIPES.md)
- [API/Worker/Minimal API samples](docs/SAMPLES.md)
- [Diagnostics (structured logs + metrics)](docs/DIAGNOSTICS.md)
- [Resilience package guide and load capacity](docs/RESILIENCE.md)
- [Source generation guide](docs/SOURCE_GENERATION.md)
- [DataDog integrations guide](docs/DATADOG.md)
- [Library comparison (NetMediate vs MediatR vs others)](docs/LIBRARY_COMPARISON.md)
- [Benchmark comparison](docs/BENCHMARK_COMPARISON.md)
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

// 3. Define a notification
public record UserCreated(string UserId, string Email);

// 4. Create a handler
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

// Or scan assemblies containing a specific type
builder.Services.AddNetMediate<MyHandler>();

var host = builder.Build();
var mediator = host.Services.GetRequiredService<IMediator>();
```

### Notifications

Notifications are published to all registered handlers simultaneously.

#### Define a Notification Message
```csharp
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
var notifications = [
    new UserRegistered("user123", "user@example.com", DateTime.UtcNow),
	new UserRegistered("user321", "user2@example.com", DateTime.UtcNow)
];
await mediator.Notify(notifications, cancellationToken);
```

Error handling
```csharp
await mediator.Notify(
    notification,
    (handlerType, message, exception) =>
    {
        logger.LogError(exception, "Publish failed for {MessageType} at {HandlerType}", message?.GetType().Name, handlerType?.Name);
    },
    cancellationToken);
	
await mediator.Notify(
    notifications,
    (handlerType, message, exception) =>
    {
        logger.LogError(exception, "Publish failed for {MessageType} at {HandlerType}", message?.GetType().Name, handlerType?.Name);
    },
    cancellationToken);
```

### Commands

Commands are sent to a single handler for processing.

#### Define a Command
```csharp
public record CreateUserCommand(string Email, string FirstName, string LastName);
```

#### Create a Command Handler
```csharp
public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    private readonly IUserRepository _userRepository;
    
    public CreateUserCommandHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }
    
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

NetMediate supports message validation through multiple approaches:

#### Self-Validating Messages
```csharp
using System.ComponentModel.DataAnnotations;

public record CreateUserCommand(string Email, string FirstName, string LastName) : IValidatable
{
    public Task<ValidationResult> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
            return Task.FromResult(new ValidationResult("Email is required", new[] { nameof(Email) }));
            
        if (!Email.Contains("@"))
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
    
    public async ValueTask<ValidationResult> ValidateAsync(
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

### Simplified Messages
NetMediate supports simplified message definitions without explicit handler interfaces.

#### Simplified Command
```csharp
public record SimpleCommand(string Data) : ICommand<SimpleCommand>;
```

And its invocation:
```csharp
await mediator.Send(new SimpleCommand("Some data"), cancellationToken);
```

#### Simplified Request
```csharp
public record SimpleRequest(string Query) : IRequest<SimpleRequest, string>;
```

And its invocation:
```csharp
var response = await mediator.Request(new SimpleRequest("Get info"), cancellationToken);
```

#### Simplified Notification
```csharp
public record SimpleNotification(string Message) : INotification<SimpleNotification>;
```

And its invocation:
```csharp
await mediator.Notify(new SimpleNotification("Hello all"), cancellationToken);
```

#### Simplified Stream
```csharp
public record SimpleStreamRequest(int Count) : IStream<SimpleStreamRequest, int>;
```

And its invocation:
```csharp
await foreach (var number in mediator.RequestStream(new SimpleStreamRequest(5), cancellationToken))
{
    Console.WriteLine(number);
}
```


### Advanced Configuration

#### Keyed Services
```csharp
[KeyedMessage("primary")]
public record PrimaryUserCommand(string Email);

[KeyedMessage("secondary")]
public record SecondaryUserCommand(string Email);

public class PrimaryUserHandler : ICommandHandler<PrimaryUserCommand>
{
    public Task Handle(PrimaryUserCommand command, CancellationToken cancellationToken = default)
    {
        // Handle primary user logic
        return Task.CompletedTask;
    }
}

public class SecondaryUserHandler : ICommandHandler<SecondaryUserCommand>
{
    public Task Handle(SecondaryUserCommand command, CancellationToken cancellationToken = default)
    {
        // Handle secondary user logic
        return Task.CompletedTask;
    }
}
```

#### Custom Configuration
```csharp
builder.Services.AddNetMediate()
    .IgnoreUnhandledMessages(ignore: true, log: true, logLevel: LogLevel.Warning)
    .FilterCommand<CreateUserCommand, CreateUserCommandHandler>(cmd => cmd.Email.EndsWith("@company.com"))
    .InstantiateHandlerByMessageFilter<DynamicMessage>(msg => 
        msg.Type == "urgent" ? typeof(UrgentMessageHandler) : typeof(StandardMessageHandler))
    .RegisterNotificationHandler<DummyNotificationMessage, DummyNotificationHandler>();
```

#### Pipeline Behaviors / Interceptors
```csharp
public class AuditRequestBehavior<TMessage, TResponse> : IRequestBehavior<TMessage, TResponse>
{
    public async Task<TResponse> Handle(
        TMessage message,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var response = await next(cancellationToken);
        Console.WriteLine($"{typeof(TMessage).Name} handled in {DateTimeOffset.UtcNow - startedAt}");
        return response;
    }
}

builder.Services.AddNetMediate();
builder.Services.AddScoped(typeof(IRequestBehavior<,>), typeof(AuditRequestBehavior<,>));
```

## Framework Support

### Supported package TFMs

All runtime packages (`NetMediate`, `NetMediate.Compat`, `NetMediate.Moq`, `NetMediate.Resilience`) are published with:

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

<!-- PERF_START -->
## Performance

> Last benchmarked: **2026-04-29 00:05 UTC** on `.NETCoreApp,Version=v10.0` (sequential, no-op handlers, Warning log, telemetry+validation disabled).
> Full details in [docs/BENCHMARK_COMPARISON.md](docs/BENCHMARK_COMPARISON.md).

### Command ops/s (higher is better)

| Library | No Code Gen · No AOT | Code Gen · No AOT | Code Gen · AOT |
|---------|:--------------------:|:-----------------:|:--------------:|
| NetMediate | 452,842 | 504,204 | ≈ Code Gen |
| MediatR 14 | 2,004,611 | NOT SUPPORTED | NOT SUPPORTED |
| martinothamar/Mediator 3 | NOT SUPPORTED | 23,691,068 | ≈ Code Gen |
| TurboMediator | NOT SUPPORTED | 19,749,185 *(net8.0)* | ≈ Code Gen *(net8.0)* |

### Request ops/s (higher is better)

| Library | No Code Gen · No AOT | Code Gen · No AOT | Code Gen · AOT |
|---------|:--------------------:|:-----------------:|:--------------:|
| NetMediate | 496,433 | 485,769 | ≈ Code Gen |
| MediatR 14 | 2,562,788 | NOT SUPPORTED | NOT SUPPORTED |
| martinothamar/Mediator 3 | NOT SUPPORTED | 20,738,283 | ≈ Code Gen |
| TurboMediator | NOT SUPPORTED | 17,301,038 *(net8.0)* | ≈ Code Gen *(net8.0)* |

> NetMediate benchmarks run with telemetry and validation disabled.
> TurboMediator *(net8.0)* — source generator incompatible with net10.0 (v0.9.3).

<!-- PERF_END -->

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

- Prevented notification exceptions from stopping execution by introducing a dedicated onError callback.
- Added batch publishing support for notifications.
- Improved consistency across handler interfaces via the IHandler base.
- Refactored internals for clearer, more maintainable code.
