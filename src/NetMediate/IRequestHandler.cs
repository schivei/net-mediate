namespace NetMediate;

/// <summary>
/// Defines a handler for processing a request message and returning a response.
/// </summary>
/// <typeparam name="TMessage">The type of the request message.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface IRequestHandler<in TMessage, TResponse> : IHandler
{
    /// <summary>
    /// Handles the specified request message asynchronously.
    /// </summary>
    /// <param name="query">The request message to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation, containing the response.</returns>
    Task<TResponse> Handle(TMessage query, CancellationToken cancellationToken = default);
}
