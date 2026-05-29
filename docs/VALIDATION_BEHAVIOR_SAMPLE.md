# Custom Validation Behavior Sample

> **GenDI pattern:** Use `[Injectable]` + `[Inject]` for regular application services. Concrete non-generic validation behaviors can use `[DecoratorFor<>]`. Only generic/open behavior implementations should be registered manually in `builder.Services`.

NetMediate does not include a built-in validation layer. Validation is a cross-cutting concern that you implement as a pipeline behavior.

## Example: DataAnnotations validation for requests

```csharp
using System.ComponentModel.DataAnnotations;
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

public record CreateUserRequest(string Email);
public record UserDto(string Id, string Email);

[DecoratorFor]
public sealed class CreateUserDataAnnotationsBehavior : IRequestHandler<CreateUserRequest, UserDto>
{
    [Inject] public required IRequestHandler<CreateUserRequest, UserDto> Next { get; init; }

    public ValueTask<UserDto> Handle(
        CreateUserRequest message,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(message, context, results, validateAllProperties: true))
            throw new MessageValidationException(results[0]);

        return Next(message, cancellationToken);
    }
}

builder.Services.AddNetMediate();
```

## Example: FluentValidation

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

[DecoratorFor]
public sealed class CreateUserFluentValidationBehavior : IRequestHandler<CreateUserRequest, UserDto>
{
    [Inject] public requried IRequestHandler<CreateUserRequest, UserDto> Next { get; init; }

    [Inject] public required IValidator<CreateUserRequest> Validator { get; init; }

    public async ValueTask<UserDto> Handle(
        CreateUserRequest message,
        CancellationToken cancellationToken)
    {
        var result = await Validator.ValidateAsync(message, cancellationToken);
        if (!result.IsValid)
            throw new ValidationException(result.Errors);

        return await Next(message, cancellationToken);
    }
}

builder.Services.AddValidatorsFromAssemblyContaining<CreateUserRequestValidator>();
builder.Services.AddNetMediate();
```
