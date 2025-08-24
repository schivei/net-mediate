namespace NetMediate;

/// <summary>
/// Defines a handler for processing a message and returning a stream of responses asynchronously.
/// </summary>
/// <typeparam name="TMessage">The type of the message to handle.</typeparam>
/// <typeparam name="TResponse">The type of the response returned in the stream.</typeparam>
public interface IStreamHandler<in TMessage, TResponse> : IHandler
{
    /// <summary>
    /// Handles the specified message and returns an asynchronous stream of responses.
    /// </summary>
    /// <param name="query">The message to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An <see cref="IAsyncEnumerable{TResponse}"/> representing the stream of responses.</returns>
    IAsyncEnumerable<TResponse> Handle(
        TMessage query,
        CancellationToken cancellationToken = default
    );
}
