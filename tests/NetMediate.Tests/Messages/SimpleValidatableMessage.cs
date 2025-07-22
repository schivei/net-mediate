using System.ComponentModel.DataAnnotations;

namespace NetMediate.Tests.Messages;

internal record SimpleValidatableMessage([Required] string Name) : IValidatable
{
    public Task<ValidationResult> ValidateAsync() =>
        Task.FromResult(Name != "right" ? new ValidationResult("Name is required", [nameof(Name)]) : ValidationResult.Success!);
}
