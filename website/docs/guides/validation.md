---
sidebar_position: 8
---

# Validation

> **GenDI pattern:** Register validation behaviors as **closed types** and prefer `[Injectable]` + `[Inject]` so the same GenDI-based activation model is used across the application.

NetMediate does not include a built-in validation layer. Validation is a cross-cutting concern that you implement as a pipeline behavior.

## DataAnnotations example

```csharp
using System.ComponentModel.DataAnnotations;
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

public record CreateUserRequest(string Email);
public record UserDto(string Id, string Email);

[Injectable<IPipelineRequestBehavior<CreateUserRequest, UserDto>>(ServiceLifetime.Singleton, Group = 10, Order = 1)]
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
```

## FluentValidation example

```csharp
using FluentValidation;
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

public record CreateUserRequest(string Email);
public record UserDto(string Id, string Email);

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

[Injectable<IPipelineRequestBehavior<CreateUserRequest, UserDto>>(ServiceLifetime.Singleton, Group = 10, Order = 1)]
public sealed class CreateUserFluentValidationBehavior : IPipelineRequestBehavior<CreateUserRequest, UserDto>
{
    [Inject] public required IValidator<CreateUserRequest> Validator { get; init; }

    public async Task<UserDto> Handle(
        object? key,
        CreateUserRequest message,
        PipelineBehaviorDelegate<CreateUserRequest, Task<UserDto>> next,
        CancellationToken cancellationToken)
    {
        var result = await Validator.ValidateAsync(message, cancellationToken);
        if (!result.IsValid)
            throw new ValidationException(result.Errors);

        return await next(key, message, cancellationToken);
    }
}

builder.Services.AddValidatorsFromAssemblyContaining<CreateUserRequestValidator>();
builder.Services.AddNetMediate();
```
