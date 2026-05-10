---
sidebar_position: 1
---

# Commands

Commands represent imperative actions in your application. This guide covers everything you need to know about using commands effectively.

## Overview

Commands are dispatched to **all** registered handlers **sequentially** in registration order. Use commands when you want to trigger side-effects across multiple consumers with no return value.

For the complete commands documentation, see the main [README](https://github.com/schivei/net-mediate#commands).

## Basic Usage

```csharp
await mediator.SendCreateUserCommandAsync(new CreateUserCommand("john@example.com", "John Doe"));
```

## Keyed Dispatch

Register handlers under routing keys and dispatch to a specific subset at runtime. This is useful for scenarios such as queue/topic routing, tenant isolation, or environment-specific handling:

```csharp
builder.Services.AddNetMediate();

// Dispatch to null-key (default) handlers
await mediator.SendMyCommandAsync(new MyCommand(), cancellationToken);

// Dispatch only to "audit" handlers
await mediator.SendMyCommandAsync("audit", new MyCommand(), cancellationToken);

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public sealed class DefaultHandler : ICommandHandler<MyCommand>
{
    public Task Handle(MyCommand message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 2, Key = "audit")]
public sealed class AuditHandler : ICommandHandler<MyCommand>
{
    public Task Handle(MyCommand message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
```

The `key` is propagated through the entire pipeline — behaviors receive it in their `Handle(object? key, ...)` signature and can use it for routing, logging, or conditional logic.

> **Keyless dispatch:** A `null` key flows through the pipeline unchanged. `mediator.SendMyCommandAsync(command, ct)` and `mediator.SendMyCommandAsync(null, command, ct)` are equivalent and target the non-keyed handlers registered in the container.

> **NativeAOT:** Keyed dispatch is fully NativeAOT + Trimming compatible. The source generator emits a `KeyedHandlerRegistry<T>` at compile time — no reflection, no `IKeyedServiceProvider` is used at runtime. Both keyed and non-keyed dispatch are safe for NativeAOT and trimmed deployments.

## See Also

- [Handlers](../getting-started/handlers.md)
- [Pipeline Behaviors](./pipeline-behaviors.md)
