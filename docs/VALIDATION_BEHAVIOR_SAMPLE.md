# Custom Validation Behavior Sample

> **GenDI pattern:** Use `[Injectable]` + `[Inject]` for regular application services. For services whose DI contract is a generic type (for example pipeline behaviors), register them manually in `builder.Services` because GenDI does not support attribute-based registration for that AOT-oriented path.

NetMediate does not include a built-in validation layer. Validation is a cross-cutting concern that you implement as a pipeline behavior.

## Example: DataAnnotations validation for requests

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

public record CreateUserRequest(string Email);
public record UserDto(string Id, string Email);

public sealed class CreateUserDataAnnotationsBehavior : IPipelineRequestBehavior<CreateUserRequest, UserDto>
{
    public Task<UserDto> Handle(
        object? key,
        CreateUserRequest message,
        PipelineBehaviorDelegate<CreateUserRequest, Task<UserDto>> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(message, context, results, validateAllProperties: true))
            throw new MessageValidationException(results[0]);

        return next(key, message, cancellationToken);
    }
}

builder.Services.AddNetMediate();
builder.Services.AddSingleton<IPipelineRequestBehavior<CreateUserRequest, UserDto>, CreateUserDataAnnotationsBehavior>();
```

## Example: FluentValidation for requests

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

public record CreateUserRequest(string Email);
public record UserDto(string Id, string Email);

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

public sealed class CreateUserFluentValidationBehavior(
    IValidator<CreateUserRequest> validator
) : IPipelineRequestBehavior<CreateUserRequest, UserDto>
{
    public async Task<UserDto> Handle(
        object? key,
        CreateUserRequest message,
        PipelineBehaviorDelegate<CreateUserRequest, Task<UserDto>> next,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(message, cancellationToken);
        if (!result.IsValid)
            throw new ValidationException(result.Errors);

        return await next(key, message, cancellationToken);
    }
}

builder.Services.AddValidatorsFromAssemblyContaining<CreateUserRequestValidator>();
builder.Services.AddNetMediate();
builder.Services.AddSingleton<IPipelineRequestBehavior<CreateUserRequest, UserDto>, CreateUserFluentValidationBehavior>();
```

## Example: Notification validation behavior

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

public record UserCreatedNotification(string UserId, string Email);

public sealed class UserCreatedValidationBehavior : IPipelineNotificationBehavior<UserCreatedNotification>
{
    public Task Handle(
        object? key,
        UserCreatedNotification message,
        PipelineBehaviorDelegate<UserCreatedNotification, Task> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(message, context, results, validateAllProperties: true))
            throw new MessageValidationException(results[0]);

        return next(key, message, cancellationToken);
    }
}

builder.Services.AddNetMediate();
builder.Services.AddSingleton<IPipelineNotificationBehavior<UserCreatedNotification>, UserCreatedValidationBehavior>();
```

## `MessageValidationException`

NetMediate ships `MessageValidationException` (in the `NetMediate` namespace) as a convenience type:

```csharp
public sealed class MessageValidationException(ValidationResult result) : Exception(result.ErrorMessage)
{
    public ValidationResult ValidationResult { get; init; }
}
```

You can throw it from any pipeline behavior and catch it in your application's error-handling middleware or exception filters.
