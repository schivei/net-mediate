---
sidebar_position: 3
---

# Notifications

Notifications are events dispatched to multiple handlers. All handlers are started simultaneously in parallel (`Task.WhenAll`) and are fire-and-forget — handler exceptions are logged by the executor but do not propagate to the caller. Batch notifications (`IEnumerable`) are also dispatched in parallel across messages. Pipeline behaviors run fully and their exceptions propagate normally.

## Usage

```csharp
await mediator.NotifyUserCreatedNotificationAsync(new UserCreatedNotification("user-123", "john@example.com"));
```
