---
sidebar_position: 4
---

# Handlers

Handlers contain the logic that processes messages in NetMediate. Each handler implements one of the four handler interfaces corresponding to the message type.

## Handler Interfaces

| Interface | Message Type | Return Type | Purpose |
|-----------|--------------|-------------|---------|
| `ICommandHandler<TMessage>` | Command | `Task` | Process commands |
| `IRequestHandler<TMessage, TResponse>` | Request | `Task<TResponse>` | Handle requests |
| `INotificationHandler<TMessage>` | Notification | `Task` | React to notifications |
| `IStreamHandler<TMessage, TResponse>` | Stream | `IAsyncEnumerable<TResponse>` | Stream responses |

## Creating Handlers

### Command Handler

```csharp
public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    private readonly IUserRepository _repository;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        IUserRepository repository,
        ILogger<CreateUserCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating user {Email}", command.Email);

        var user = new User
        {
            Email = command.Email,
            Name = command.Name
        };

        await _repository.AddAsync(user, cancellationToken);

        _logger.LogInformation("User created with ID {UserId}", user.Id);
    }
}
```

### Request Handler

```csharp
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    private readonly IUserRepository _repository;

    public GetUserQueryHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdAsync(query.UserId, cancellationToken);

        if (user == null)
            throw new UserNotFoundException(query.UserId);

        return new UserDto(user.Id, user.Email, user.Name);
    }
}
```

### Notification Handler

```csharp
public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
    private readonly IEmailService _emailService;

    public SendWelcomeEmailHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
    {
        await _emailService.SendWelcomeEmailAsync(
            notification.Email,
            notification.Name,
            cancellationToken);
    }
}
```

### Stream Handler

```csharp
public class GetUserActivityHandler : IStreamHandler<GetUserActivityQuery, ActivityDto>
{
    private readonly IActivityRepository _repository;

    public GetUserActivityHandler(IActivityRepository repository)
    {
        _repository = repository;
    }

    public async IAsyncEnumerable<ActivityDto> Handle(
        GetUserActivityQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var activity in _repository.GetUserActivitiesAsync(
            query.UserId,
            cancellationToken))
        {
            yield return new ActivityDto(
                activity.Id,
                activity.Type,
                activity.Timestamp);
        }
    }
}
```

## Handler Registration

Handlers are automatically discovered and registered by the source generator when you call `AddNetMediate()`:

```csharp
builder.Services.AddNetMediate();
```

The generator scans your assembly for all concrete (non-abstract, non-generic) classes implementing handler interfaces and registers them automatically.

## Dependency Injection

Handlers support constructor injection just like any other service:

```csharp
public class MyHandler : ICommandHandler<MyCommand>
{
    private readonly IRepository _repository;
    private readonly ILogger _logger;
    private readonly IEmailService _emailService;

    public MyHandler(
        IRepository repository,
        ILogger<MyHandler> logger,
        IEmailService emailService)
    {
        _repository = repository;
        _logger = logger;
        _emailService = emailService;
    }

    public async Task Handle(MyCommand command, CancellationToken cancellationToken)
    {
        // Implementation
    }
}
```

## Handler Lifetime

All handlers are registered as **Singleton** services by default. The same instance is reused across mediator operations.

## Multiple Handlers

### Commands and Notifications

Both commands and notifications support multiple handlers:

```csharp
// First handler
public class Handler1 : ICommandHandler<MyCommand>
{
    public Task Handle(MyCommand command, CancellationToken ct)
    {
        Console.WriteLine("Handler 1");
        return Task.CompletedTask;
    }
}

// Second handler
public class Handler2 : ICommandHandler<MyCommand>
{
    public Task Handle(MyCommand command, CancellationToken ct)
    {
        Console.WriteLine("Handler 2");
        return Task.CompletedTask;
    }
}
```

**Commands**: Handlers execute sequentially in registration order.
**Notifications**: Handlers execute concurrently (fire-and-forget).

### Requests and Streams

Only **one** handler can be registered for requests. Registering multiple handlers will result in only the first registered handler being invoked.

For streams, **multiple** `IStreamHandler<TMessage, TResponse>` implementations can be registered. Their results are merged sequentially — each handler's async-enumerable output is yielded in registration order.

## Error Handling

### Commands and Requests

Exceptions thrown in handlers propagate to the caller wrapped in `MediatorException`:

```csharp
try
{
    await mediator.Send(new MyCommand());
}
catch (MediatorException ex)
{
    // ex.InnerException contains the original exception
    // ex.MessageType contains the message type
    // ex.HandlerType contains the handler type
    // ex.TraceId contains the correlation ID
}
```

### Notifications

Exceptions in notification handlers are caught and logged but do not propagate to the caller. This ensures one failing handler doesn't prevent other handlers from executing.

### Streams

Exceptions in stream handlers propagate immediately to the consumer:

```csharp
try
{
    await foreach (var item in mediator.RequestStream<MyQuery, MyResult>(new MyQuery()))
    {
        Console.WriteLine(item);
    }
}
catch (MediatorException ex)
{
    // Handle stream error
}
```

## Best Practices

### Keep Handlers Focused

Each handler should have a single responsibility:

```csharp
// ✅ Good - focused handler
public class CreateUserHandler : ICommandHandler<CreateUserCommand>
{
    public async Task Handle(CreateUserCommand command, CancellationToken ct)
    {
        // Only creates user
    }
}

// ❌ Avoid - doing too much
public class CreateUserHandler : ICommandHandler<CreateUserCommand>
{
    public async Task Handle(CreateUserCommand command, CancellationToken ct)
    {
        // Creates user
        // Sends email
        // Updates analytics
        // Notifies external service
        // ... too many responsibilities
    }
}
```

### Use Cancellation Tokens

Always respect the cancellation token:

```csharp
public async Task Handle(MyCommand command, CancellationToken cancellationToken)
{
    await _httpClient.GetAsync("https://api.example.com", cancellationToken);
    await _repository.SaveAsync(data, cancellationToken);
}
```

### Log Appropriately

Use structured logging:

```csharp
_logger.LogInformation(
    "Processing command {CommandType} for user {UserId}",
    typeof(TCommand).Name,
    command.UserId);
```

### Don't Block

Avoid blocking calls in async handlers:

```csharp
// ❌ Avoid
public async Task Handle(MyCommand command, CancellationToken ct)
{
    Thread.Sleep(1000); // Blocking!
    var result = _service.GetData().Result; // Blocking!
}

// ✅ Good
public async Task Handle(MyCommand command, CancellationToken ct)
{
    await Task.Delay(1000, ct);
    var result = await _service.GetDataAsync(ct);
}
```

## Testing Handlers

Handlers are easy to test in isolation:

```csharp
[Fact]
public async Task Handle_ValidCommand_CreatesUser()
{
    // Arrange
    var repository = new InMemoryUserRepository();
    var handler = new CreateUserHandler(repository);
    var command = new CreateUserCommand("john@example.com", "John Doe");

    // Act
    await handler.Handle(command, CancellationToken.None);

    // Assert
    var users = await repository.GetAllAsync();
    Assert.Single(users);
    Assert.Equal("john@example.com", users[0].Email);
}
```

For more testing examples, see the [Testing Guide](../testing/unit-testing.md).

## Next Steps

- [Pipeline Behaviors](../guides/pipeline-behaviors.md) - Add cross-cutting concerns
- [Validation](../guides/validation.md) - Implement validation patterns
- [Testing](../testing/moq-recipes.md) - Learn testing strategies
