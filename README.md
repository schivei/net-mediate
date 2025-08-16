# NetMediate

[![CI/CD Pipeline](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml)

A lightweight and efficient .NET implementation of the Mediator pattern, providing a clean alternative to MediatR for in-process messaging and communication between components.

## Introduction

NetMediate is a mediator pattern library for .NET that enables decoupled communication between components in your application. It provides a simple and flexible way to send commands, publish notifications, make requests, and handle streaming responses while maintaining clean architecture principles.

### Key Features

- **Commands**: Send one-way messages to single handlers
- **Notifications**: Publish messages to multiple handlers simultaneously
- **Requests**: Send messages and receive responses
- **Streaming**: Handle requests that return multiple responses over time
- **Validation**: Built-in message validation support with custom validators
- **Dependency Injection**: Seamless integration with Microsoft.Extensions.DependencyInjection
- **Keyed Services**: Support for keyed service registration and resolution
- **Cancellation Support**: Full cancellation token support across all operations
- **Multi-targeting**: Supports .NET 8.0, .NET 9.0, and .NET Standard 2.1

## Installation

### Package Manager Console
```powershell
Install-Package NetMediate
```

### .NET CLI
```bash
dotnet add package NetMediate
```

### PackageReference
```xml
<PackageReference Include="NetMediate" Version="x.x.x" />
```

## Usage Examples

### Basic Setup

First, register NetMediate services in your dependency injection container:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate;

var builder = Host.CreateApplicationBuilder();

// Register NetMediate and scan all loaded assemblies for handlers
builder.Services.AddNetMediate();

// Or scan specific assemblies
builder.Services.AddNetMediate(typeof(MyHandler).Assembly);

// Or scan assemblies containing a specific type
builder.Services.AddNetMediate<MyHandler>();

var host = builder.Build();
var mediator = host.Services.GetRequiredService<IMediator>();
```

### Notifications

Notifications are published to all registered handlers simultaneously.

#### Define a Notification Message
```csharp
public record UserRegistered(string UserId, string Email, DateTime RegisteredAt);
```

#### Create Notification Handlers
```csharp
public class EmailNotificationHandler : INotificationHandler<UserRegistered>
{
    private readonly IEmailService _emailService;
    
    public EmailNotificationHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }
    
    public async Task Handle(UserRegistered notification, CancellationToken cancellationToken = default)
    {
        await _emailService.SendWelcomeEmailAsync(notification.Email, cancellationToken);
    }
}

public class AuditLogHandler : INotificationHandler<UserRegistered>
{
    private readonly IAuditService _auditService;
    
    public AuditLogHandler(IAuditService auditService)
    {
        _auditService = auditService;
    }
    
    public async Task Handle(UserRegistered notification, CancellationToken cancellationToken = default)
    {
        await _auditService.LogEventAsync($"User {notification.UserId} registered", cancellationToken);
    }
}
```

#### Publish Notifications
```csharp
var notification = new UserRegistered("user123", "user@example.com", DateTime.UtcNow);
await mediator.Notify(notification);
```

### Commands

Commands are sent to a single handler for processing.

#### Define a Command
```csharp
public record CreateUserCommand(string Email, string FirstName, string LastName);
```

#### Create a Command Handler
```csharp
public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    private readonly IUserRepository _userRepository;
    
    public CreateUserCommandHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }
    
    public async Task Handle(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        var user = new User
        {
            Email = command.Email,
            FirstName = command.FirstName,
            LastName = command.LastName
        };
        
        await _userRepository.CreateAsync(user, cancellationToken);
    }
}
```

#### Send Commands
```csharp
var command = new CreateUserCommand("user@example.com", "John", "Doe");
await mediator.Send(command);
```

### Requests

Requests are sent to a handler and return a response.

#### Define a Request and Response
```csharp
public record GetUserQuery(string UserId);
public record UserDto(string Id, string Email, string FirstName, string LastName);
```

#### Create a Request Handler
```csharp
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    private readonly IUserRepository _userRepository;
    
    public GetUserQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }
    
    public async Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(query.UserId, cancellationToken);
        
        return new UserDto(user.Id, user.Email, user.FirstName, user.LastName);
    }
}
```

#### Send Requests
```csharp
var query = new GetUserQuery("user123");
var userDto = await mediator.Request<GetUserQuery, UserDto>(query);
```

### Streams

Streams allow handlers to return multiple responses over time.

#### Define a Stream Request
```csharp
public record GetUserActivityQuery(string UserId, DateTime FromDate);
public record ActivityDto(string Id, string Action, DateTime Timestamp);
```

#### Create a Stream Handler
```csharp
public class GetUserActivityQueryHandler : IStreamHandler<GetUserActivityQuery, ActivityDto>
{
    private readonly IActivityRepository _activityRepository;
    
    public GetUserActivityQueryHandler(IActivityRepository activityRepository)
    {
        _activityRepository = activityRepository;
    }
    
    public async IAsyncEnumerable<ActivityDto> Handle(
        GetUserActivityQuery query, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var activity in _activityRepository.GetUserActivityStreamAsync(
            query.UserId, query.FromDate, cancellationToken))
        {
            yield return new ActivityDto(activity.Id, activity.Action, activity.Timestamp);
        }
    }
}
```

#### Process Streams
```csharp
var query = new GetUserActivityQuery("user123", DateTime.UtcNow.AddDays(-30));

await foreach (var activity in mediator.RequestStream<GetUserActivityQuery, ActivityDto>(query))
{
    Console.WriteLine($"{activity.Timestamp}: {activity.Action}");
}
```

### Validations

NetMediate supports message validation through multiple approaches:

#### Self-Validating Messages
```csharp
using System.ComponentModel.DataAnnotations;

public record CreateUserCommand(string Email, string FirstName, string LastName) : IValidatable
{
    public Task<ValidationResult> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
            return Task.FromResult(new ValidationResult("Email is required", new[] { nameof(Email) }));
            
        if (!Email.Contains("@"))
            return Task.FromResult(new ValidationResult("Invalid email format", new[] { nameof(Email) }));
            
        if (string.IsNullOrWhiteSpace(FirstName))
            return Task.FromResult(new ValidationResult("First name is required", new[] { nameof(FirstName) }));
            
        return Task.FromResult(ValidationResult.Success!);
    }
}
```

#### External Validation Handlers
```csharp
public class CreateUserCommandValidator : IValidationHandler<CreateUserCommand>
{
    private readonly IUserRepository _userRepository;
    
    public CreateUserCommandValidator(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }
    
    public async ValueTask<ValidationResult> ValidateAsync(
        CreateUserCommand message, 
        CancellationToken cancellationToken = default)
    {
        var existingUser = await _userRepository.GetByEmailAsync(message.Email, cancellationToken);
        
        if (existingUser != null)
            return new ValidationResult("Email already exists", new[] { nameof(message.Email) });
            
        return ValidationResult.Success!;
    }
}
```

#### Validation Exceptions
When validation fails, NetMediate throws a `MessageValidationException`:

```csharp
try
{
    var command = new CreateUserCommand("", "John", "Doe"); // Invalid email
    await mediator.Send(command);
}
catch (MessageValidationException ex)
{
    Console.WriteLine($"Validation failed: {ex.Message}");
}
```

### Advanced Configuration

#### Keyed Services
```csharp
[KeyedMessage("primary")]
public record PrimaryUserCommand(string Email);

[KeyedMessage("secondary")]
public record SecondaryUserCommand(string Email);

public class PrimaryUserHandler : ICommandHandler<PrimaryUserCommand>
{
    public Task Handle(PrimaryUserCommand command, CancellationToken cancellationToken = default)
    {
        // Handle primary user logic
        return Task.CompletedTask;
    }
}

public class SecondaryUserHandler : ICommandHandler<SecondaryUserCommand>
{
    public Task Handle(SecondaryUserCommand command, CancellationToken cancellationToken = default)
    {
        // Handle secondary user logic
        return Task.CompletedTask;
    }
}
```

#### Custom Configuration
```csharp
builder.Services.AddNetMediate()
    .IgnoreUnhandledMessages(ignore: true, log: true, logLevel: LogLevel.Warning)
    .FilterCommand<CreateUserCommand, CreateUserCommandHandler>(cmd => cmd.Email.EndsWith("@company.com"))
    .InstantiateHandlerByMessageFilter<DynamicMessage>(msg => 
        msg.Type == "urgent" ? typeof(UrgentMessageHandler) : typeof(StandardMessageHandler));
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
