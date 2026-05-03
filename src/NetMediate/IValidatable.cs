using System.ComponentModel.DataAnnotations;

namespace NetMediate;

/// <summary>
/// Represents an object that can be validated asynchronously.
/// </summary>
public interface IValidatable : IMessage
{
    /// <summary>
    /// Validates the current object asynchronously.
    /// </summary>
    /// <returns>
    /// A <see cref="Task{ValidationResult}"/> representing the result of the validation.
    /// </returns>
    Task<ValidationResult> ValidateAsync();
}
