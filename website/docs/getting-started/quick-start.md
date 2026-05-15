---
sidebar_position: 2
---

# Quick Start

Get up and running with NetMediate in just a few minutes! This guide will walk you through creating a simple notification system.

## 🚀 Step 1: Install Packages

First, install the required NuGet package:

```bash
dotnet add package NetMediate.Core
dotnet add package NetMediate.SourceGeneration
```

> 💡 **Release productivity highlight**
>
> Use `NetMediate.Core` for contracts and `NetMediate.SourceGeneration` in the startup project. The generator package brings the runtime + required generators automatically.

Then open your `.csproj` and add the `PackageReference`:

```xml
<PackageReference Include="NetMediate.Core" Version="*" />
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x.x">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>contentfiles; compile; runtime</PrivateAssets>
</PackageReference>
```

:::tip Library projects
Use the generator package with the explicit analyzer-style metadata:

```xml
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x.x">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>contentfiles; compile; runtime</PrivateAssets>
</PackageReference>
```
:::

## 🧩 Step 2: Define a Message

Create a notification message. No marker interfaces are required - any class or record works:

```csharp
namespace MyApp.Notifications;

public record UserCreated(string UserId, string Email, DateTime CreatedAt);
```

## 🛠️ Step 3: Create a Handler

Create one or more handlers for your message:

```csharp
using GenDI;
using NetMediate;

namespace MyApp.Handlers;

[ServiceInjection]
public interface IEmailService
{
    Task SendWelcomeEmailAsync(string email, CancellationToken cancellationToken);
}

[Injectable(ServiceLifetime.Scoped)]
public sealed class EmailService : IEmailService
{
    public Task SendWelcomeEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public class WelcomeEmailHandler : INotificationHandler<UserCreated>
{
    [Inject] public required IEmailService EmailService { get; init; }
    [Inject] public required ILogger<WelcomeEmailHandler> Logger { get; init; }

    public async Task Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "Sending welcome email to {Email} for user {UserId}",
            notification.Email,
            notification.UserId);

        await EmailService.SendWelcomeEmailAsync(
            notification.Email,
            cancellationToken);
    }
}
```

You can create multiple handlers for the same notification:

```csharp
[ServiceInjection]
public interface IAuditService
{
    Task LogAsync(string message, CancellationToken cancellationToken);
}

[Injectable(ServiceLifetime.Scoped)]
public sealed class AuditService : IAuditService
{
    public Task LogAsync(string message, CancellationToken cancellationToken) => Task.CompletedTask;
}

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 2)]
public class AuditLogHandler : INotificationHandler<UserCreated>
{
    [Inject] public required IAuditService AuditService { get; init; }

    public async Task Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        await AuditService.LogAsync(
            $"User {notification.UserId} was created",
            cancellationToken);
    }
}
```

## ⚙️ Step 4: Register Services

Register NetMediate services in your application's startup/configuration:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate;

var builder = Host.CreateApplicationBuilder(args);

// The source generator discovers all handlers automatically
builder.Services.AddNetMediate();

// AddNetMediate also triggers AddGenDIServices(),
// so [Injectable] implementations are registered automatically.

var host = builder.Build();
await host.StartAsync();
```

:::tip
`AddNetMediate()` is generated at compile-time by the source generator. It automatically discovers and registers all handler implementations in your project - no manual registration needed!
:::

> **GenDI style:** prefer `[Injectable]` + `[Inject]`. With GenDI you can choose the service lifetime, `Group`, `Order`, and keyed registrations (`Key`). Use `[Injectable<TService>]` only when you need to force a specific **non-generic** contract and contract discovery does not already find `[ServiceInjection]`. Concrete non-generic classes that implement **closed generic** contracts can still use `[Injectable]`. Only generic/open service implementations (for example `AuditBehavior<TMessage, TResponse>`) should be registered manually in `builder.Services` for the AOT-oriented path.

## 📣 Step 5: Use the Mediator

Inject `IMediator` and publish your notification:

```csharp
using GenDI;
using NetMediate;

[Injectable(ServiceLifetime.Scoped)]
public class UserService
{
    [Inject] public required IMediator Mediator { get; init; }
    [Inject] public required IUserRepository UserRepository { get; init; }

    public async Task<User> CreateUserAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        // Create the user
        var user = new User { Email = email };
        await UserRepository.AddAsync(user, cancellationToken);

        // Publish the notification - all handlers will be invoked
        await Mediator.Notify(
            new UserCreated(user.Id, user.Email, DateTime.UtcNow),
            cancellationToken);

        return user;
    }
}
```

## ✅ Complete Example

Here's a complete minimal API example:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;
using GenDI;

var builder = WebApplication.CreateBuilder(args);

// Register NetMediate
builder.Services.AddNetMediate();

var app = builder.Build();

// Define the endpoint
app.MapPost("/users", async (CreateUserRequest request, IMediator mediator) =>
{
    // Publish a notification
    await mediator.NotifyUserCreatedAsync(new(
        Guid.NewGuid().ToString(),
        request.Email,
        DateTime.UtcNow));

    return Results.Ok(new { Message = "User created successfully" });
});

await app.RunAsync();

// Message types
public record CreateUserRequest(string Email);
public record UserCreated(string UserId, string Email, DateTime CreatedAt);

// Handler
[Injectable(ServiceLifetime.Scoped)]
public class UserCreatedHandler : INotificationHandler<UserCreated>
{
    [Inject] public required ILogger<UserCreatedHandler> Logger { get; init; }

    public Task Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "User created: {UserId}, {Email}",
            notification.UserId,
            notification.Email);

        return Task.CompletedTask;
    }
}
```

## 🔭 What's Next?

Now that you have a working example, explore more features:

- **[Message Types](./message-types.md)** - Learn about Commands, Requests, Notifications, and Streams
- **[Handlers](./handlers.md)** - Deep dive into handler implementations
- **[Pipeline Behaviors](../guides/pipeline-behaviors.md)** - Add cross-cutting concerns
- **[Resilience](../advanced/resilience.md)** - Add retry and timeout behaviors
- **[Testing](../testing/moq-recipes.md)** - Learn how to test your handlers

## Troubleshooting

### `AddNetMediate()` not found

If the `AddNetMediate()` method is not available:

1. Ensure your startup project has a direct `NetMediate.SourceGeneration` package reference
2. Rebuild your project
3. Restart your IDE or refresh IntelliSense
4. Check that your handler classes are not abstract or generic

### Handlers not being called

If your handlers aren't executing:

1. Verify handlers implement the correct interface
2. Ensure handlers are concrete (non-abstract) classes
3. Check that you're using the correct mediator method (`Notify`, `Send`, `Request`, etc.)
4. Enable logging to see mediator activity
