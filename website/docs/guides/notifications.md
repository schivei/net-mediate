---
sidebar_position: 3
---

# Notifications

Notifications are fire-and-forget events sent to multiple handlers. Handlers are dispatched without being awaited; exceptions from handlers are unobserved.

For detailed notification documentation, see the main [README](https://github.com/schivei/net-mediate#notifications).

## Usage

```csharp
await mediator.Notify(new UserCreatedNotification("user-123", "john@example.com"));
```
