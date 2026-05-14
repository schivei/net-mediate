---
sidebar_position: 3
---

# Notifications

Notifications are events dispatched to multiple handlers. Handlers are started concurrently and the dispatch is fire-and-forget — the pipeline task is discarded and `Task.CompletedTask` is returned immediately to the caller. Handler and behavior exceptions are logged by the executor but do not propagate to the caller. Batch notifications (`IEnumerable`) are dispatched sequentially in a loop.

## Usage

```csharp
await mediator.NotifyUserRegisteredAsync(new UserRegistered("user-123", "john@example.com", "John"));
```

## Complete Example

The following example fans out a `UserRegistered` event to three independent handlers: sending a welcome e-mail, provisioning default settings, and recording an analytics event.

```csharp
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

// ----- Notification message -----
public record UserRegistered(string UserId, string Email, string Name);

// ----- Shared services -----
[ServiceInjection] public interface IEmailService
{
    Task SendWelcomeAsync(string email, string name, CancellationToken ct);
}

[ServiceInjection] public interface IUserSettingsService
{
    Task InitializeDefaultsAsync(string userId, CancellationToken ct);
}

[ServiceInjection] public interface IAnalyticsService
{
    Task TrackAsync(string eventName, string userId, CancellationToken ct);
}

// ----- Handler 1: send welcome e-mail -----
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public class SendWelcomeEmailHandler : INotificationHandler<UserRegistered>
{
    [Inject] public required IEmailService Email { get; init; }

    public async Task Handle(UserRegistered evt, CancellationToken ct) =>
        await Email.SendWelcomeAsync(evt.Email, evt.Name, ct);
}

// ----- Handler 2: provision user settings -----
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 2)]
public class ProvisionUserSettingsHandler : INotificationHandler<UserRegistered>
{
    [Inject] public required IUserSettingsService Settings { get; init; }

    public async Task Handle(UserRegistered evt, CancellationToken ct) =>
        await Settings.InitializeDefaultsAsync(evt.UserId, ct);
}

// ----- Handler 3: record analytics event -----
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 3)]
public class TrackUserRegisteredHandler : INotificationHandler<UserRegistered>
{
    [Inject] public required IAnalyticsService Analytics { get; init; }

    public async Task Handle(UserRegistered evt, CancellationToken ct) =>
        await Analytics.TrackAsync("user_registered", evt.UserId, ct);
}

// ----- Usage -----
// Returns Task.CompletedTask immediately; all three handlers run in the background.
await mediator.NotifyUserRegisteredAsync(
    new UserRegistered("user-123", "john@example.com", "John"),
    cancellationToken);
```

All three handlers start immediately after the call returns — the caller does not wait for any of them to complete, and a failure in one handler does not affect the others.

## Batch Notifications

Dispatch a collection of notifications in a single call:

```csharp
var events = newUsers.Select(u => new UserRegistered(u.Id, u.Email, u.Name));
await mediator.NotifyUserRegisteredAsync(events, cancellationToken);
```

Each message is dispatched sequentially in a loop (not in parallel). This keeps ordering predictable and avoids thread-pool pressure when notifying large batches. Within each message, all handlers are still started concurrently.

