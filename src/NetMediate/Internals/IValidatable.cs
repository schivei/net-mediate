using System.ComponentModel.DataAnnotations;

namespace NetMediate.Internals;

public interface IValidatable
{
    /// <summary>
    /// Validates the current instance asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous validation operation. The task result contains a validation result.</returns>
    Task<ValidationResult> ValidateAsync();
}
