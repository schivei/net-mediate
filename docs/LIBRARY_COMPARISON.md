# Library Comparison: NetMediate, MediatR, and Alternatives

This document explains the technical and design differences between **NetMediate** and the
most popular mediator-pattern libraries in the .NET ecosystem so that you can make an
informed choice for your project.

---

## Table of Contents

- [Overview](#overview)
- [Feature Matrix](#feature-matrix)
- [API Surface Comparison](#api-surface-comparison)
- [Registration & Startup](#registration--startup)
- [Notification Dispatch](#notification-dispatch)
- [Pipeline Behaviours](#pipeline-behaviours)
- [Validation](#validation)
- [Streaming](#streaming)
- [Observability](#observability)
- [Resilience](#resilience)
- [Source Generation & AOT](#source-generation--aot)
- [Migration from MediatR](#migration-from-mediatr)
- [When to Choose Which Library](#when-to-choose-which-library)

---

## Overview

| Library | Latest stable | Min .NET | Maintained |
|---------|--------------|----------|------------|
| **NetMediate** | see NuGet | netstandard2.0 / net10+ | ✅ Active |
| **MediatR** | 14.x | net8+ | ✅ Active |
| **Wolverine** | 3.x | net8+ | ✅ Active |
| **Mediator** (martinothamar) | 2.x | net8+ | ✅ Active |

---

## Feature Matrix

| Feature | NetMediate | MediatR 14 | Wolverine | martinothamar/Mediator |
|---------|:----------:|:----------:|:---------:|:----------------------:|
| Commands (fire & forget) | ✅ | ✅ (as `IRequest`) | ✅ | ✅ |
| Requests (query/response) | ✅ | ✅ | ✅ | ✅ |
| Notifications (fan-out) | ✅ | ✅ | ✅ | ✅ |
| Streaming responses | ✅ | ✅ | ✅ | ✅ |
| Built-in validation | ✅ | ❌ | ❌ | ❌ |
| FluentValidation bridge | ✅ (`NetMediate.FluentValidation`) | ❌ (separate libs) | partial | ❌ |
| Pipeline behaviours | ✅ per-kind interfaces | ✅ `IPipelineBehavior<,>` | ✅ middleware | ✅ |
| Keyed service support | ✅ | ❌ | ❌ | ❌ |
| Source generation | ✅ (`NetMediate.SourceGeneration`) | ❌ | ❌ | ✅ (always-on) |
| AOT / trimming annotations | ✅ (scan paths annotated) | ❌ | ❌ | ✅ |
| MediatR migration shim | ✅ (`NetMediate.Compat`) | N/A | ❌ | ❌ |
| Resilience (retry/circuit) | ✅ (`NetMediate.Resilience`) | ❌ | partial | ❌ |
| OpenTelemetry built-in | ✅ | ❌ | ✅ | ❌ |
| netstandard2.0 support | ✅ | ❌ (net8+ only from v12) | ❌ | ❌ |

---

## API Surface Comparison

### Commands / Fire-and-forget

**NetMediate** introduces an explicit `ICommand<TMessage>` contract to make intent clear:
```csharp
// Define
public record CreateOrder(string ProductId, int Qty) : ICommand<CreateOrder>;

// Handle
public class CreateOrderHandler : ICommandHandler<CreateOrder>
{
    public Task Handle(CreateOrder cmd, CancellationToken ct) => ...;
}

// Send
await mediator.Send(new CreateOrder("SKU-1", 3), ct);
```

**MediatR** uses the same `IRequest<Unit>` base for everything, treating commands as
requests with no response:
```csharp
public record CreateOrder(string ProductId, int Qty) : IRequest;

public class CreateOrderHandler : IRequestHandler<CreateOrder>
{
    public Task Handle(CreateOrder cmd, CancellationToken ct) => ...;
}

await mediator.Send(new CreateOrder("SKU-1", 3), ct);
```

### Requests / Query-Response

**NetMediate** uses a two-type-argument generic to make both the message and response
types explicit:
```csharp
var result = await mediator.Request<GetOrderQuery, OrderDto>(new GetOrderQuery(id), ct);
```

**MediatR** infers the response from the request type:
```csharp
var result = await mediator.Send(new GetOrderQuery(id), ct);
```

### Notifications

**NetMediate** supports an optional per-handler error callback to allow fan-out to
continue even when one handler throws:
```csharp
await mediator.Notify(
    new OrderShipped(orderId),
    (handlerType, message, ex) => logger.LogError(ex, "Handler {H} failed", handlerType.Name),
    ct);
```

**MediatR** propagates exceptions from the first failing handler by default (configurable
with a custom `INotificationPublisher`).

---

## Registration & Startup

| Approach | NetMediate | MediatR 14 |
|----------|-----------|-----------|
| Assembly scan (reflection) | `services.AddNetMediate(assembly)` | `services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(asm))` |
| Explicit registration | `builder.RegisterCommandHandler<Cmd, Handler>()` | must register manually via DI |
| Source-generated (AOT) | `services.AddNetMediateGenerated()` | ❌ not available |

### Startup reflection cost

Both libraries perform reflection at startup to discover handler types.  NetMediate offers
the **`NetMediate.SourceGeneration`** analyzer package to move that cost to compile time
and to produce a zero-reflection startup path that is compatible with Native AOT.

After startup, the dispatch hot-path in NetMediate is reflection-free:
* Notification dispatch uses a virtual `DispatchAsync` method on the packet rather than
  `MethodInfo.MakeGenericMethod` (eliminated in v2+).
* `KeyedMessageAttribute` lookups are cached in a `ConcurrentDictionary` so that each
  type is inspected via reflection at most once.

MediatR uses a similar `ServiceFactory` pattern internally but does not provide a
source-generation alternative.

---

## Notification Dispatch

NetMediate dispatches notifications **asynchronously** through a channel + background
worker (`NotificationWorker`).  This decouples the caller from the fan-out latency and
allows multiple notifications to be queued while handlers run in the background.

MediatR dispatches notifications **synchronously** within the call to `Publish`, using
the registered `INotificationPublisher` strategy (sequential or parallel).

Wolverine uses a full message-bus model (in-process and/or out-of-process) which is more
suited to distributed systems.

---

## Pipeline Behaviours

### NetMediate

NetMediate provides **per-kind** behaviour interfaces so that the compiler enforces the
correct signature for each message kind:

| Kind | Interface |
|------|-----------|
| Commands | `ICommandBehavior<TMessage>` |
| Requests | `IRequestBehavior<TMessage, TResponse>` |
| Notifications | `INotificationBehavior<TMessage>` |
| Streams | `IStreamBehavior<TMessage, TResponse>` |

```csharp
public class LoggingBehavior<TMessage, TResponse> : IRequestBehavior<TMessage, TResponse>
{
    public async Task<TResponse> Handle(
        TMessage msg, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        _logger.LogInformation("Handling {Type}", typeof(TMessage).Name);
        var result = await next(ct);
        _logger.LogInformation("Handled {Type}", typeof(TMessage).Name);
        return result;
    }
}
```

### MediatR

MediatR uses a single `IPipelineBehavior<TRequest, TResponse>` interface for all message
kinds, which requires more boilerplate constraints to specialize for commands:

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest req, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        _logger.LogInformation("Handling {Type}", typeof(TRequest).Name);
        var result = await next();
        _logger.LogInformation("Handled {Type}", typeof(TRequest).Name);
        return result;
    }
}
```

---

## Validation

| | NetMediate | MediatR |
|-|-----------|---------|
| Self-validating messages | ✅ `IValidatable` | ❌ |
| External validation handlers | ✅ `IValidationHandler<T>` | Via `IPipelineBehavior` + FluentValidation |
| FluentValidation bridge | ✅ `NetMediate.FluentValidation` package | Separate community packages |

NetMediate validates messages **before** they reach the handler, throwing
`MessageValidationException` on failure.  The validation pipeline is extensible: any
number of `IValidationHandler<T>` instances can be registered and they run in sequence.

---

## Streaming

Both libraries support `IAsyncEnumerable<TResponse>` streaming handlers.  The API
differs in that NetMediate requires an explicit `IStreamHandler<TMessage, TResponse>`
interface while MediatR uses `IStreamRequestHandler<TRequest, TResponse>`.

---

## Observability

NetMediate ships with **built-in** `ActivitySource` and `Meter` instrumentation in the
core package (no extra package required).  Every `Send`, `Request`, `Notify`, and stream
operation is traced and measured automatically.

MediatR has no built-in telemetry; you need a community package or custom
`IPipelineBehavior` to add tracing.

NetMediate also provides three DataDog-specific companion packages:
- `NetMediate.DataDog.OpenTelemetry`
- `NetMediate.DataDog.Serilog`
- `NetMediate.DataDog.ILogger`

---

## Resilience

`NetMediate.Resilience` adds optional **retry**, **timeout**, and **circuit-breaker**
pipeline behaviours backed by Polly v8.  Simply call `services.AddNetMediateResilience()`
and configure the policies:

```csharp
services.AddNetMediateResilience(
    configureRetry: o => { o.MaxRetryCount = 3; o.Delay = TimeSpan.FromMilliseconds(50); },
    configureTimeout: o => { o.RequestTimeout = TimeSpan.FromSeconds(5); });
```

MediatR has no built-in resilience support.

---

## Source Generation & AOT

| | NetMediate | MediatR 14 | martinothamar/Mediator |
|-|-----------|-----------|----------------------|
| Source generator | Optional (`NetMediate.SourceGeneration`) | ❌ | Always-on (required) |
| AOT-safe dispatch | ✅ (no runtime reflection post-startup) | ❌ | ✅ |
| Scan paths annotated | ✅ (`[RequiresUnreferencedCode]` on .NET 5+) | ❌ | N/A |

Using `NetMediate.SourceGeneration` generates an `AddNetMediateGenerated()` extension
method at compile time.  This means:
1. Zero startup reflection cost.
2. Works with `PublishAot` and aggressive linker trimming.
3. The source generator validates that every handler registered at compile time actually
   implements the correct interface.

---

## Migration from MediatR

`NetMediate.Compat` provides a drop-in MediatR shim that re-exports the
`MediatR.IMediator`, `MediatR.IRequest`, `MediatR.INotification`, and handler interfaces
from the same namespace.  Existing MediatR code compiles and runs unchanged while you
migrate handler-by-handler to the native NetMediate contracts.

See [docs/MEDIATR_MIGRATION_GUIDE.md](MEDIATR_MIGRATION_GUIDE.md) for step-by-step
instructions.

---

## When to Choose Which Library

| Scenario | Recommended |
|----------|-------------|
| New .NET 8+ project, want zero-reflection startup | **NetMediate** (with SourceGeneration) or **martinothamar/Mediator** |
| Existing MediatR codebase, incremental migration | **NetMediate** + `NetMediate.Compat` |
| Need netstandard2.0 support (desktop, MAUI, Xamarin) | **NetMediate** |
| Distributed messaging / service bus needed | **Wolverine** |
| Minimal dependencies, community ecosystem matters | **MediatR** |
| Need built-in resilience without extra packages | **NetMediate.Resilience** |
| Need FluentValidation integration | **NetMediate.FluentValidation** |

---

*For benchmark numbers, see [BENCHMARK_COMPARISON.md](BENCHMARK_COMPARISON.md).*
