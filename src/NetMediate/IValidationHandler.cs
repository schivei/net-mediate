using System.ComponentModel.DataAnnotations;

namespace NetMediate;

/// <summary>
/// Defines a handler for validating messages of type <typeparamref name="TMessage"/>.
/// </summary>
/// <typeparam name="TMessage">The type of the message to validate.</typeparam>
public interface IValidationHandler<TMessage>
{
    /// <summary>
    /// Asynchronously validates the specified message.
    /// </summary>
    /// <param name="message">The message instance to validate.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A <see cref="ValueTask{ValidationResult}"/> representing the asynchronous validation operation.
    /// The result contains a <see cref="ValidationResult"/> indicating the outcome of the validation.
    /// </returns>
    ValueTask<ValidationResult> ValidateAsync(TMessage message, CancellationToken cancellationToken = default);
}
