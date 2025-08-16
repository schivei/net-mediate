using System.ComponentModel.DataAnnotations;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests.ValidationHandlers;

internal sealed class DecoupledValidationHandler : IValidationHandler<DecoupledValidatableMessage>
{
    public ValueTask<ValidationResult> ValidateAsync(
        DecoupledValidatableMessage message,
        CancellationToken cancellationToken = default
    ) =>
        ValueTask.FromResult(
            message.Name == "right"
                ? ValidationResult.Success!
                : new ValidationResult("Name must be 'right'.")
        );
}
