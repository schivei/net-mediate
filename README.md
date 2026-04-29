# NetMediate

[![CI/CD Pipeline](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml)
[![NuGet](https://img.shields.io/nuget/v/NetMediate?style=flat)](https://www.nuget.org/packages/NetMediate/)

A feature-rich, high-performance .NET Mediator pattern library designed for clean architecture, extensive observability, and seamless migration from MediatR. NetMediate ships ready-to-use built-in validation, OpenTelemetry tracing, resilience behaviors, and optional source-generated zero-reflection dispatch — all in one cohesive ecosystem.

## Table of Contents

- [Why NetMediate?](#why-netmediate)
- [Feature Highlights](#feature-highlights)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Usage Examples](#usage-examples)
  - [Notifications](#notifications)
  - [Notification Providers](#notification-providers)
  - [Commands](#commands)
  - [Requests](#requests)
  - [Streams](#streams)
  - [Validations](#validations)
  - [Simplified Messages](#simplified-messages)
  - [Advanced Configuration](#advanced-configuration)
- [Package Ecosystem](#package-ecosystem)
- [Framework Support](#framework-support)
- [Companion Guides](#companion-guides)
- [Contributing](#contributing)
- [License](#license)

## Why NetMediate?

NetMediate was built to address real production needs that existing mediator libraries leave to user-land or third-party packages:

- **MediatR** ([source](https://github.com/jbogard/MediatR)) is the most widely adopted mediator library in .NET, with excellent community support and a simple API.  It is the right choice when you want a small, well-known dependency with a large ecosystem of blog posts and packages.  NetMediate is not trying to replace MediatR — it extends the idea with built-in validation, telemetry, keyed services, netstandard2.0 support, and an optional source-generated startup path.

- **martinothamar/Mediator** ([source](https://github.com/martinothamar/Mediator)) is the throughput champion: its always-on Roslyn source generator produces zero-allocation switch-based dispatch that is 40–50× faster than any DI-resolved mediator.  If raw throughput is your primary requirement and you are on .NET 8+, Mediator is an outstanding choice.  NetMediate offers an optional `NetMediate.SourceGeneration` package that approaches this performance while remaining DI-friendly and adding built-in cross-cutting concerns.

- **Wolverine** ([source](https://github.com/JasperFx/wolverine)) goes well beyond in-process messaging to provide a full message-bus and saga framework with optional durability.  It is the right choice for distributed systems, long-running workflows, and transactional outbox patterns.

- **TurboMediator** ([source](https://github.com/marcocestari/TurboMediator)) is an ambitious source-generated library with 20+ optional modules (scheduling, feature flags, persistence, distributed locking, and more).  It focuses on the .NET 8/9 ecosystem.

**NetMediate's sweet spot** is the large set of production applications that need *more* than a basic mediator dispatch — built-in validation, telemetry, resilience, keyed services, MediatR migration, netstandard2.0 targeting — without pulling in a full message-bus framework.

---

## Feature Highlights

| Feature | Details |
|---------|---------|
| **Commands** | Explicit `ICommandHandler<TMessage>` with dedicated pipeline |
| **Requests** | `IRequestHandler<TMessage, TResponse>` with typed pipeline |
| **Notifications** | Fan-out to all `INotificationHandler<T>` with pluggable dispatch strategy |
| **Streaming** | `IAsyncEnumerable<T>` via `IStreamHandler<TMessage, TResponse>` |
| **Built-in validation** | `IValidatable` (self-validation) + `IValidationHandler<T>` (external) |
| **FluentValidation bridge** | `NetMediate.FluentValidation` adapts `IValidator<T>` into the validation pipeline |
| **Per-kind pipeline behaviors** | `ICommandBehavior<T>`, `IRequestBehavior<T,R>`, `INotificationBehavior<T>`, `IStreamBehavior<T,R>` |
| **OpenTelemetry built-in** | `ActivitySource` + `Meter` for every dispatch; zero extra packages required |
| **Resilience** | Optional retry, timeout, circuit-breaker via `NetMediate.Resilience` (Polly v8) |
| **Source generation** | `NetMediate.SourceGeneration` moves handler discovery to compile-time; AOT-safe |
| **Keyed DI services** | `[KeyedMessage("key")]` routes messages to different handler instances |
| **Notification providers** | Pluggable: inline (default), channel/background (`NetMediate.InternalNotifier`), custom (`NetMediate.Notifications`) |
| **MediatR migration shim** | `NetMediate.Compat` re-exports MediatR contracts so existing code compiles unchanged |
| **Singleton handlers** | Handlers registered as `Singleton` by default for maximum dispatch performance |
| **Broad target framework** | `net10.0` · `netstandard2.0` · `netstandard2.1` |
| **DataDog integrations** | `NetMediate.DataDog.OpenTelemetry`, `.Serilog`, `.ILogger` |

---

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

## Package Ecosystem

```xml
<!-- Core + optional modules -->
<PackageReference Include="NetMediate" Version="x.x.x" />

<!-- MediatR migration compatibility layer -->
<PackageReference Include="NetMediate.Compat" Version="x.x.x" />

<!-- Moq helpers for unit tests -->
<PackageReference Include="NetMediate.Moq" Version="x.x.x" />

<!-- Retry / timeout / circuit-breaker pipeline behaviors (Polly v8) -->
<PackageReference Include="NetMediate.Resilience" Version="x.x.x" />

<!-- FluentValidation bridge (net8+) -->
<PackageReference Include="NetMediate.FluentValidation" Version="x.x.x" />

<!-- Compile-time handler registration (source generator / AOT-safe) -->
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />

<!-- Channel-based fire-and-forget notification dispatch (background worker) -->
<PackageReference Include="NetMediate.InternalNotifier" Version="x.x.x" />

<!-- Inline synchronous notification dispatch for unit tests (no delays) -->
<PackageReference Include="NetMediate.InternalNotifier.Test" Version="x.x.x" />

<!-- Base class for custom notification providers -->
<PackageReference Include="NetMediate.Notifications" Version="x.x.x" />

<!-- DataDog telemetry / Serilog / ILogger integrations -->
<PackageReference Include="NetMediate.DataDog.OpenTelemetry" Version="x.x.x" />
<PackageReference Include="NetMediate.DataDog.Serilog" Version="x.x.x" />
<PackageReference Include="NetMediate.DataDog.ILogger" Version="x.x.x" />
```

| Package | Purpose |
|---------|---------|
| `NetMediate` | Core mediator: commands, requests, notifications, streams, validation, telemetry |
| `NetMediate.Compat` | Re-exports MediatR contracts so existing code compiles and runs without changes |
| `NetMediate.Moq` | `Mocking.Create`, `AddMockSingleton`, and async setup helpers for concise test setup |
| `NetMediate.Resilience` | Retry, timeout, and circuit-breaker pipeline behaviors via Polly v8 |
| `NetMediate.FluentValidation` | Adapts `IValidator<T>` into the NetMediate validation pipeline (requires net8+) |
| `NetMediate.SourceGeneration` | Generates `AddNetMediateGenerated()` at compile time — zero startup reflection, AOT-safe |
| `NetMediate.InternalNotifier` | Channel + `BackgroundService` worker for fire-and-forget notification dispatch |
| `NetMediate.InternalNotifier.Test` | Inline synchronous notification dispatch for unit tests (no `Task.Delay` needed) |
| `NetMediate.Notifications` | `NotificationProviderBase` abstract class for building custom notification providers |
| `NetMediate.DataDog.OpenTelemetry` | Wires NetMediate traces/metrics to DataDog via OTLP exporters |
| `NetMediate.DataDog.Serilog` | DataDog Serilog sink configuration with NetMediate observability enrichers |
| `NetMediate.DataDog.ILogger` | ILogger scope helpers with DataDog-compatible fields and NetMediate correlation values |

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
var notifications = new[]
{
    new UserRegistered("user123", "user@example.com", DateTime.UtcNow),
    new UserRegistered("user321", "user2@example.com", DateTime.UtcNow)
};
await mediator.Notify(notifications, cancellationToken);
```

### Notification Providers

The built-in default dispatches notifications **inline** (same call stack).  Choose an
alternative provider to change the dispatch strategy without touching handler code.

#### Channel-based fire-and-forget (background worker)

Install `NetMediate.InternalNotifier` and call `AddNetMediateInternalNotifier()`:

```csharp
using NetMediate.InternalNotifier;

builder.Services
    .AddNetMediate(typeof(MyHandler).Assembly)
    .UseNotificationProvider<ChannelNotificationProvider>(); // declared below

builder.Services.AddNetMediateInternalNotifier(); // registers ChannelNotificationProvider + BackgroundNotificationWorker
```

Notifications are written to an unbounded `Channel<T>` and consumed by a dedicated
`BackgroundService` worker — the caller returns immediately without waiting for handlers
to finish.

#### Inline synchronous provider for unit tests

Install `NetMediate.InternalNotifier.Test` to remove the need for `Task.Delay` in tests:

```csharp
using NetMediate.InternalNotifier.Test;

services
    .AddNetMediate(typeof(MyHandler).Assembly)
    .AddNetMediateTestNotifier(); // registers TestNotificationProvider (inline + synchronous)
```

#### Custom notification provider

Install `NetMediate.Notifications` and derive from `NotificationProviderBase<T>`:

```csharp
using NetMediate.Notifications;

public class MyQueueNotificationProvider : NotificationProviderBase<MyQueueNotificationProvider>
{
    protected override Task DispatchAsync(
        INotificationDispatcher dispatcher,
        INotificationPacket packet,
        CancellationToken cancellationToken)
    {
        // enqueue to your own queue / bus and dispatch later
        return Task.CompletedTask;
    }
}

// Registration
builder.Services
    .AddNetMediate()
    .UseCustomNotificationProvider<MyQueueNotificationProvider>();
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

`NetMediate.SourceGeneration` is an analyzer package (`netstandard2.0`) and works from all supported host TFMs.

### Application types covered

Because packages expose `netstandard2.0` and `netstandard2.1` assets, they can be consumed by:

- desktop applications (WPF, WinForms, Avalonia)
- CLI applications
- mobile applications
- MAUI applications
- server/web applications (ASP.NET Core, Worker Service, gRPC)

Always validate your specific app stack (DI host model, platform runtime, and trimming/AOT profile) in your CI pipeline.

### Benchmark note by target

Performance scenarios are measured from runnable host runtimes.  Current benchmark executions are reported for `net10.0`.
For `netstandard2.0`/`netstandard2.1`, throughput is determined by the concrete runtime hosting those assets.

<!-- PERF_START -->
## Performance

> Last benchmarked: **2026-04-29 00:05 UTC** on `.NETCoreApp,Version=v10.0` (sequential, no-op handlers, Warning log, telemetry+validation disabled).
> Full details in [docs/BENCHMARK_COMPARISON.md](docs/BENCHMARK_COMPARISON.md).

> **How to read these numbers:** NetMediate deliberately runs with a dedicated DI scope per
> dispatch (isolation guarantee) and includes built-in validation + telemetry hooks even when
> disabled for the benchmark.  Source-generated libraries (martinothamar/Mediator, TurboMediator)
> skip DI resolution entirely via switch-generated dispatch, which explains their much higher
> raw throughput.  Enabling `NetMediate.SourceGeneration` narrows the gap significantly.

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

> NetMediate benchmarks run with telemetry and validation disabled for a fair baseline.
> TurboMediator *(net8.0)* — source generator incompatible with net10.0 (v0.9.3).

<!-- PERF_END -->

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
