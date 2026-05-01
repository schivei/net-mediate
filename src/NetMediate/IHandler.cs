namespace NetMediate;

/// <summary>
/// Defines a handler that processes a message of a specified type and returns a result.
/// </summary>
/// <remarks>Implementations should ensure thread safety if handlers are used concurrently. The handler is
/// responsible for processing the message and returning an appropriate result. Cancellation is supported via the
/// provided token.</remarks>
/// <typeparam name="TMessage">The type of message to be handled. Must implement <see cref="IMessage"/> and cannot be null.</typeparam>
/// <typeparam name="TResult">The type of result produced by handling the message.</typeparam>
public interface IHandler<TMessage, TResult> : IHandler where TMessage : notnull, IMessage
{
    /// <summary>
    /// Handles the specified message and returns a result of the operation.
    /// </summary>
    /// <param name="message">The message to be processed. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A result of type <typeparamref name="TResult"/> produced by handling the message.</returns>
    TResult Handle(TMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a contract for handling a specific operation or request.
/// </summary>
/// <remarks>Implement this interface to provide custom handling logic for a particular operation type. The
/// details of the operation and the expected behavior are determined by the specific implementation.</remarks>
public interface IHandler;