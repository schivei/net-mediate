# NetMediate

[![CI/CD Pipeline](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml)
[![NuGet](https://img.shields.io/nuget/v/NetMediate?style=flat)](https://www.nuget.org/packages/NetMediate/)
[![Documentation](https://img.shields.io/badge/docs-website-blue)](https://elton.schivei.nom.br/net-mediate)

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

### What’s new in this version

- ✅ `dotnet add package NetMediate.SourceGeneration` is now the recommended entrypoint for application/startup projects.
- 📦 `NetMediate.Core` now carries the contracts, while `NetMediate.SourceGeneration` injects `NetMediate` and `GenDI.SourceGenerator` through `buildTransitive`.
- ✨ New generated typed dispatch extensions (for commands, notifications, requests, and streams) reduce boilerplate and improve call-site readability.
- 🔁 `buildTransitive` propagation keeps generator behavior consistent in larger multi-project solutions when you intentionally allow transitive flow.

### Why this improves day-to-day engineering

- **Faster onboarding**: fewer setup decisions and less “it works on my machine” friction.
- **Cleaner organization**: generated typed APIs make mediator usage explicit and easier to navigate in large solutions.
- **More predictable architecture**: compile-time registration and transitive analyzer behavior keep projects aligned as teams scale.

### Key Features

- **Commands**: Send one-way messages to all registered handlers sequentially
- **Notifications**: Publish messages to multiple handlers — all handlers started in parallel (`Task.WhenAll`); handler results and exceptions are discarded (fire-and-forget). Batch notifications (`IEnumerable`) are also dispatched in parallel.
- **Requests**: Send a message to a single handler and receive a typed response
- **Streaming**: Handle requests that return multiple responses over time via `IAsyncEnumerable`
- **Pipeline Behaviors**: Interceptors with pre/post flow for every message kind
- **Optional resilience package**: Retry, timeout, and circuit-breaker behaviors in `NetMediate.Resilience`
- **OpenTelemetry-ready diagnostics**: Built-in `ActivitySource`/`Meter` for Send/Request/Notify/Stream
- **Optional DataDog integrations**: OpenTelemetry, Serilog, and ILogger support packages
- **Keyed handler routing**: Register handlers under named keys and dispatch to specific subsets at runtime
- **Streaming fan-out**: Multiple `IStreamHandler` registrations supported — their items are merged sequentially
- **Cancellation Support**: Full cancellation token support across all operations
- **Broad runtime compatibility**: Multi-targeted for `net10.0`, `netstandard2.0`, and `netstandard2.1`

## Installation

### Shared contracts project
```powershell
Install-Package NetMediate.Core
```

### Application / startup project
```powershell
Install-Package NetMediate.SourceGeneration
```

> **Note:** Install `NetMediate.Core` where you only need the contracts (`IMediator`, handlers, behaviors). Install `NetMediate.SourceGeneration` in the executable/startup project that calls `AddNetMediate()`. Its `buildTransitive` file adds the required `PackageReference` entries for `NetMediate` and `GenDI.SourceGenerator`.

### .NET CLI
```bash
dotnet add package NetMediate.Core
dotnet add package NetMediate.SourceGeneration
```

> **Note:** If you are publishing your own library, you may add `PrivateAssets="all"` to the `NetMediate.SourceGeneration` reference to avoid flowing the generator package transitively. The startup project can keep the default behavior.

### PackageReference
```xml
<PackageReference Include="NetMediate.Core" Version="x.x.x" />
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x.x">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

> **Note:** `NetMediate.SourceGeneration` should be referenced with `IncludeAssets` + `PrivateAssets="all"`. It adds `NetMediate` and `GenDI.SourceGenerator` indirectly via `buildTransitive`.

### GenDI-first activation pattern

`NetMediate.SourceGeneration` also activates GenDI in the startup project. Prefer the GenDI style for your application services and supporting implementations:

```csharp
using GenDI;
using Microsoft.Extensions.DependencyInjection;

[ServiceInjection]
public interface IEmailService
{
    Task SendWelcomeEmailAsync(string email, CancellationToken cancellationToken);
}

[Injectable(ServiceLifetime.Scoped, Group = 10, Order = 1, Key = "primary")]
public sealed class SmtpEmailService : IEmailService
{
    public Task SendWelcomeEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

[Injectable(ServiceLifetime.Scoped)]
public sealed class UserFacade
{
    [Inject] public required IEmailService EmailService { get; init; }
    [Inject] public required ILogger<UserFacade> Logger { get; init; }
}
```

With GenDI the consumer chooses the `ServiceLifetime`, `Group`, `Order`, and `Key`. Use `[Injectable<TService>]` only when you need to force a specific contract and contract discovery does not already find `[ServiceInjection]`. `AddNetMediate()` already calls `AddGenDIServices()` for you.

### Optional companion packages
```xml
<PackageReference Include="NetMediate.Moq" Version="x.x.x" />
<PackageReference Include="NetMediate.Resilience" Version="x.x.x" />
<PackageReference Include="NetMediate.Quartz" Version="x.x.x" />
<PackageReference Include="NetMediate.DataDog.OpenTelemetry" Version="x.x.x" />
<PackageReference Include="NetMediate.DataDog.Serilog" Version="x.x.x" />
<PackageReference Include="NetMediate.DataDog.ILogger" Version="x.x.x" />
```

- **NetMediate.Moq**: lightweight Moq helpers for unit and integration tests (`Mocking.Create`, `AddMockSingleton`, async setup extensions).
- **NetMediate.Resilience**: optional retry, timeout, and circuit-breaker pipeline behaviors for request and notification flows.
- **NetMediate.Quartz**: persists notifications as Quartz.NET jobs, enabling crash recovery and cluster-distributed notification execution.
- **NetMediate.DataDog.OpenTelemetry**: wires NetMediate traces/metrics to DataDog through OpenTelemetry OTLP exporters.
- **NetMediate.DataDog.Serilog**: attaches the DataDog Serilog sink and enriches logs with NetMediate activity fields.
- **NetMediate.DataDog.ILogger**: `ILogger` scope helpers with DataDog-compatible fields and NetMediate correlation values.

## Companion Guides

- [Full documentation website](https://elton.schivei.nom.br/net-mediate)
- [NetMediate.Moq recipes](docs/NETMEDIATE_MOQ_RECIPES.md)
- [API/Worker/Minimal API samples](docs/SAMPLES.md)
- [Diagnostics (traces + metrics)](docs/DIAGNOSTICS.md)
- [Resilience package guide](docs/RESILIENCE.md)
- [Benchmark results](docs/BENCHMARKS.md)
- [Quartz persistent notifications](docs/QUARTZ.md)
- [Source generation guide](docs/SOURCE_GENERATION.md)
- [AOT / NativeAOT and trimming guide](docs/AOT.md)
- [DataDog integrations guide](docs/DATADOG.md)
- [Wiki index](docs/WIKI.md)
- [Validation behavior sample](docs/VALIDATION_BEHAVIOR_SAMPLE.md)

## Quick Start

Here's a minimal example to get you started with NetMediate:

```csharp
// 1. Install the package
// Shared contracts: dotnet add package NetMediate.Core
// Startup/app project: dotnet add package NetMediate.SourceGeneration

// 2. Register services — source generator discovers all handlers automatically
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddNetMediate(); // all handlers in your project are registered here

// 3. Define a notification (no marker interface required)
public record UserCreated(string UserId, string Email);

// 4. Create a handler (Handle returns Task)
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public class UserCreatedHandler : INotificationHandler<UserCreated>
{
    [Inject] public required ILogger<UserCreatedHandler> Logger { get; init; }

    public Task Handle(UserCreated notification, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("User {UserId} was created", notification.UserId);
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

// NetMediate.SourceGeneration discovers handlers automatically at compile time
// and registers all handlers in your project.
builder.Services.AddNetMediate();

var host = builder.Build();
var mediator = host.Services.GetRequiredService<IMediator>();
```

### Notifications

`Notify` runs the notification pipeline (behaviors are fully awaited and their exceptions propagate to the caller). When the pipeline reaches the handler dispatch step, all registered handlers are started simultaneously via `Task.WhenAll` and the result is discarded — handlers are fire-and-forget. Handler exceptions and completion timing have no effect on the pipeline or the caller. When sending a batch of notifications (`IEnumerable`), each message's pipeline is dispatched in parallel (`Task.WhenAll` across messages).

#### Define a Notification Message
```csharp
// No marker interface required — any plain class or record works
public record UserRegistered(string UserId, string Email, DateTime RegisteredAt);
```

#### Create Notification Handlers
```csharp
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public class EmailNotificationHandler : INotificationHandler<UserRegistered>
{
    [Inject] public required IEmailService EmailService { get; init; }

    // Handle must return Task, not Task
    public async Task Handle(UserRegistered notification, CancellationToken cancellationToken = default)
    {
        await EmailService.SendWelcomeEmailAsync(notification.Email, cancellationToken);
    }
}

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 2)]
public class AuditLogHandler : INotificationHandler<UserRegistered>
{
    [Inject] public required IAuditService AuditService { get; init; }

    public async Task Handle(UserRegistered notification, CancellationToken cancellationToken = default)
    {
        await AuditService.LogEventAsync(
            $"User {notification.UserId} registered",
            cancellationToken
        );
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
| Notification | `INotificationHandler<TMessage>` | All handlers started in parallel (fire-and-forget via `Task.WhenAll`); handler exceptions unobserved |
| Stream | `IStreamHandler<TMessage, TResponse>` | All registered handlers, items merged sequentially (handler A items first, then handler B) |

```csharp
// Command — no return value, dispatched to all registered handlers sequentially
public record DeleteUserCommand(string UserId);

// Request — single handler, returns a response
public record GetUserQuery(string UserId);

// Notification — all handlers started in parallel (fire-and-forget); handler exceptions unobserved
public record UserDeleted(string UserId);

// Stream — all registered handlers, items merged sequentially
public record GetRecentEventsQuery(int MaxItems);
```

### Keyed Dispatch

Register handlers under routing keys and dispatch to a specific subset at runtime. This is useful for scenarios such as queue/topic routing, tenant isolation, or environment-specific handling:

```csharp
// Registration — same message type, different keys
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterCommandHandler<DefaultHandler, MyCommand>();        // null key → "__default"
    configure.RegisterCommandHandler<AuditHandler, MyCommand>("audit");  // keyed
});

// Dispatch to null-key (default) handlers
await mediator.Send(new MyCommand(), cancellationToken);

// Dispatch only to "audit" handlers
await mediator.Send("audit", new MyCommand(), cancellationToken);
```

The `key` is propagated through the entire pipeline — behaviors receive it in their `Handle(object? key, ...)` signature and can use it for routing, logging, or conditional logic.

> **Default routing key:** A `null` key (the default when no key is passed) is normalized internally to the constant `Extensions.DEFAULT_ROUTING_KEY = "__default"`. This means `mediator.Send(command, ct)` and `mediator.Send(null, command, ct)` are exactly equivalent. Avoid using the literal string `"__default"` as your own routing key to prevent conflicts.

> **NativeAOT:** Non-keyed registration and dispatch remain fully NativeAOT-compatible. Keyed registration uses `IKeyedServiceProvider` internally, which is **not NativeAOT-compatible**; use it only when NativeAOT is not required.

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
    // Handle receives object? key — the same key passed to the dispatch call.
    // Use it for routing (e.g. queue/topic selection) or contextual filtering.
    public async Task<TResponse> Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var response = await next(key, message, cancellationToken);
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
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching {typeof(TMessage).Name} (key={key})");
        await next(key, message, cancellationToken);
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

`NetMediate.SourceGeneration` is shipped as its own package (`netstandard2.0` analyzer). When installed directly, its `buildTransitive` file adds the required `NetMediate` runtime and `GenDI.SourceGenerator` dependencies automatically.

### Application types covered

Because packages expose `netstandard2.0` and `netstandard2.1` assets they can be consumed by desktop, CLI, mobile, MAUI, and server/web applications.

## Contributing

Contributions are welcome! Please read our [Contributing Guidelines](CONTRIBUTING.md) and [Code of Conduct](CODE_OF_CONDUCT.md).

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
