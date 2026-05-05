---
sidebar_position: 6
---

# Custom Validation Behavior Sample

NetMediate does not include a built-in validation layer. Validation is a cross-cutting concern that you implement as a **pipeline behavior**, giving you full control over the validation library and strategy.

## Example: DataAnnotations validation for requests

```csharp
using System.ComponentModel.DataAnnotations;
using NetMediate;

/// <summary>
/// Validates the incoming request using DataAnnotations before forwarding to the handler.
/// Throws <see cref="MessageValidationException"/> when validation fails.
/// </summary>
public sealed class DataAnnotationsRequestBehavior<TMessage, TResponse>
    : IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{
    public Task<TResponse> Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(message, context, results, validateAllProperties: true))
        {
            // MessageValidationException is provided by NetMediate and accepts a ValidationResult.
            throw new MessageValidationException(results[0]);
        }

        return next(key, message, cancellationToken);
    }
}
```

### Registration

```csharp
// Open-generic: validates every request type
builder.Services.AddSingleton(
    typeof(IPipelineRequestBehavior<,>),
    typeof(DataAnnotationsRequestBehavior<,>));
```

## Example: FluentValidation for requests

```csharp
using FluentValidation;
using NetMediate;

public sealed class FluentValidationRequestBehavior<TMessage, TResponse>(
    IValidator<TMessage>? validator = null
) : IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{
    public async Task<TResponse> Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        if (validator is not null)
        {
            var result = await validator.ValidateAsync(message, cancellationToken);
            if (!result.IsValid)
                throw new ValidationException(result.Errors);
        }

        return await next(key, message, cancellationToken);
    }
}
```

### Registration

```csharp
// Register FluentValidation validators from your assembly
builder.Services.AddValidatorsFromAssemblyContaining<MyValidator>();

// Register the behavior open-generic
builder.Services.AddSingleton(
    typeof(IPipelineRequestBehavior<,>),
    typeof(FluentValidationRequestBehavior<,>));
```

## Example: Notification validation behavior

The same approach works for notifications:

```csharp
public sealed class DataAnnotationsNotificationBehavior<TMessage>
    : IPipelineBehavior<TMessage>
    where TMessage : notnull
{
    public Task Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(message, context, results, validateAllProperties: true))
            throw new MessageValidationException(results[0]);

        return next(key, message, cancellationToken);
    }
}
```

```csharp
// Open-generic: validates every notification type
builder.Services.AddSingleton(
    typeof(IPipelineBehavior<>),
    typeof(DataAnnotationsNotificationBehavior<>));
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
