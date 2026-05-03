using System.ComponentModel.DataAnnotations;

namespace NetMediate.Tests.Messages;

internal record SimpleValidatableMessage([Required] string Name) : BaseMessage, IValidatable, INotification
{
    public Task<ValidationResult> ValidateAsync() =>
        Task.FromResult(
            Name != "right"
                ? new("Name is required", [nameof(Name)])
                : ValidationResult.Success!
        );
}
