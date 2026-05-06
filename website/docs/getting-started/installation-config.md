---
sidebar_position: 5
---

# Installation, Configuration & Usage

This page centralizes installation, configuration, and usage details for each NetMediate package.

## Core package (`NetMediate`)

### Installation

```bash
dotnet add package NetMediate
```

Then open your `.csproj` and add `PrivateAssets="all"` to the `PackageReference`:

```xml
<PackageReference Include="NetMediate" Version="x.x.x" PrivateAssets="all" />
```

:::caution Required: PrivateAssets="all"
`PrivateAssets="all"` is **required**. The `NetMediate.SourceGeneration` analyzer is bundled inside `NetMediate` and is activated only when this attribute is present. Without it, `AddNetMediate()` will not be generated and handler registration will not work.
:::

### Configuration

Handler registration is done automatically at compile time via the bundled source generator. Call the generated method:

```csharp
using NetMediate;

// Source generation discovers all ICommandHandler<>, IRequestHandler<,>,
// INotificationHandler<>, and IStreamHandler<,> implementations in your project
// and generates closed-type AOT-safe registrations automatically.
builder.Services.AddNetMediate();
```

### Usage

```csharp
// Command: dispatched sequentially to all registered handlers, no return value
await mediator.Send(new CreateUserCommand("user-1"), cancellationToken);

// Request: single handler, returns a response
var dto = await mediator.Request<GetUserRequest, UserDto>(new GetUserRequest("user-1"), cancellationToken);

// Notification: fire-and-forget dispatch to all registered handlers (exceptions unobserved)
await mediator.Notify(new UserCreatedNotification("user-1"), cancellationToken);

// Notification (batch): each message dispatched sequentially (one after another)
await mediator.Notify(new[] { n1, n2, n3 }, cancellationToken);

// Stream: single handler; yields items asynchronously
await foreach (var item in mediator.RequestStream<GetEventsQuery, EventDto>(new GetEventsQuery(), cancellationToken))
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
| `INotificationHandler<TMessage>` | `Task` | All registered handlers, fire-and-forget; exceptions unobserved |
| `IStreamHandler<TMessage, TResponse>` | `IAsyncEnumerable<TResponse>` | All registered handlers, items merged **sequentially** (handler A items first, then handler B) |

:::note Unhandled messages
`Send` and `Notify` are silent no-ops when no handler is registered. `Request` and `RequestStream` throw `InvalidOperationException`.
:::

### Keyed handler registration

All `Register*Handler` methods accept an optional `key` argument. This lets you register multiple handlers for the same message type under distinct keys and dispatch to a specific one at runtime:

```csharp
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterCommandHandler<DefaultCommandHandler, MyCommand>();          // null key → "__default"
    configure.RegisterCommandHandler<AuditCommandHandler, MyCommand>("audit");    // keyed
});

// Dispatch to the default (null-key) handlers
await mediator.Send(command, ct);

// Dispatch only to handlers registered under "audit"
await mediator.Send("audit", command, ct);
```

The same `key` parameter is available on all dispatch methods: `Send(key, ...)`, `Notify(key, ...)`, `Request(key, ...)`, and `RequestStream(key, ...)`.

:::note Default routing key
A `null` key is normalized internally to `Extensions.DEFAULT_ROUTING_KEY = "__default"`. This means `mediator.Send(command, ct)` and `mediator.Send(null, command, ct)` are exactly equivalent. Avoid using the literal string `"__default"` as your own routing key to prevent conflicts.
:::

:::caution NativeAOT
Non-keyed registration and dispatch remain fully NativeAOT-compatible. Keyed registration uses `IKeyedServiceProvider` internally, which is **not NativeAOT-compatible**; use it only when NativeAOT is not required.
:::

### Optional base class

`ABaseHandler<TMessage, TResult>` is an optional abstract base that implements `IHandler<TMessage, TResult>`. You are not required to use it.

## Pipeline behaviors

### Configuration

Register behavior implementations using the builder:

```csharp
// Via builder (closed-type, fully AOT-safe — the only supported approach)
builder.Services.UseNetMediate(configure =>
{
    configure.RegisterBehavior<MyLoggingBehavior, MyRequest, Task<MyResponse>>();
});
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
public sealed class AuditRequestBehavior<TMessage, TResponse>
    : IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{
    public async Task<TResponse> Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        // pre-processing (key is available for routing/filtering)
        var result = await next(key, message, cancellationToken);
        // post-processing
        return result;
    }
}
```

:::tip Validation
There is no built-in validation in NetMediate. Implement your own validation as a pipeline behavior. See the [Validation guide](validation) for an example.
:::

## Resilience package (`NetMediate.Resilience`)

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

See the [Resilience guide](../advanced/resilience) for full details.

## Source generation (`NetMediate.SourceGeneration`)

### Installation

`NetMediate.SourceGeneration` is **bundled inside the `NetMediate` package** — no separate installation is required. The analyzer is activated by setting `PrivateAssets="all"` on the `NetMediate` `PackageReference` (see the Core package section above).

### Usage

```csharp
builder.Services.AddNetMediate();
```

The generator discovers all `ICommandHandler<>`, `IRequestHandler<,>`, `INotificationHandler<>`, and `IStreamHandler<,>` implementations in your project and emits strongly-typed closed-type registrations — no reflection, fully AOT-compatible. See the [Source Generation guide](../advanced/source-generation).

## Quartz (`NetMediate.Quartz`)

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
```

See the [Quartz guide](../advanced/quartz) for full details.

## Moq (`NetMediate.Moq`)

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

See the [Moq Recipes guide](../testing/moq-recipes) for full details.

## DataDog integrations

### Installation

```bash
dotnet add package NetMediate.DataDog.OpenTelemetry
dotnet add package NetMediate.DataDog.Serilog
dotnet add package NetMediate.DataDog.ILogger
```

See the [DataDog guide](../advanced/datadog) for full details.
