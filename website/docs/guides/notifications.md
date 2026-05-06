---
sidebar_position: 3
---

# Notifications

Notifications are events dispatched to multiple handlers. All handlers are started simultaneously in parallel (`Task.WhenAll`) and are fire-and-forget — their results and exceptions do not affect the caller. Batch notifications (`IEnumerable`) are also dispatched in parallel across messages. Pipeline behaviors run fully and their exceptions propagate normally.

## Usage

```csharp
await mediator.Notify(new UserCreatedNotification("user-123", "john@example.com"));
```
