---
sidebar_position: 1
---

# Commands

Commands represent imperative actions in your application. This guide covers everything you need to know about using commands effectively.

## Overview

Commands are dispatched to **all** registered handlers **sequentially** in registration order. Use commands when you want to trigger side-effects across multiple consumers with no return value.

For the complete commands documentation, see the main [README](https://github.com/schivei/net-mediate#commands).

## Usage

```csharp
await mediator.Send(new CreateUserCommand("john@example.com", "John Doe"));
```

## See Also

- [Handlers](../getting-started/handlers.md)
- [Pipeline Behaviors](./pipeline-behaviors.md)
