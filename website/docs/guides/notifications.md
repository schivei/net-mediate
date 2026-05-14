---
sidebar_position: 3
---

# Notifications

Notifications are events dispatched to multiple handlers. Handlers are started concurrently and the dispatch is fire-and-forget — the pipeline task is discarded and `Task.CompletedTask` is returned immediately to the caller. Handler and behavior exceptions are logged by the executor but do not propagate to the caller. Batch notifications (`IEnumerable`) are dispatched sequentially in a loop.

## Usage

```csharp
await mediator.NotifyUserCreatedNotificationAsync(new UserCreatedNotification("user-123", "john@example.com"));
```
