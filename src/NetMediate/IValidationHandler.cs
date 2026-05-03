using System.ComponentModel.DataAnnotations;

namespace NetMediate;

/// <summary>
/// Defines a handler for validating messages of type <typeparamref name="TMessage"/>.
/// </summary>
/// <typeparam name="TMessage">The type of the message to validate.</typeparam>
public interface IValidationHandler<TMessage> : IValidationHandler where TMessage : notnull
{
    /// <summary>
    /// Asynchronously validates the specified message.
    /// </summary>
    /// <param name="message">The message instance to validate.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A <see cref="Task{ValidationResult}"/> representing the asynchronous validation operation.
    /// </returns>
    Task<ValidationResult> ValidateAsync(
        TMessage message,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Defines a handler that performs validation logic within a request handling pipeline.
/// </summary>
/// <remarks>Implement this interface to provide custom validation behavior for requests processed by the
/// pipeline.</remarks>
public interface IValidationHandler;
