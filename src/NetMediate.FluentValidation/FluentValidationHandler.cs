using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

namespace NetMediate.FluentValidation;

/// <summary>
/// Bridges a FluentValidation <see cref="AbstractValidator{T}"/> (or any
/// <see cref="IValidator{T}"/> implementation) into the NetMediate validation
/// pipeline by implementing <see cref="IValidationHandler{TMessage}"/>.
/// </summary>
/// <typeparam name="TMessage">The message type to validate.</typeparam>
/// <remarks>
/// Register this handler through
/// <see cref="FluentValidationMediatorExtensions.AddFluentValidation{TMessage,TValidator}"/>
/// rather than directly.
/// </remarks>
public sealed class FluentValidationHandler<TMessage>(IValidator<TMessage> validator)
    : IValidationHandler<TMessage>
{
    /// <inheritdoc />
    public async ValueTask<System.ComponentModel.DataAnnotations.ValidationResult> ValidateAsync(
        TMessage message,
        CancellationToken cancellationToken = default)
    {
        var result = await validator.ValidateAsync(message, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsValid)
            return System.ComponentModel.DataAnnotations.ValidationResult.Success!;

        var memberNames = result.Errors.Select(e => e.PropertyName).Distinct().ToArray();
        var errorMessage = string.Join("; ", result.Errors.Select(e => e.ErrorMessage));
        return new System.ComponentModel.DataAnnotations.ValidationResult(errorMessage, memberNames);
    }
}
