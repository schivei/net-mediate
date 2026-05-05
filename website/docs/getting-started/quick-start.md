---
sidebar_position: 2
---

# Quick Start

Get up and running with NetMediate in just a few minutes! This guide will walk you through creating a simple notification system.

## Step 1: Install Packages

First, install the required NuGet packages:

```bash
dotnet add package NetMediate
dotnet add package NetMediate.SourceGeneration
```

## Step 2: Define a Message

Create a notification message. No marker interfaces are required - any class or record works:

```csharp
namespace MyApp.Notifications;

public record UserCreated(string UserId, string Email, DateTime CreatedAt);
```

## Step 3: Create a Handler

Create one or more handlers for your message:

```csharp
using NetMediate;

namespace MyApp.Handlers;

public class WelcomeEmailHandler : INotificationHandler<UserCreated>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<WelcomeEmailHandler> _logger;

    public WelcomeEmailHandler(
        IEmailService emailService,
        ILogger<WelcomeEmailHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Sending welcome email to {Email} for user {UserId}",
            notification.Email,
            notification.UserId);

        await _emailService.SendWelcomeEmailAsync(
            notification.Email,
            cancellationToken);
    }
}
```

You can create multiple handlers for the same notification:

```csharp
public class AuditLogHandler : INotificationHandler<UserCreated>
{
    private readonly IAuditService _auditService;

    public AuditLogHandler(IAuditService auditService)
    {
        _auditService = auditService;
    }

    public async Task Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        await _auditService.LogAsync(
            $"User {notification.UserId} was created",
            cancellationToken);
    }
}
```

## Step 4: Register Services

Register NetMediate services in your application's startup/configuration:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate;

var builder = Host.CreateApplicationBuilder(args);

// The source generator discovers all handlers automatically
builder.Services.AddNetMediate();

// Register your other services
builder.Services.AddSingleton<IEmailService, EmailService>();
builder.Services.AddSingleton<IAuditService, AuditService>();

var host = builder.Build();
await host.StartAsync();
```

:::tip
`AddNetMediate()` is generated at compile-time by the source generator. It automatically discovers and registers all handler implementations in your project - no manual registration needed!
:::

## Step 5: Use the Mediator

Inject `IMediator` and publish your notification:

```csharp
using NetMediate;

public class UserService
{
    private readonly IMediator _mediator;
    private readonly IUserRepository _userRepository;

    public UserService(IMediator mediator, IUserRepository userRepository)
    {
        _mediator = mediator;
        _userRepository = userRepository;
    }

    public async Task<User> CreateUserAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        // Create the user
        var user = new User { Email = email };
        await _userRepository.AddAsync(user, cancellationToken);

        // Publish the notification - all handlers will be invoked
        await _mediator.Notify(
            new UserCreated(user.Id, user.Email, DateTime.UtcNow),
            cancellationToken);

        return user;
    }
}
```

## Complete Example

Here's a complete minimal API example:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

var builder = WebApplication.CreateBuilder(args);

// Register NetMediate
builder.Services.AddNetMediate();

var app = builder.Build();

// Define the endpoint
app.MapPost("/users", async (CreateUserRequest request, IMediator mediator) =>
{
    // Publish a notification
    await mediator.Notify(new UserCreated(
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
public class UserCreatedHandler : INotificationHandler<UserCreated>
{
    private readonly ILogger<UserCreatedHandler> _logger;

    public UserCreatedHandler(ILogger<UserCreatedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "User created: {UserId}, {Email}",
            notification.UserId,
            notification.Email);

        return Task.CompletedTask;
    }
}
```

## What's Next?

Now that you have a working example, explore more features:

- **[Message Types](./message-types.md)** - Learn about Commands, Requests, Notifications, and Streams
- **[Handlers](./handlers.md)** - Deep dive into handler implementations
- **[Pipeline Behaviors](../guides/pipeline-behaviors.md)** - Add cross-cutting concerns
- **[Resilience](../advanced/resilience.md)** - Add retry and timeout behaviors
- **[Testing](../testing/moq-recipes.md)** - Learn how to test your handlers

## Troubleshooting

### `AddNetMediate()` not found

If the `AddNetMediate()` method is not available:

1. Ensure `NetMediate.SourceGeneration` is installed with correct attributes
2. Rebuild your project
3. Restart your IDE or refresh IntelliSense
4. Check that your handler classes are not abstract or generic

### Handlers not being called

If your handlers aren't executing:

1. Verify handlers implement the correct interface
2. Ensure handlers are concrete (non-abstract) classes
3. Check that you're using the correct mediator method (`Notify`, `Send`, `Request`, etc.)
4. Enable logging to see mediator activity
