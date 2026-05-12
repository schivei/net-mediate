# Wiki: Installation, Configuration, and Usage

This page centralizes installation, configuration, and usage details for each NetMediate resource.

## ✨ Version highlights

- ✅ `dotnet add package NetMediate.SourceGeneration` is now the recommended startup-project entrypoint.
- 📦 `NetMediate.Core` holds the contracts, while `NetMediate.SourceGeneration` adds the required `PackageReference` entries for `NetMediate` and `GenDI.SourceGenerator` via `buildTransitive`.
- 🧠 Generated typed dispatch extensions improve readability and reduce repetitive mediator boilerplate in large codebases.
- 🔁 `buildTransitive` propagation allows consistent generator behavior across multi-project solutions when transitive flow is desired.

## 1) Contracts package (`NetMediate.Core`)

### Installation

```bash
dotnet add package NetMediate.Core
```

> **Important:** Use this package in shared/contracts-only projects:
>
> ```xml
> <PackageReference Include="NetMediate.Core" Version="x.x.x" />
> ```

### Configuration

Handler registration is done automatically at compile time in the startup/application project via `NetMediate.SourceGeneration`. Call the generated method there:

```csharp
using GenDI;
using NetMediate;

// Source generation discovers all ICommandHandler<>, IRequestHandler<,>,
// INotificationHandler<>, and IStreamHandler<,> implementations in your project
// and generates closed-type AOT-safe registrations automatically.
builder.Services.AddNetMediate();
```

> **GenDI-first style**: `AddNetMediate()` also triggers `AddGenDIServices()`. Prefer `[Injectable]` + `[Inject]` so the consumer can choose `ServiceLifetime`, `Group`, `Order`, and `Key`. Use `[Injectable<TService>]` only when you need to force a specific **non-generic** contract and contract discovery does not already find `[ServiceInjection]`. Concrete non-generic classes that implement **closed generic** contracts can still use `[Injectable]`. Only generic/open service implementations (for example `AuditBehavior<TMessage, TResponse>`) should be registered manually in `builder.Services` for the AOT-oriented path.

> **Registration model scope today**: NetMediate documents the currently released GenDI model only: `[ServiceInjection]` contract discovery, additive `[Injectable<TService>]`, per-implementation `ServiceLifetime` / `Group` / `Order` / `Key` on `[Injectable]`, and keyed property injection via `[Inject(Key = ...)]`. Roadmap items such as `[InjectOptional]`, `[ConditionalInjectable]`, `[DecoratorFor<TService>]`, lifetime overrides on `[ServiceInjection]` / `[Inject]`, factory registration, and modules are still future GenDI work and are not assumed by the NetMediate guides yet.

### Usage

```csharp
// Command: dispatched sequentially to all registered handlers, no return value
await mediator.SendCreateUserCommandAsync(new CreateUserCommand("user-1"), cancellationToken);

// Request: single handler, returns a response
var dto = await mediator.RequestGetUserRequestAsync(new GetUserRequest("user-1"), cancellationToken);

// Notification: all handlers started in parallel (fire-and-forget); handler exceptions discarded by executor
await mediator.NotifyUserCreatedNotificationAsync(new UserCreatedNotification("user-1"), cancellationToken);

// Notification (batch): each message's pipeline dispatched in parallel (Task.WhenAll across messages)
await mediator.NotifyUserCreatedNotificationAsync(new[] { n1, n2, n3 }, cancellationToken);

// Stream: single handler; yields items asynchronously
await foreach (var item in mediator.StreamGetEventsQueryAsync(new GetEventsQuery(), cancellationToken))
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
| `INotificationHandler<TMessage>` | `Task` | All handlers started in parallel (`Task.WhenAll`), fire-and-forget; handler exceptions discarded |
| `IStreamHandler<TMessage, TResponse>` | `IAsyncEnumerable<TResponse>` | All registered handlers, items merged **sequentially** (handler A items first, then handler B) |

> **Unhandled messages**: `Send` and `Notify` are silent no-ops when no handler is registered. `Request` and `RequestStream` throw `InvalidOperationException`.

### Keyed handler registration

Use GenDI metadata to register multiple handlers for the same message type under distinct keys and dispatch to a specific one at runtime:

```csharp
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public sealed class DefaultCommandHandler : ICommandHandler<MyCommand> { }

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 2, Key = "audit")]
public sealed class AuditCommandHandler : ICommandHandler<MyCommand> { }

// Dispatch to the default (null-key) handlers
await mediator.SendMyCommandAsync(command, ct);

// Dispatch only to handlers registered under "audit"
await mediator.SendMyCommandAsync("audit", command, ct);
```

The same `key` parameter is available on all dispatch methods: `Send(key, ...)`, `Notify(key, ...)`, `Request(key, ...)`, and `RequestStream(key, ...)`.

> **Keyless dispatch:** A `null` key flows through the pipeline unchanged. This means `mediator.SendMyCommandAsync(command, ct)` and `mediator.SendMyCommandAsync(null, command, ct)` are exactly equivalent and target the non-keyed handlers registered in the container.

> **NativeAOT:** Keyed dispatch is fully NativeAOT + Trimming compatible. The source generator emits a `KeyedHandlerRegistry<T>` at compile time — no reflection, no `IKeyedServiceProvider` is used at runtime. Both keyed and non-keyed dispatch are safe for NativeAOT and trimmed deployments.

### Optional base class

`ABaseHandler<TMessage, TResult>` is an optional abstract base that implements `IHandler<TMessage, TResult>`. You are not required to use it.

## 2) Pipeline behaviors

### Configuration

Register concrete non-generic behavior implementations with `[Injectable]`. Reserve manual DI registration only for generic/open behavior implementations:

```csharp
[Injectable(ServiceLifetime.Singleton, Group = 10, Order = 1)]
public sealed class MyLoggingBehavior : IPipelineRequestBehavior<MyRequest, MyResponse>
{
    public Task<MyResponse> Handle(
        object? key,
        MyRequest message,
        PipelineBehaviorDelegate<MyRequest, Task<MyResponse>> next,
        CancellationToken cancellationToken) =>
        next(key, message, cancellationToken);
}

builder.Services.AddNetMediate();
```

### Behavior interfaces

| Interface | Applies to |
|---|---|
| `IPipelineBehavior<TMessage, TResult>` | Any pipeline; `TResult` is `Task`, `Task<TResponse>`, or `IAsyncEnumerable<TResponse>` |
| `IPipelineBehavior<TMessage>` | Notification pipeline shorthand (`TResult = Task`) |
| `IPipelineRequestBehavior<TMessage, TResponse>` | Request pipeline shorthand (`TResult = Task<TResponse>`) |
| `IPipelineStreamBehavior<TMessage, TResponse>` | Stream pipeline shorthand (`TResult = IAsyncEnumerable<TResponse>`) |

### Usage

The `next` delegate accepts `(message, cancellationToken)`. Behaviors execute in registration order (outer-to-inner for pre, inner-to-outer for post). Every `Handle` method receives an optional `key` parameter — the same key that was passed to the dispatch call, which you can use for routing or contextual filtering:

```csharp
public sealed class AuditMyRequestBehavior
    : IPipelineRequestBehavior<MyRequest, MyResponse>
{
    public async Task<MyResponse> Handle(
        object? key,
        MyRequest message,
        PipelineBehaviorDelegate<MyRequest, Task<MyResponse>> next,
        CancellationToken cancellationToken)
    {
        // pre-processing (key is available for routing/filtering)
        var result = await next(key, message, cancellationToken);
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
// Override defaults before calling AddNetMediate() — all options are independent
builder.Services.Configure<RetryBehaviorOptions>(opts =>
{
    opts.MaxRetryCount = 2;
    opts.Delay = TimeSpan.Zero;
});

builder.Services.Configure<TimeoutBehaviorOptions>(opts =>
{
    opts.RequestTimeout = TimeSpan.FromSeconds(30);
    opts.NotificationTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.Configure<CircuitBreakerBehaviorOptions>(opts =>
{
    opts.FailureThreshold = 5;
    opts.OpenDuration = TimeSpan.FromSeconds(30);
});
```

See [RESILIENCE.md](RESILIENCE.md) for full details.

## 4) Source generation (`NetMediate.SourceGeneration`)

### Installation

Install `NetMediate.SourceGeneration` directly in the startup/application project. Its `buildTransitive` file adds `NetMediate` and `GenDI.SourceGenerator` automatically:

```xml
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x.x">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

### Usage

```csharp
builder.Services.AddNetMediate();
```

The generator discovers all `ICommandHandler<>`, `IRequestHandler<,>`, `INotificationHandler<>`, and `IStreamHandler<,>` implementations in your project and emits strongly-typed closed-type registrations — no reflection, fully AOT-compatible. See [SOURCE_GENERATION.md](SOURCE_GENERATION.md).

## 5) Quartz (`NetMediate.Quartz`)

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
builder.Services.AddNetMediate();
```

See [QUARTZ.md](QUARTZ.md) for full details.

## 6) Moq (`NetMediate.Moq`)

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
