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
await mediator.Send(new CreateUserCommand("john@example.com", "John Doe"));
```

## Keyed Dispatch

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

> **Default routing key:** A `null` key is normalized internally to `"__default"`. This means `mediator.Send(command, ct)` and `mediator.Send(null, command, ct)` are exactly equivalent. Avoid using `"__default"` as your own routing key.

> **NativeAOT:** Non-keyed registration and dispatch remain fully NativeAOT-compatible. Keyed registration uses `IKeyedServiceProvider` internally, which is **not NativeAOT-compatible**; use it only when NativeAOT is not required.

## See Also

- [Handlers](../getting-started/handlers.md)
- [Pipeline Behaviors](./pipeline-behaviors.md)
