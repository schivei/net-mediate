using System.ComponentModel.DataAnnotations;

namespace NetMediate;

/// <summary>
/// Represents an object that can be validated asynchronously.
/// </summary>
public interface IValidatable
{
    /// <summary>
    /// Validates the current object asynchronously.
    /// </summary>
    /// <returns>
    /// A <see cref="ValidationResult"/> representing the result of the validation.
    /// </returns>
    Task<ValidationResult> ValidateAsync();
}
