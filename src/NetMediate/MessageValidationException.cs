using System.ComponentModel.DataAnnotations;

namespace NetMediate;

/// <summary>
/// Represents an exception that is thrown when a message fails validation.
/// </summary>
/// <remarks>Use this exception to indicate that a message did not meet validation requirements. The associated
/// ValidationResult provides details about the validation failure.</remarks>
/// <param name="result">The result of the validation operation that caused the exception. Cannot be null.</param>
public sealed class MessageValidationException(ValidationResult result) : Exception(result.ErrorMessage)
{
    /// <summary>
    /// Gets the result of the validation operation.
    /// </summary>
    public ValidationResult ValidationResult { get; init; } = result;
}
